using Launchly.Core;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.WebSearch;

/// <summary>"youtube beatles" gibi sorgularda ilgili sitede arama yapar.</summary>
public sealed class WebSearchPlugin : IPlugin
{
    public const string PluginId = "launchly.websearch";
    private const string DefaultIcon = ""; // Globe

    private readonly Settings _settings;
    private readonly IUrlOpener _opener;
    private readonly ILogger _logger;

    public WebSearchPlugin(Settings settings, IUrlOpener opener, ILogger<WebSearchPlugin> logger)
        : this(settings, opener, (ILogger)logger)
    {
    }

    internal WebSearchPlugin(Settings settings, IUrlOpener opener, ILogger? logger)
    {
        _settings = settings;
        _opener = opener;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Anahtar kelimeler ayardaki etkin kaynaklardan her okunuşta türetilir; ayar penceresi (07) kaynakları
    /// değiştirince <c>PluginManager</c> dizini yeniden kurduğunda yeni kelimeler geçerli olur.
    /// </summary>
    public PluginMetadata Metadata => new()
    {
        Id = PluginId,
        Name = "Web Search",
        ActionKeywords = ActiveSources().Select(s => s.ActionKeyword.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
    };

    public Task InitAsync(PluginContext context)
    {
        // Ayarda kaynak yoksa (silinmiş/boş liste) varsayılanlar geri konur; Settings kaydı çıkışta yapılır.
        if (_settings.SearchSources is not { Count: > 0 })
        {
            _settings.SearchSources = SearchSource.CreateDefaults();
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        var source = query.ActionKeyword.Length == 0
            ? null
            : ActiveSources().FirstOrDefault(s =>
                string.Equals(s.ActionKeyword.Trim(), query.ActionKeyword, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var keyword = source.ActionKeyword.Trim();
        var search = query.Search;
        Result result;
        if (search.Length == 0)
        {
            // Yalnızca kaynak gösterilir; arama çalıştırılmaz.
            result = new Result
            {
                Title = source.Title,
                SubTitle = "Aramak için yazın",
                Icon = IconOf(source),
                Score = 1000,
                TypeLabel = "Alt+1",
                AutoComplete = keyword + " ",
            };
        }
        else
        {
            var url = SearchUrl.Build(source.UrlTemplate, search);
            result = new Result
            {
                Title = search,
                SubTitle = $"Search {source.Title}",
                Icon = IconOf(source),
                Score = 1000,
                TypeLabel = "Alt+1",
                AutoComplete = $"{keyword} {search}",
                Action = () => Task.FromResult(Open(url)),
            };
        }

        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }

    private bool Open(string url)
    {
        if (!SearchUrl.IsSafe(url))
        {
            _logger.LogWarning("Geçersiz arama adresi engellendi: {Url}", url);
            return false;
        }

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

    private static string IconOf(SearchSource s) => string.IsNullOrEmpty(s.Icon) ? DefaultIcon : s.Icon;

    private IEnumerable<SearchSource> ActiveSources() =>
        (_settings.SearchSources ?? [])
            .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.ActionKeyword) && !string.IsNullOrWhiteSpace(s.UrlTemplate));
}
