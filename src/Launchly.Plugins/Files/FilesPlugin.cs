using System.Diagnostics;
using Launchly.Core.Querying;
using Launchly.Plugin;
using Launchly.Plugins.Calculator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Files;

/// <summary>Dosya/klasörü ilişkili uygulamayla açar; testlerde sahtesi kullanılır.</summary>
public interface IFileOpener
{
    void Open(string path);

    /// <summary>Gezgin'i açar ve dosyayı/klasörü seçili gösterir.</summary>
    void Reveal(string path);
}

public sealed class ShellFileOpener : IFileOpener
{
    public void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public void Reveal(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
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
    private readonly IClipboardService? _clipboard;
    private readonly ILogger _logger;

    public FilesPlugin(IFileOpener opener, IClipboardService clipboard, ILogger<FilesPlugin> logger)
        : this(DefaultRoots(), opener, (ILogger)logger, clipboard)
    {
    }

    /// <param name="clipboard">Verilmezse "Yolu kopyala" eylemi sunulmaz.</param>
    internal FilesPlugin(IEnumerable<string> roots, IFileOpener opener, ILogger? logger, IClipboardService? clipboard = null)
    {
        _roots = roots.ToArray();
        _opener = opener;
        _clipboard = clipboard;
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
                    ContextActions = BuildActions(path),
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

    private IReadOnlyList<ResultAction> BuildActions(string path)
    {
        var actions = new List<ResultAction>(2)
        {
            new()
            {
                Title = "Dosya konumunu aç",
                Glyph = "",   // FolderOpen
                Execute = () => Task.FromResult(Reveal(path)),
            },
        };

        if (_clipboard is { } clipboard)
        {
            actions.Add(new ResultAction
            {
                Title = "Yolu kopyala",
                Glyph = "",   // Copy
                Execute = () => Task.FromResult(Copy(clipboard, path)),
            });
        }

        return actions;
    }

    private bool Reveal(string path)
    {
        try
        {
            _opener.Reveal(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dosya konumu açılamadı: {Path}", path);
            return false;
        }
    }

    private bool Copy(IClipboardService clipboard, string path)
    {
        try
        {
            clipboard.SetText(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yol panoya yazılamadı: {Path}", path);
            return false;
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
