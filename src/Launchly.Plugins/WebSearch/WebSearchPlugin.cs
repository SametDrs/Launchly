using Launchly.Core;
using Launchly.Core.Localization;
using Launchly.Core.Querying;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.WebSearch;

/// <summary>"youtube beatles" gibi sorgularda ilgili sitede arama yapar.</summary>
public sealed class WebSearchPlugin : IPlugin
{
    public const string PluginId = SearchSource.WebSearchPluginId;
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
    /// Global anahtar sabittir: "film" gibi yalın kelime (sonda boşluk yok) de siteyi açabilsin diye
    /// eklenti global sorguda da çağrılır; orada yalnızca sözlük araması yapılır.
    /// </summary>
    public PluginMetadata Metadata => new()
    {
        Id = PluginId,
        Name = Loc.T("plugin.websearch.name"),
        Description = Loc.T("plugin.websearch.desc"),
        ActionKeywords = ActiveSources(_settings.SearchSources)
            .Select(s => s.ActionKeyword.Trim())
            .Distinct(KeywordComparer.Instance)
            .Append(PluginMetadata.GlobalKeyword)
            .ToArray(),
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
        cancellationToken.ThrowIfCancellationRequested();

        // Ayar uygulanırken liste yeni bir nesneyle değişir; sorgu boyunca tek başvuru kullanılır.
        var sources = _settings.SearchSources;

        if (query.ActionKeyword.Length == 0)
        {
            // Global yol: yalın "film" yazılıp Enter'a basıldıysa {q}'suz kaynağın sitesi açılır; diğer her şey boş.
            var bare = query.Search.Trim();
            if (bare.Length == 0)
            {
                return Empty();
            }

            var site = ActiveSources(sources).FirstOrDefault(s =>
                !s.IsSearchable && KeywordComparer.Instance.Equals(s.ActionKeyword.Trim(), bare));
            return site is null ? Empty() : One(OpenSiteResult(site, score: 950));
        }

        var source = ActiveSources(sources).FirstOrDefault(s =>
            KeywordComparer.Instance.Equals(s.ActionKeyword.Trim(), query.ActionKeyword));
        if (source is null)
        {
            return Empty();
        }

        if (!source.IsSearchable)
        {
            // "film" ve "film xyz": kelimeden sonraki metin yok sayılır.
            return One(OpenSiteResult(source, score: 1000));
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
                SubTitle = Loc.T("websearch.typeToSearch"),
                Icon = IconOf(source),
                Score = 1000,
                TypeLabel = "Alt+1",
                AutoComplete = keyword + " ",
            };
        }
        else
        {
            var url = SearchUrl.Build(source.UrlTemplate.Trim(), search);
            result = new Result
            {
                Title = search,
                SubTitle = Loc.T("websearch.searchIn", source.Title),
                Icon = IconOf(source),
                Score = 1000,
                TypeLabel = "Alt+1",
                AutoComplete = $"{keyword} {search}",
                Action = () => Task.FromResult(Open(url)),
            };
        }

        return One(result);
    }

    private Result OpenSiteResult(SearchSource source, int score)
    {
        var url = source.UrlTemplate.Trim();
        return new Result
        {
            Title = Loc.T("websearch.openSite", source.Title),
            SubTitle = url,
            Icon = IconOf(source),
            Score = score,
            TypeLabel = "Alt+1",
            AutoComplete = source.ActionKeyword.Trim(),
            Action = () => Task.FromResult(Open(url)),
        };
    }

    private static Task<IReadOnlyList<Result>> Empty() => Task.FromResult<IReadOnlyList<Result>>([]);

    private static Task<IReadOnlyList<Result>> One(Result r) => Task.FromResult<IReadOnlyList<Result>>([r]);


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

    private static IEnumerable<SearchSource> ActiveSources(List<SearchSource>? sources) =>
        (sources ?? [])
            .Where(s => s is not null && s.Enabled && !string.IsNullOrWhiteSpace(s.ActionKeyword) && !string.IsNullOrWhiteSpace(s.UrlTemplate));
}
