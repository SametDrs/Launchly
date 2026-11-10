using System.Diagnostics;
using Launchly.Core;
using Launchly.Core.Querying;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Programs;

/// <summary>Start Menu uygulamalarını bulur, ikonlarıyla listeler ve açar.</summary>
public sealed class ProgramsPlugin : IPlugin
{
    public const string PluginId = "launchly.programs";
    private const int MaxReturned = 50;

    private readonly ILogger _logger;
    private readonly JsonStorage<ProgramCache> _cacheStorage;
    private readonly ProgramScanner _scanner;
    private readonly IconLoader _icons = new();
    private readonly Action<string> _launch;
    private volatile ProgramEntry[] _entries = [];

    /// <summary>DI için: gerçek Start Menu klasörleri ve <c>%AppData%\Launchly\cache\programs.json</c>.</summary>
    public ProgramsPlugin(ILogger<ProgramsPlugin> logger)
        : this(
            logger,
            Path.Combine(DataLocation.RoamingDirectory, "cache", "programs.json"),
            new ProgramScanner(ProgramScanner.DefaultRoots()),
            LaunchShortcut)
    {
    }

    internal ProgramsPlugin(ILogger logger, string cachePath, ProgramScanner scanner, Action<string> launch)
    {
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

        RefreshTask = Task.Run(RefreshCore);
        return Task.CompletedTask;
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

        var results = new List<Result>();
        foreach (var (entry, score) in scored.Take(MaxReturned))
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

    private void RefreshCore()
    {
        try
        {
            var scanned = _scanner.Scan();
            _entries = scanned.ToArray();
            _cacheStorage.Save(new ProgramCache { ScannedAtUtc = DateTime.UtcNow, Programs = scanned });
            _logger.LogInformation("Programs taraması bitti: {Count} uygulama", scanned.Count);
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
