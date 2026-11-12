using Launchly.Core;
using Launchly.Core.Plugins;
using Launchly.Plugin;
using Launchly.Plugins.WebSearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

public sealed class WebSearchPluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchly-web-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeOpener : IUrlOpener
    {
        public List<string> Opened { get; } = [];

        public bool Throw { get; set; }

        public void Open(string url)
        {
            if (Throw)
            {
                throw new InvalidOperationException("boom");
            }

            Opened.Add(url);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static WebSearchPlugin Create(Settings settings, FakeOpener opener) =>
        new(settings, opener, NullLogger.Instance);

    private static Task<IReadOnlyList<Result>> Ask(WebSearchPlugin p, string keyword, string search) =>
        p.QueryAsync(new Query($"{keyword} {search}".Trim(), keyword, search), CancellationToken.None);

    [Theory]
    [InlineData("beatles", "https://www.youtube.com/results?search_query=beatles")]
    [InlineData("the beatles", "https://www.youtube.com/results?search_query=the%20beatles")]
    [InlineData("çay ğ ş", "https://www.youtube.com/results?search_query=%C3%A7ay%20%C4%9F%20%C5%9F")]
    [InlineData("a&b?c=d", "https://www.youtube.com/results?search_query=a%26b%3Fc%3Dd")]
    public async Task Query_BuildsEncodedUrl_AndActionOpensIt(string search, string expected)
    {
        using var _ = TestLanguage.Use("en");   // beklenen alt yazı İngilizce
        var opener = new FakeOpener();
        var plugin = Create(new Settings(), opener);

        var results = await Ask(plugin, "youtube", search);

        var r = Assert.Single(results);
        Assert.Equal(search, r.Title);
        Assert.Equal("Search YouTube", r.SubTitle);
        Assert.Equal("Alt+1", r.TypeLabel);
        Assert.True(await r.Action!());
        Assert.Equal([expected], opener.Opened);
    }

    [Fact]
    public async Task Query_Google_UsesGoogleTemplate()
    {
        var opener = new FakeOpener();
        var r = Assert.Single(await Ask(Create(new Settings(), opener), "GOOGLE", "claude"));
        await r.Action!();
        Assert.Equal(["https://www.google.com/search?q=claude"], opener.Opened);
    }

    [Fact]
    public async Task Query_KeywordOnly_ShowsSourceWithoutAction()
    {
        var opener = new FakeOpener();
        var r = Assert.Single(await Ask(Create(new Settings(), opener), "youtube", string.Empty));

        Assert.Equal("Aramak için yazın", r.SubTitle);
        Assert.Null(r.Action);
        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task DisabledSource_ProducesNoResult_AndNoKeyword()
    {
        var settings = new Settings();
        settings.SearchSources.First(s => s.ActionKeyword == "youtube").Enabled = false;
        var plugin = Create(settings, new FakeOpener());

        Assert.Empty(await Ask(plugin, "youtube", "beatles"));
        Assert.DoesNotContain("youtube", plugin.Metadata.ActionKeywords);
        Assert.Contains("google", plugin.Metadata.ActionKeywords);
    }

    [Fact]
    public async Task UnknownOrEmptyKeyword_ProducesNothing()
    {
        var plugin = Create(new Settings(), new FakeOpener());
        Assert.Empty(await Ask(plugin, "nope", "x"));
        Assert.Empty(await plugin.QueryAsync(new Query("x", string.Empty, "x"), CancellationToken.None));
    }

    [Fact]
    public async Task NonHttpTemplate_IsBlocked_AndWindowStaysOpen()
    {
        var settings = new Settings
        {
            SearchSources = [new SearchSource { Title = "Evil", ActionKeyword = "evil", UrlTemplate = "file:///c:/windows/{q}" }],
        };
        var opener = new FakeOpener();
        var r = Assert.Single(await Ask(Create(settings, opener), "evil", "calc.exe"));

        Assert.False(await r.Action!());
        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task OpenerFailure_ReturnsFalse()
    {
        var r = Assert.Single(await Ask(Create(new Settings(), new FakeOpener { Throw = true }), "google", "x"));
        Assert.False(await r.Action!());
    }

    [Fact]
    public async Task InitAsync_RestoresDefaults_WhenSourcesMissing()
    {
        var settings = new Settings { SearchSources = [] };
        await Create(settings, new FakeOpener()).InitAsync(new PluginContext(_dir));
        Assert.Equal(3, settings.SearchSources.Count);
    }

    [Fact]
    public async Task ThroughPluginManager_KeywordRoutesToWebSearch()
    {
        var settings = new Settings();
        var plugin = Create(settings, new FakeOpener());
        using var manager = new PluginManager([plugin], settings, NullLogger<PluginManager>.Instance, Path.Combine(_dir, "plugins"));
        IReadOnlyList<Result>? last = null;

        await manager.QueryAsync("youtube beatles", r => last = r);

        var r = Assert.Single(last!);
        Assert.Equal("beatles", r.Title);
        Assert.Equal(WebSearchPlugin.PluginId, r.PluginId);
    }

    [Fact]
    public void Settings_DefaultSources_AreComplete_AndRoundTripThroughJson()
    {
        var keywords = new Settings().SearchSources.Select(s => s.ActionKeyword);
        Assert.Equal(["google", "youtube", "github"], keywords);

        // settings.json'da SearchSources yoksa varsayılanlar gelir.
        var loaded = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Hotkey\":\"Alt+Space\"}")!;
        Assert.Equal(3, loaded.SearchSources.Count);
    }

    [Theory]
    [InlineData("https://a.com/?q=1", true)]
    [InlineData("http://a.com", true)]
    [InlineData("file:///c:/x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("calc.exe", false)]
    public void IsSafe_AllowsOnlyHttpHttps(string url, bool expected) =>
        Assert.Equal(expected, SearchUrl.IsSafe(url));
}
