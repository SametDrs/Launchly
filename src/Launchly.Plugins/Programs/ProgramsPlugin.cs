using System.Diagnostics;
using Launchly.Core;
using Launchly.Core.Querying;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Programs;

/// <summary>Start Menu uygulamalarını bulur, ikonlarıyla listeler ve açar.</summary>
public sealed class ProgramsPlugin : IPlugin, IDisposable
{
    public const string PluginId = "launchly.programs";
    private const int MaxReturned = 50;

    private readonly ILogger _logger;
    private readonly JsonStorage<ProgramCache> _cacheStorage;
    private readonly ProgramScanner _scanner;
    private readonly Settings? _settings;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IconLoader _icons = new();
    private readonly Action<string> _launch;
    private readonly object _saveGate = new();
    private volatile ProgramEntry[] _entries = [];
    private int _disposed;

    /// <summary>DI için: gerçek Start Menu klasörleri ve <c>%AppData%\Launchly\cache\programs.json</c>.</summary>
    public ProgramsPlugin(ILogger<ProgramsPlugin> logger, Settings settings)
        : this(
            logger,
            Path.Combine(DataLocation.RoamingDirectory, "cache", "programs.json"),
            new ProgramScanner(ProgramScanner.DefaultRoots(), logger: logger),
            LaunchShortcut,
            settings)
    {
    }

    internal ProgramsPlugin(ILogger logger, string cachePath, ProgramScanner scanner, Action<string> launch, Settings? settings = null)
    {
        _settings = settings;
        _logger = logger ?? NullLogger.Instance;
        _cacheStorage = new JsonStorage<ProgramCache>(cachePath, _logger);
        _scanner = scanner;
        _launch = launch;
    }

    public PluginMetadata Metadata { get; } = new()
    {
        Id = PluginId,
        Name = "Programs",
        ActionKeywords = [PluginMetadata.GlobalKeyword],
        HandlesEmptyQuery = true,
    };

    /// <summary>Son (ya da süren) arka plan taraması; testler bekler.</summary>
    internal Task RefreshTask { get; private set; } = Task.CompletedTask;

    internal int Count => _entries.Length;

    public Task InitAsync(PluginContext context)
    {
        // Önbellek hızlı yüklenir; asıl tarama arka planda yapılır, açılışı geciktirmez.
        try
        {
            _entries = _cacheStorage.Load().Programs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Programs önbelleği yüklenemedi");
        }

        var token = _shutdown.Token;
        RefreshTask = Task.Run(() => RefreshCore(token));
        return Task.CompletedTask;
    }

    /// <summary>Uygulama kapanırken arka plan taramasını iptal eder; yarım tarama önbelleğe yazılmaz.</summary>
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

        // _shutdown Dispose edilmez: tarama iş parçacığı token'ı hâlâ kullanıyor olabilir (yalnızca bellek, yönetilmeyen kaynak yok).
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        var entries = _entries;
        IEnumerable<(ProgramEntry Entry, int Score)> scored;

        if (string.IsNullOrWhiteSpace(query.Search))
        {
            // Girdiler tarama sırasında ada göre sıralıdır; eşit puanda sıra korunur.
            scored = entries.Select(e => (e, 0));
        }
        else
        {
            scored = entries
                .Select(e => (Entry: e, Match: StringMatcher.Match(query.Search, e.Name)))
                .Where(x => x.Match.IsMatch)
                .Select(x => (x.Entry, x.Match.Score))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        // İkon yüklemek pahalı: yalnızca listede gösterilebilecek kadar (Settings.MaxResults) sonuç üretilir.
        var limit = _settings is null ? MaxReturned : Math.Clamp(_settings.MaxResults, 1, MaxReturned);
        var results = new List<Result>();
        foreach (var (entry, score) in scored.Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = entry.ShortcutPath;
            results.Add(new Result
            {
                Title = entry.Name,
                SubTitle = entry.TargetPath,
                Score = score,
                TypeLabel = "Application",
                IconImage = _icons.Get(path),
                Action = () => Task.FromResult(TryLaunch(path)),
            });
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private void RefreshCore(CancellationToken cancellationToken)
    {
        try
        {
            var scanned = _scanner.Scan(cancellationToken);
            lock (_saveGate)
            {
                // İptal edilmiş (Dispose) tarama ne listeyi ne önbelleği yazar.
                cancellationToken.ThrowIfCancellationRequested();
                _entries = scanned.ToArray();
                _cacheStorage.Save(new ProgramCache { ScannedAtUtc = DateTime.UtcNow, Programs = scanned });
            }

            _logger.LogInformation("Programs taraması bitti: {Count} uygulama", scanned.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Programs taraması iptal edildi");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Programs taraması başarısız");
        }
    }

    private bool TryLaunch(string path)
    {
        try
        {
            _launch(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uygulama açılamadı: {Path}", path);
            return false;
        }
    }

    private static void LaunchShortcut(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
