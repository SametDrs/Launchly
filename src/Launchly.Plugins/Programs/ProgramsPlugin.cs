using System.ComponentModel;
using Launchly.Core;
using Launchly.Core.Querying;
using Launchly.Core.Storage;
using Launchly.Core.Localization;
using Launchly.Plugin;
using Launchly.Plugins.Calculator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Programs;

/// <summary>
/// Start Menu kısayollarını ve <c>shell:AppsFolder</c> uygulamalarını (Store/MSIX dahil) bulur, ikonlarıyla listeler ve açar.
/// Boş kutuda en çok kullanılanları (yoksa başlangıç listesini) gösterir; aramada kullanım puanı sıralamaya katılır.
/// </summary>
public sealed class ProgramsPlugin : IPlugin, IDisposable
{
    public const string PluginId = "launchly.programs";
    private const int MaxReturned = 50;

    /// <summary>Kullanım bonusu üst sınırı: StringMatcher bantları 100 puan aralıklı, bonus bant atlatmaz.</summary>
    internal const int MaxUsageBonus = 50;

    private readonly ILogger _logger;
    private readonly JsonStorage<ProgramCache> _cacheStorage;
    private readonly ProgramScanner _scanner;
    private readonly IAppsFolderSource _appsFolder;
    private readonly ProgramUsageStore _usage;
    private readonly bool _ownsUsage;
    private readonly Func<string?> _defaultBrowser;
    private readonly Settings? _settings;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IconLoader _icons = new();
    private readonly IProgramLauncher _launcher;
    private readonly IClipboardService? _clipboard;
    private readonly object _saveGate = new();
    private volatile ProgramEntry[] _entries = [];
    private int _disposed;

    /// <summary>DI için: gerçek Start Menu klasörleri + AppsFolder, <c>%AppData%\Launchly\cache\programs.json</c>.</summary>
    public ProgramsPlugin(ILogger<ProgramsPlugin> logger, Settings settings, ProgramUsageStore usage, IClipboardService clipboard)
        : this(
            logger,
            Path.Combine(DataLocation.RoamingDirectory, "cache", "programs.json"),
            new ProgramScanner(ProgramScanner.DefaultRoots(), logger: logger),
            new ShellProgramLauncher(),
            settings,
            new AppsFolderScanner(),
            usage,
            DefaultBrowser.GetExecutablePath,
            clipboard)
    {
    }

    /// <param name="usage">Verilmezse önbelleğin yanında (<c>usage.json</c>) eklentiye ait bir kayıt açılır ve Dispose'da kapatılır.</param>
    /// <param name="defaultBrowser">Varsayılan tarayıcı exe yolu; verilmezse tarayıcı başlangıç listesinde yoktur.</param>
    /// <param name="clipboard">Verilmezse "Yolu kopyala" eylemi sunulmaz.</param>
    internal ProgramsPlugin(
        ILogger logger,
        string cachePath,
        ProgramScanner scanner,
        IProgramLauncher launcher,
        Settings? settings = null,
        IAppsFolderSource? appsFolder = null,
        ProgramUsageStore? usage = null,
        Func<string?>? defaultBrowser = null,
        IClipboardService? clipboard = null)
    {
        _settings = settings;
        _logger = logger ?? NullLogger.Instance;
        _cacheStorage = new JsonStorage<ProgramCache>(cachePath, _logger);
        _scanner = scanner;
        _launcher = launcher;
        _clipboard = clipboard;
        _appsFolder = appsFolder ?? EmptyAppsFolderSource.Instance;
        _ownsUsage = usage is null;
        _usage = usage ?? new ProgramUsageStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cachePath))!, "usage.json"), logger: _logger);
        _defaultBrowser = defaultBrowser ?? (() => null);
    }

    public PluginMetadata Metadata => new()
    {
        Id = PluginId,
        Name = Loc.T("plugin.programs.name"),
        Description = Loc.T("plugin.programs.desc"),
        ActionKeywords = [PluginMetadata.GlobalKeyword],
        HandlesEmptyQuery = true,
    };

    /// <summary>Son (ya da süren) arka plan taraması; testler bekler.</summary>
    internal Task RefreshTask { get; private set; } = Task.CompletedTask;

    internal int Count => _entries.Length;

    internal IReadOnlyList<ProgramEntry> Entries => _entries;

    public Task InitAsync(PluginContext context)
    {
        // Önbellek hızlı yüklenir; asıl tarama arka planda yapılır, açılışı geciktirmez.
        try
        {
            // v1 önbellekte Kind/AppUserModelId yoktur: Shortcut ve "" olur. İlk tarama dosyayı v2 olarak yeniden yazar.
            _entries = ProgramCatalog.Sanitize(_cacheStorage.Load().Programs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Programs önbelleği yüklenemedi");
        }

        var token = _shutdown.Token;
        RefreshTask = Task.Run(() => RefreshCore(token));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Uygulama kapanırken arka plan taramasını iptal eder (yarım tarama önbelleğe yazılmaz) ve bekleyen kullanım
    /// yazımını bir kez, sınırlı süre bekleyerek bitirir. Sonra gelen açılışlar kaydedilmez.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();

        // UI'ı taramanın bitmesini bekleyerek bloklamayız. Yalnızca sürmekte olan (kısa) önbellek yazımı bitsin diye
        // kilitten geçilir; bundan sonra başlayan her yazım iptali görüp vazgeçer.
        lock (_saveGate)
        {
        }

        if (_ownsUsage)
        {
            _usage.Dispose();
        }
        else
        {
            _usage.Flush();   // paylaşılan tekil kayıt: DI kapatır; burada yalnızca bekleyen yazım biter
        }

        // _shutdown Dispose edilmez: tarama iş parçacığı token'ı hâlâ kullanıyor olabilir (yalnızca bellek, yönetilmeyen kaynak yok).
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        // Gizli küme kilitsiz anlık görüntüden okunur; gizlenenler hem boş listeden hem aramadan çıkar.
        var hidden = _usage.HiddenIds;
        var entries = hidden.Count == 0 ? _entries : _entries.Where(e => !hidden.Contains(e.Id)).ToArray();

        // İkon yüklemek pahalı: yalnızca listede gösterilebilecek kadar (Settings.MaxResults) sonuç üretilir.
        var limit = _settings is null ? MaxReturned : Math.Clamp(_settings.MaxResults, 1, MaxReturned);
        IEnumerable<(ProgramEntry Entry, int Score)> scored;

        if (string.IsNullOrWhiteSpace(query.Search))
        {
            limit = Math.Min(Settings.EmptyQueryMaxResults, limit);
            scored = EmptyQueryList(entries, hidden, limit).Select(e => (e, 0));   // sıra korunur (eşit puanda kararlı sıralama)
        }
        else
        {
            scored = entries
                .Select(e => (Entry: e, Match: StringMatcher.Match(query.Search, e.Name)))
                .Where(x => x.Match.IsMatch)
                .Select(x => (x.Entry, Score: x.Match.Score + UsageBonus(x.Entry.Id)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        var results = new List<Result>();
        foreach (var (entry, score) in scored.Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captured = entry;
            results.Add(new Result
            {
                Title = entry.Name,
                SubTitle = entry.TargetPath,
                Score = score,
                TypeLabel = Loc.T("type.application"),
                IconImage = IconFor(entry),
                Action = () => Task.FromResult(TryLaunch(captured, asAdmin: false)),
                ContextActions = BuildActions(captured),
            });
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    /// <summary>Arama bonusu: <c>min(50, round(Frecency x 10))</c>.</summary>
    internal int UsageBonus(string id)
    {
        var frecency = _usage.Frecency(id);
        return frecency <= 0 ? 0 : (int)Math.Min(MaxUsageBonus, Math.Round(frecency * 10, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Boş kutu: önce sabitlenenler (sabitleme sırasıyla), sonra kullanım kaydındakiler (frecency azalan), sonra kalan
    /// yer başlangıç listesiyle dolar (zaten listede olan atlanır). Artık bulunmayan kimlik (kaldırılmış uygulama) yer
    /// kaplamadan atlanır ama kayıtta kalır. Gizlenenler hiçbir aşamada gösterilmez. Toplam <paramref name="limit"/>.
    /// </summary>
    private List<ProgramEntry> EmptyQueryList(ProgramEntry[] entries, IReadOnlySet<string> hidden, int limit)
    {
        var starters = StarterApps.Resolve(entries, _defaultBrowser);
        var byId = new Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            byId.TryAdd(e.Id, e);
        }

        foreach (var s in starters)
        {
            byId.TryAdd(s.Id, s);   // Command yedekleri taranmaz; kayıttaki "cmd:" kimlikleri buradan bulunur
        }

        var list = new List<ProgramEntry>(limit);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(ProgramEntry entry)
        {
            if (list.Count < limit && !hidden.Contains(entry.Id) && added.Add(entry.Id))
            {
                list.Add(entry);
            }
        }

        foreach (var id in _usage.PinnedIds)
        {
            if (byId.TryGetValue(id, out var entry))
            {
                Add(entry);
            }
        }

        foreach (var id in _usage.TopIds(int.MaxValue))
        {
            if (list.Count >= limit)
            {
                break;
            }

            if (byId.TryGetValue(id, out var entry))
            {
                Add(entry);
            }
        }

        foreach (var s in starters)
        {
            Add(s);
        }

        return list;
    }

    private System.Windows.Media.ImageSource? IconFor(ProgramEntry entry) => entry.Kind switch
    {
        ProgramKind.AppsFolder => _icons.GetForParsingName(entry.AppUserModelId),
        ProgramKind.Command => CommandIconPath(entry.ShortcutPath) is { } path ? _icons.Get(path) : null,
        _ => _icons.Get(entry.ShortcutPath),
    };

    // "notepad.exe" -> System32/Windows altındaki gerçek dosya; "ms-settings:" gibi protokollerin ikonu yok (harf karosu).
    private static string? CommandIconPath(string command)
    {
        if (command.Length == 0 || command.EndsWith(':'))
        {
            return null;
        }

        if (Path.IsPathRooted(command))
        {
            return command;
        }

        foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void RefreshCore(CancellationToken cancellationToken)
    {
        try
        {
            var lnk = _scanner.Scan(cancellationToken);

            IReadOnlyList<(string ParsingName, string DisplayName)> apps;
            try
            {
                apps = _appsFolder.Enumerate(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // AppsFolder tek başına bozulursa .lnk sonuçlarıyla devam edilir.
                SafeLog(l => l.LogWarning(ex, "AppsFolder taranamadı; yalnızca Start Menu kısayolları kullanılıyor"));
                apps = [];
            }

            var merged = ProgramCatalog.Merge(lnk, apps);
            int kept;
            lock (_saveGate)
            {
                // İptal edilmiş (Dispose) tarama ne listeyi ne önbelleği yazar.
                cancellationToken.ThrowIfCancellationRequested();

                // Boş tarama (kök yok, hepsi hata verdi vb.) dolu listenin/önbelleğin üzerine yazılmaz.
                kept = merged.Count == 0 ? _entries.Length : 0;
                if (kept == 0)
                {
                    _entries = merged.ToArray();
                    _cacheStorage.Save(new ProgramCache { ScannedAtUtc = DateTime.UtcNow, Programs = merged });
                }
            }

            if (merged.Count == 0)
            {
                SafeLog(l => l.LogWarning("Programs taraması 0 uygulama buldu; mevcut liste korundu ({Kept} uygulama)", kept));
            }
            else
            {
                SafeLog(l => l.LogInformation(
                    "Programs taraması bitti: {Count} uygulama ({Lnk} kısayol, {Apps} AppsFolder öğesi)", merged.Count, lnk.Count, apps.Count));
            }
        }
        catch (OperationCanceledException)
        {
            SafeLog(l => l.LogInformation("Programs taraması iptal edildi"));
        }
        catch (Exception ex)
        {
            SafeLog(l => l.LogError(ex, "Programs taraması başarısız"));
        }
    }

    // Dispose sonrası (logger'lar da kapanmış olabilir) loglama atlanır; logger istisnası arka plan task'ını bozmaz.
    private void SafeLog(Action<ILogger> log)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            log(_logger);
        }
        catch (Exception)
        {
            // Loglama hatası taramayı etkilemez.
        }
    }

    /// <summary>
    /// Menü eylemleri (<see cref="ProgramEntry.Kind"/>'a göre). Paketli (AppsFolder) uygulamada <c>runas</c> uygulanamaz
    /// ve kurulum klasörü açılmaz: yönetici ve konum eylemi yoktur. "Aç" eklenmez (arayüz <see cref="Result.Action"/>'dan ekler).
    /// </summary>
    internal IReadOnlyList<ResultAction> BuildActions(ProgramEntry entry)
    {
        var actions = new List<ResultAction>();
        if (CanRunAsAdmin(entry))
        {
            actions.Add(new ResultAction
            {
                Id = ResultAction.RunAsAdminId,
                Title = Loc.T("action.runAsAdmin"),
                Glyph = "",   // Admin
                Hint = "Ctrl+Shift+Enter",
                Execute = () => Task.FromResult(TryLaunch(entry, asAdmin: true)),
            });
        }

        if (RevealPath(entry) is { } reveal)
        {
            actions.Add(new ResultAction
            {
                Title = Loc.T("action.reveal"),
                Glyph = "",   // FolderOpen
                Execute = () => Task.FromResult(TryReveal(reveal)),
            });
        }

        if (_clipboard is { } clipboard && CopyPath(entry) is { Length: > 0 } copy)
        {
            actions.Add(new ResultAction
            {
                Title = Loc.T("action.copyPath"),
                Glyph = "",   // Copy
                Execute = () => Task.FromResult(TryCopy(clipboard, copy)),
            });
        }

        var id = entry.Id;
        var name = entry.Name;
        actions.Add(new ResultAction
        {
            Title = Loc.T("action.hide"),
            Glyph = "",   // Hide
            RequeryAfter = true,
            Execute = () => Task.FromResult(ChangePrefs(() => _usage.Hide(id, name), id)),
        });

        // Başlık sorgu anındaki duruma göre; eylem başlığın söylediğini yapar (arada değiştiyse etkisiz kalır).
        var pinned = _usage.IsPinned(id);
        actions.Add(new ResultAction
        {
            Title = pinned ? Loc.T("action.unpin") : Loc.T("action.pin"),
            Glyph = pinned ? "" : "",   // Unpin / Pin
            RequeryAfter = true,
            Execute = () => Task.FromResult(ChangePrefs(() => pinned ? _usage.Unpin(id) : _usage.Pin(id), id)),
        });

        return actions;
    }

    // Command girdisinde protokol (ör. "ms-settings:") yönetici olarak açılamaz.
    private static bool CanRunAsAdmin(ProgramEntry entry) => entry.Kind switch
    {
        ProgramKind.Shortcut => entry.ShortcutPath.Length > 0,
        ProgramKind.Command => entry.ShortcutPath.Length > 0 && !entry.ShortcutPath.EndsWith(':'),
        _ => false,
    };

    /// <summary>Gezgin'de seçilecek dosya: hedef doluysa ve varsa o, yoksa .lnk. Command: çözülen dosya. AppsFolder: yok.</summary>
    internal static string? RevealPath(ProgramEntry entry) => entry.Kind switch
    {
        ProgramKind.Shortcut when entry.TargetPath.Length > 0 && File.Exists(entry.TargetPath) => entry.TargetPath,
        ProgramKind.Shortcut when entry.ShortcutPath.Length > 0 => entry.ShortcutPath,
        ProgramKind.Command => CommandIconPath(entry.ShortcutPath),
        _ => null,
    };

    /// <summary>Panoya yazılacak yol: hedef doluysa o, yoksa .lnk; Command: çözülen dosya ya da komut; AppsFolder: <c>shell:AppsFolder\&lt;AUMID&gt;</c>.</summary>
    internal static string CopyPath(ProgramEntry entry) => entry.Kind switch
    {
        ProgramKind.AppsFolder => entry.LaunchPath,
        ProgramKind.Command => CommandIconPath(entry.ShortcutPath) ?? entry.ShortcutPath,
        _ => entry.TargetPath.Length > 0 ? entry.TargetPath : entry.ShortcutPath,
    };

    // Gizle/sabitle: bellek anında güncellenir, disk debounce ile. Pencere açık kalır (false); liste RequeryAfter ile yenilenir.
    private bool ChangePrefs(Func<bool> change, string id)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            change();
        }
        catch (Exception ex)
        {
            SafeLog(l => l.LogWarning(ex, "Gizleme/sabitleme kaydedilemedi: {Id}", id));
        }

        return false;
    }

    private bool TryReveal(string path)
    {
        try
        {
            _launcher.Reveal(path);
            return true;
        }
        catch (Exception ex)
        {
            SafeLog(l => l.LogError(ex, "Dosya konumu açılamadı: {Path}", path));
            return false;
        }
    }

    private bool TryCopy(IClipboardService clipboard, string text)
    {
        try
        {
            clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            SafeLog(l => l.LogError(ex, "Yol panoya yazılamadı"));
            return false;
        }
    }

    private bool TryLaunch(ProgramEntry entry, bool asAdmin)
    {
        var path = entry.LaunchPath;
        try
        {
            if (asAdmin)
            {
                _launcher.LaunchAsAdmin(path);
            }
            else
            {
                _launcher.Launch(path);
            }
        }
        catch (Win32Exception ex) when (asAdmin && ex.NativeErrorCode == ShellProgramLauncher.ErrorCancelled)
        {
            // Kullanıcı UAC istemini reddetti: hata değil; pencere açık kalır, kullanım sayılmaz.
            SafeLog(l => l.LogInformation("Yönetici olarak çalıştırma kullanıcı tarafından iptal edildi: {Path}", path));
            return false;
        }
        catch (Exception ex)
        {
            SafeLog(l => l.LogError(ex, "Uygulama açılamadı: {Path}", path));
            return false;
        }

        // Yalnızca başarılı açılış sayılır. Kayıt belleği günceller; disk yazımı arka planda (debounce).
        if (Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                _usage.RecordLaunch(entry.Id);
            }
            catch (Exception ex)
            {
                SafeLog(l => l.LogWarning(ex, "Kullanım kaydı güncellenemedi: {Id}", entry.Id));
            }
        }

        return true;
    }
}
