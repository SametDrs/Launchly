using System.Text.RegularExpressions;
using Launchly.Plugin;
using Launchly.Plugins.WebSearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Url;

/// <summary>Girdinin web adresi olup olmadığını anlar; yalnızca http/https kabul eder, şema yoksa https ekler.</summary>
public static partial class UrlDetector
{
    // alan.adi[:port][/yol] biçimi (şemasız).
    [GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,24}(?::\d{1,5})?(?:[/?#]\S*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareDomain();

    public static bool TryNormalize(string? input, out string url)
    {
        url = string.Empty;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var abs) ||
                (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps) ||
                abs.Host.Length == 0)
            {
                return false;
            }

            url = abs.AbsoluteUri;
            return true;
        }

        if (!BareDomain().IsMatch(text) ||
            !Uri.TryCreate("https://" + text, UriKind.Absolute, out var uri) ||
            uri.Host.Length == 0)
        {
            return false;
        }

        // Şemasız, "www."suz, port/yol/sorgu içermeyen ve son etiketi dosya uzantısı olan girdi dosya adıdır.
        if (!text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) &&
            text.IndexOfAny([':', '/', '?', '#']) < 0 &&
            FileExtensions.Contains(text[(text.LastIndexOf('.') + 1)..]))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    // Şemasız yazılınca site değil dosya adı sayılan uzantılar ("notes.md", "rapor.pdf"). Gerçek TLD olanlar
    // (.md, .py, .zip, .mov, .sh) da dahildir: bilinen ve kabul edilen sınırlama; "https://" ya da "www." ile
    // yazılırsa URL olarak kalır.
    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "md", "txt", "log", "ini", "cfg", "conf", "json", "xml", "yml", "yaml", "csv", "tsv", "cs", "csproj", "sln",
        "py", "js", "ts", "tsx", "jsx", "java", "cpp", "h", "sh", "ps1", "bat", "cmd", "exe", "dll", "msi", "iso",
        "zip", "rar", "7z", "gz", "tar", "png", "jpg", "jpeg", "gif", "bmp", "svg", "webp", "pdf", "doc", "docx",
        "xls", "xlsx", "ppt", "pptx", "mp3", "wav", "flac", "mp4", "mov", "avi", "mkv", "webm",
    };
}

/// <summary>"github.com" veya "https://..." yazılınca "Open URL" sonucu verir.</summary>
public sealed class UrlPlugin : IPlugin
{
    public const string PluginId = "launchly.url";

    private readonly IUrlOpener _opener;
    private readonly ILogger _logger;

    public UrlPlugin(IUrlOpener opener, ILogger<UrlPlugin> logger)
        : this(opener, (ILogger)logger)
    {
    }

    internal UrlPlugin(IUrlOpener opener, ILogger? logger)
    {
        _opener = opener;
        _logger = logger ?? NullLogger.Instance;
    }

    public PluginMetadata Metadata { get; } = new() { Id = PluginId, Name = "URL" };

    public Task InitAsync(PluginContext context) => Task.CompletedTask;

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        if (!UrlDetector.TryNormalize(query.Raw, out var url))
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var result = new Result
        {
            Title = "Open URL",
            SubTitle = url,
            Icon = "", // Globe
            Score = 850,
            TypeLabel = "URL",
            AutoComplete = query.Raw.Trim(),
            Action = () => Task.FromResult(Open(url)),
        };
        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }

    private bool Open(string url)
    {
        try
        {
            _opener.Open(url);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adres açılamadı: {Url}", url);
            return false;
        }
    }
}
