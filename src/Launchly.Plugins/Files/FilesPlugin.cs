using System.Diagnostics;
using Launchly.Core.Querying;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Files;

/// <summary>Dosya/klasörü ilişkili uygulamayla açar; testlerde sahtesi kullanılır.</summary>
public interface IFileOpener
{
    void Open(string path);
}

public sealed class ShellFileOpener : IFileOpener
{
    public void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}

/// <summary>
/// "f rapor": kullanıcı klasörlerinde ad taraması (içerik değil). Her sorguda sınırlı derinlikte dizin taranır;
/// Windows Search API'ye geçiş ileride (bkz. backlog-log).
/// </summary>
public sealed class FilesPlugin : IPlugin
{
    public const string PluginId = "launchly.files";
    public const string Keyword = "f";

    private const int MaxDepth = 4;
    private const int MaxScanned = 30000;
    private const int MaxResults = 30;

    private readonly IReadOnlyList<string> _roots;
    private readonly IFileOpener _opener;
    private readonly ILogger _logger;

    public FilesPlugin(IFileOpener opener, ILogger<FilesPlugin> logger)
        : this(DefaultRoots(), opener, (ILogger)logger)
    {
    }

    internal FilesPlugin(IEnumerable<string> roots, IFileOpener opener, ILogger? logger)
    {
        _roots = roots.ToArray();
        _opener = opener;
        _logger = logger ?? NullLogger.Instance;
    }

    public PluginMetadata Metadata { get; } = new()
    {
        Id = PluginId,
        Name = "Files",
        ActionKeywords = [Keyword],
        SearchDelayMs = 120,
    };

    public static IReadOnlyList<string> DefaultRoots() =>
        new[]
        {
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
        }
        .Select(Environment.GetFolderPath)
        .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"))
        .Where(p => !string.IsNullOrEmpty(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public Task InitAsync(PluginContext context) => Task.CompletedTask;

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        if (query.Search.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var hits = new List<(string Path, bool IsDir, int Score)>();
        var scanned = 0;
        foreach (var root in _roots)
        {
            Scan(root, 0, query.Search, hits, ref scanned, cancellationToken);
            if (scanned >= MaxScanned)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var results = hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(h =>
            {
                var path = h.Path;
                return new Result
                {
                    Title = Path.GetFileName(path),
                    SubTitle = path,
                    Icon = h.IsDir ? "" : "", // Folder / Page
                    Score = h.Score,
                    TypeLabel = h.IsDir ? "Folder" : "File",
                    Action = () => Task.FromResult(Open(path)),
                };
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private static void Scan(string dir, int depth, string search, List<(string, bool, int)> hits, ref int scanned, CancellationToken ct)
    {
        if (depth > MaxDepth || scanned >= MaxScanned)
        {
            return;
        }

        var subdirs = new List<string>();
        try
        {
            // Öznitelikler numaralandırma sırasında gelir; öğe başına ek dosya sistemi çağrısı yapılmaz.
            foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if (++scanned >= MaxScanned)
                {
                    break;
                }

                var entry = info.FullName;
                var attrs = info.Attributes;
                if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                var isDir = (attrs & FileAttributes.Directory) != 0;
                var match = StringMatcher.Match(search, info.Name);
                if (match.IsMatch)
                {
                    hits.Add((entry, isDir, match.Score));
                }

                if (isDir && (attrs & FileAttributes.ReparsePoint) == 0)
                {
                    subdirs.Add(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Erişilemeyen/yarıda kesilen dizin: o ana kadarki sonuçlar yeter.
        }

        foreach (var sub in subdirs)
        {
            Scan(sub, depth + 1, search, hits, ref scanned, ct);
        }
    }

    private bool Open(string path)
    {
        try
        {
            _opener.Open(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Açılamadı: {Path}", path);
            return false;
        }
    }
}
