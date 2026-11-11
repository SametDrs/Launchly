using System.Threading.Channels;
using Launchly.Core.Querying;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;

namespace Launchly.Core.Plugins;

/// <summary>
/// Eklentileri yükler, sorguyu anahtar kelimeye göre yönlendirir, eklentileri paralel çalıştırır ve
/// sonuçları toplu (~20 ms) birleştirip Score'a göre sıralı yayınlar. Yeni sorgu öncekini iptal eder;
/// iptal edilmiş sorgunun sonucu hiçbir zaman yayınlanmaz.
/// </summary>
public sealed class PluginManager : IDisposable
{
    private readonly IReadOnlyList<IPlugin> _plugins;
    private readonly Settings _settings;
    private readonly ILogger<PluginManager> _logger;
    private readonly string _dataRoot;
    private readonly object _gate = new();
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private Dictionary<string, List<IPlugin>> _byKeyword = new(KeywordComparer.Instance);
    private List<IPlugin> _global = [];
    private CancellationTokenSource? _current;

    public PluginManager(IEnumerable<IPlugin> plugins, Settings settings, ILogger<PluginManager> logger)
        : this(plugins, settings, logger, Path.Combine(DataLocation.RoamingDirectory, "Plugins"))
    {
    }

    internal PluginManager(IEnumerable<IPlugin> plugins, Settings settings, ILogger<PluginManager> logger, string dataRoot)
    {
        _plugins = plugins.ToList();
        _settings = settings;
        _logger = logger;
        _dataRoot = dataRoot;
        RebuildIndex(_plugins);
    }

    /// <summary>Sonuçlar bu süre boyunca biriktirilip tek seferde yayınlanır.</summary>
    internal TimeSpan MergeWindow { get; init; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Etkin eklentilerin anahtar kelimeleri (global "*" hariç).</summary>
    public IReadOnlyCollection<string> ActionKeywords
    {
        get
        {
            lock (_gate)
            {
                return _byKeyword
                    .Where(kv => kv.Value.Any(IsEnabled))
                    .Select(kv => kv.Key)
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// <paramref name="exceptPluginId"/> dışındaki etkin eklentilerin anahtar kelimeleri (global "*" hariç).
    /// Ayarlarda tanımlanan özel kaynakların bu kelimelerle çakışması reddedilir.
    /// </summary>
    public IReadOnlyCollection<string> ReservedKeywords(string exceptPluginId)
    {
        lock (_gate)
        {
            return _byKeyword
                .Where(kv => kv.Value.Any(p => IsEnabled(p) && !string.Equals(p.Metadata.Id, exceptPluginId, StringComparison.Ordinal)))
                .Select(kv => kv.Key)
                .ToArray();
        }
    }

    /// <summary>Tüm eklentileri paralel başlatır. Başlatması başarısız olan eklenti devre dışı bırakılır.</summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_plugins.Select(InitPluginAsync)).ConfigureAwait(false);
        lock (_gate)
        {
            RebuildIndex(_plugins.Where(p => !_failed.Contains(p.Metadata.Id)));
        }
    }

    /// <summary>
    /// Anahtar kelime dizinini eklentilerin güncel <c>Metadata</c>'sından yeniden kurar (ör. ayar penceresinde
    /// web kaynakları değişince). Başlatması başarısız olan eklentiler dışarıda kalır.
    /// </summary>
    public void RefreshKeywords()
    {
        lock (_gate)
        {
            RebuildIndex(_plugins.Where(p => !_failed.Contains(p.Metadata.Id)));
        }
    }

    /// <summary>Kutu boşken çalışacak etkin bir eklenti var mı (ana ekran listesi).</summary>
    public bool HasEmptyQueryPlugins
    {
        get
        {
            lock (_gate)
            {
                return _global.Any(p => p.Metadata.HandlesEmptyQuery && IsEnabled(p));
            }
        }
    }

    /// <summary>Metni etkin anahtar kelimelere göre <see cref="Query"/>'ye çevirir.</summary>
    public Query BuildQuery(string? raw) => QueryBuilder.Build(raw, ActionKeywords);

    /// <summary>Çalışan sorguyu iptal eder (yayın yapılmaz).</summary>
    public void CancelCurrent()
    {
        lock (_gate)
        {
            CancelLocked();
        }
    }

    /// <summary>
    /// Sorguyu çalıştırır; öncekini iptal eder. <paramref name="publish"/>, her toplu birleştirmeden sonra
    /// o ana kadarki tüm sonuçları (sıralı, <see cref="Settings.MaxResults"/> ile sınırlı) verir; en az bir kez
    /// çağrılır (sonuç yoksa boş liste). Çağrı, yayın sırasında kilit altındadır: kısa tutun (ör. UI'a Post).
    /// Eklentiler bitince ya da sorgu iptal edilince döner.
    /// </summary>
    public Task QueryAsync(string? raw, Action<IReadOnlyList<Result>> publish, CancellationToken cancellationToken = default)
    {
        // Son yayın, değişiklik yoksa önceki anlık görüntünün aynısıdır (aynı nesne); burada tekrar verilmez.
        IReadOnlyList<Result>? last = null;
        return QueryAsync(raw, (results, _) =>
        {
            if (!ReferenceEquals(results, last))
            {
                last = results;
                publish(results);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Tek parametreli yayın temsilcisi alan aşırı yüklemeyle aynı; ek olarak
    /// <c>isFinal</c>: tüm eklentiler döndükten sonraki son yayında true (sorgu başına tam bir kez; iptal edilen
    /// sorguda hiç). Son yayın, son toplu yayından sonra yeni sonuç gelmediyse önceki listenin aynı nesnesidir.
    /// Bir sorgunun yayınları sıralıdır ve aynı anda iki tane çalışmaz.
    /// </summary>
    public async Task QueryAsync(string? raw, Action<IReadOnlyList<Result>, bool> publish, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;

        lock (_gate)
        {
            CancelLocked();
            _current = cts;
        }

        try
        {
            var query = BuildQuery(raw);
            var plugins = SelectPlugins(query);

            var emptyQuery = string.IsNullOrWhiteSpace(query.Raw);
            if (emptyQuery)
            {
                // Kutu boş: yalnızca açıkça isteyen eklentiler çalışır (ör. uygulama listesi).
                plugins = plugins.Where(p => p.Metadata.HandlesEmptyQuery).ToList();
            }

            if (plugins.Count == 0)
            {
                PublishIfCurrent(cts, publish, [], isFinal: true);
                return;
            }

            var channel = Channel.CreateUnbounded<Result[]>(new UnboundedChannelOptions { SingleReader = true });
            var producers = plugins.Select(p => RunPluginAsync(p, query, channel.Writer, token)).ToArray();
            _ = Task.WhenAll(producers).ContinueWith(
                _ => channel.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            await ConsumeAsync(channel.Reader, cts, publish, emptyQuery).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Yeni sorgu geldi; sessizce çık.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, cts))
                {
                    _current = null;
                }
            }
        }
    }

    public void Dispose()
    {
        CancelCurrent();
    }

    private async Task ConsumeAsync(ChannelReader<Result[]> reader, CancellationTokenSource cts, Action<IReadOnlyList<Result>, bool> publish, bool emptyQuery)
    {
        var token = cts.Token;
        var all = new List<Result>();
        IReadOnlyList<Result>? last = null;

        while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            Drain(reader, all);

            // Aynı anda gelen diğer eklenti sonuçlarını tek yayında birleştir.
            await Task.Delay(MergeWindow, token).ConfigureAwait(false);
            Drain(reader, all);

            // Kanal tamamlandı ve boşaldıysa (tüm eklentiler döndü) bu yayın sondur.
            var snapshot = Sort(all, emptyQuery);
            var isFinal = reader.Completion.IsCompleted;
            if (!PublishIfCurrent(cts, publish, snapshot, isFinal))
            {
                return;   // iptal edildi: başka yayın yok
            }

            if (isFinal)
            {
                return;
            }

            last = snapshot;
        }

        // Eklentiler son toplu yayından sonra (yeni sonuç vermeden) bitti ya da hiç sonuç gelmedi.
        PublishIfCurrent(cts, publish, last ?? [], isFinal: true);
    }

    private static void Drain(ChannelReader<Result[]> reader, List<Result> into)
    {
        while (reader.TryRead(out var batch))
        {
            into.AddRange(batch);
        }
    }

    /// <summary>Score'a göre (eşitlikte geliş sırası) sıralar; boş sorguda en çok <see cref="Settings.EmptyQueryMaxResults"/> sonuç.</summary>
    private List<Result> Sort(List<Result> all, bool emptyQuery)
    {
        var max = Math.Max(1, _settings.MaxResults);
        if (emptyQuery)
        {
            max = Math.Min(Settings.EmptyQueryMaxResults, max);
        }

        return all.OrderByDescending(r => r.Score).Take(max).ToList();
    }

    /// <summary>Yalnızca hâlâ güncel sorguysa yayınlar; kilit, iptalle yayının yarışmasını önler.</summary>
    private bool PublishIfCurrent(CancellationTokenSource cts, Action<IReadOnlyList<Result>, bool> publish, IReadOnlyList<Result> results, bool isFinal)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_current, cts) || cts.IsCancellationRequested)
            {
                return false;
            }

            publish(results, isFinal);
            return true;
        }
    }

    private async Task RunPluginAsync(IPlugin plugin, Query query, ChannelWriter<Result[]> writer, CancellationToken token)
    {
        try
        {
            if (plugin.Metadata.SearchDelayMs > 0)
            {
                await Task.Delay(plugin.Metadata.SearchDelayMs, token).ConfigureAwait(false);
            }

            // Eşzamanlı çalışan eklenti bile diğerlerini bloklamasın.
            var results = await Task.Run(() => plugin.QueryAsync(query, token), CancellationToken.None).ConfigureAwait(false);

            // İptal edilmiş sorgunun sonucu (eklenti token'ı yok saymış olsa bile) atılır.
            if (token.IsCancellationRequested || results.Count == 0)
            {
                return;
            }

            foreach (var r in results)
            {
                if (r.PluginId.Length == 0)
                {
                    r.PluginId = plugin.Metadata.Id;
                }
            }

            await writer.WriteAsync(results.ToArray(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eklenti sorgusu başarısız: {PluginId}", plugin.Metadata.Id);
        }
    }

    private async Task InitPluginAsync(IPlugin plugin)
    {
        var id = plugin.Metadata.Id;
        try
        {
            var dir = Path.Combine(_dataRoot, id);
            Directory.CreateDirectory(dir);
            await plugin.InitAsync(new PluginContext(dir)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eklenti başlatılamadı, devre dışı: {PluginId}", id);
            lock (_gate)
            {
                _failed.Add(id);
            }
        }
    }

    private List<IPlugin> SelectPlugins(Query query)
    {
        lock (_gate)
        {
            if (query.ActionKeyword.Length > 0)
            {
                return _byKeyword.TryGetValue(query.ActionKeyword, out var list)
                    ? list.Where(IsEnabled).ToList()
                    : [];
            }

            return _global.Where(IsEnabled).ToList();
        }
    }

    private bool IsEnabled(IPlugin plugin) =>
        _settings.DisabledPlugins?.Contains(plugin.Metadata.Id, StringComparer.Ordinal) != true;

    private void CancelLocked()
    {
        _current?.Cancel();
        _current = null;
    }

    private void RebuildIndex(IEnumerable<IPlugin> plugins)
    {
        var byKeyword = new Dictionary<string, List<IPlugin>>(KeywordComparer.Instance);
        var global = new List<IPlugin>();

        foreach (var plugin in plugins)
        {
            foreach (var keyword in plugin.Metadata.ActionKeywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()))
            {
                if (keyword == PluginMetadata.GlobalKeyword)
                {
                    if (!global.Contains(plugin))
                    {
                        global.Add(plugin);
                    }

                    continue;
                }

                if (!byKeyword.TryGetValue(keyword, out var list))
                {
                    byKeyword[keyword] = list = [];
                }

                if (!list.Contains(plugin))
                {
                    list.Add(plugin);
                }
            }
        }

        _byKeyword = byKeyword;
        _global = global;
    }
}
