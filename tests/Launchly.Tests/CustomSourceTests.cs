using Launchly.App.SettingsUi;
using Launchly.Core;
using Launchly.Core.Plugins;
using Launchly.Core.Querying;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Launchly.Plugins.Url;
using Launchly.Plugins.WebSearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

/// <summary>Görev 10: {q}'suz özel kaynak (anahtar kelime siteyi açar), Türkçe i eşleşmesi, çakışma denetimi, dosya adı ayrımı.</summary>
public sealed class CustomSourceTests : IDisposable
{
    private const string Site = "https://example.com/";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchly-custom-" + Guid.NewGuid().ToString("N"));

    public CustomSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Opener : IUrlOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string url) => Opened.Add(url);
    }

    private sealed class FakeHotkey : IHotkeyRegistrar
    {
        public bool IsRegistered => true;

        public bool TryRegister(string hotkey, out string? error)
        {
            error = null;
            return true;
        }
    }

    private sealed class FakeAuto : IAutoStart
    {
        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }

    private static Settings WithFilm(bool enabled = true, string template = Site)
    {
        var s = new Settings();
        s.SearchSources.Add(new SearchSource { Title = "Film", ActionKeyword = "film", UrlTemplate = template, Enabled = enabled });
        return s;
    }

    private static WebSearchPlugin Web(Settings s, Opener o) => new(s, o, NullLogger.Instance);

    private static Task<IReadOnlyList<Result>> Ask(IPlugin p, string raw, string keyword, string search) =>
        p.QueryAsync(new Query(raw, keyword, search), CancellationToken.None);

    private static Task<IReadOnlyList<Result>> NoResults(Query q, CancellationToken t) =>
        Task.FromResult<IReadOnlyList<Result>>([]);

    // ---- SearchSource ----

    [Theory]
    [InlineData("https://example.com/", true)]
    [InlineData("http://site.example", true)]
    [InlineData("https://s.example/?q={q}", true)]
    [InlineData("ftp://site.example/", false)]
    [InlineData("site.example", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("https://", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///c:/x", false)]
    public void SearchSource_Validate_Template(string template, bool ok)
    {
        var source = new SearchSource { Title = "T", ActionKeyword = "k", UrlTemplate = template };
        Assert.Equal(ok, source.TryValidate(out var error));
        Assert.Equal(ok, error is null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a b")]
    [InlineData("*")]
    public void SearchSource_Validate_RejectsBadKeyword(string keyword) =>
        Assert.False(new SearchSource { Title = "T", ActionKeyword = keyword, UrlTemplate = Site }.TryValidate(out _));

    [Fact]
    public void SearchSource_Validate_RejectsEmptyTitle() =>
        Assert.False(new SearchSource { Title = " ", ActionKeyword = "k", UrlTemplate = Site }.TryValidate(out _));

    [Fact]
    public void SearchSource_IsSearchable_ComputedAndNotSerialized()
    {
        Assert.True(new SearchSource { UrlTemplate = "https://a/?q={q}" }.IsSearchable);
        Assert.False(new SearchSource { UrlTemplate = Site }.IsSearchable);

        var json = System.Text.Json.JsonSerializer.Serialize(new SearchSource { Title = "T", ActionKeyword = "k", UrlTemplate = Site });
        Assert.DoesNotContain("IsSearchable", json);
    }

    [Fact]
    public void LegacySettingsJson_LoadsUnchanged_AndSavedFileHasNoNewFields()
    {
        var path = Path.Combine(_dir, "settings.json");
        const string legacy = "{\"Version\":1,\"Hotkey\":\"Ctrl+Space\",\"SearchSources\":[{\"Title\":\"YouTube\",\"ActionKeyword\":\"youtube\",\"UrlTemplate\":\"https://y/?q={q}\",\"Icon\":\"\",\"Enabled\":true}]}";
        File.WriteAllText(path, legacy);

        var settings = new JsonStorage<Settings>(path).Load().Normalize();

        Assert.Equal("Ctrl+Space", settings.Hotkey);
        var source = Assert.Single(settings.SearchSources);
        Assert.True(source.IsSearchable);
        Assert.True(source.TryValidate(out _));

        new JsonStorage<Settings>(path).Save(settings);
        Assert.DoesNotContain("IsSearchable", File.ReadAllText(path));
    }

    // ---- KeywordComparer / QueryBuilder ----

    [Theory]
    [InlineData("film", "FILM")]
    [InlineData("film", "FİLM")]
    [InlineData("film", "fılm")]
    [InlineData("fılm", "FILM")]
    [InlineData("kız", "KIZ")]
    [InlineData("kiz", "KİZ")]
    public void KeywordComparer_TreatsTurkishIAsOne(string a, string b)
    {
        Assert.True(KeywordComparer.Instance.Equals(a, b));
        Assert.Equal(KeywordComparer.Instance.GetHashCode(a), KeywordComparer.Instance.GetHashCode(b));
    }

    [Fact]
    public void KeywordComparer_DifferentWordsStayDifferent()
    {
        Assert.False(KeywordComparer.Instance.Equals("film", "filmler"));
        Assert.False(KeywordComparer.Instance.Equals("film", "fil"));
        Assert.False(KeywordComparer.Instance.Equals("şarkı", "sarki"));
    }

    [Fact]
    public async Task TurkishCapitalI_KeywordWorksEndToEnd()
    {
        var settings = WithFilm();
        var opener = new Opener();
        using var manager = new PluginManager([Web(settings, opener)], settings, NullLogger<PluginManager>.Instance, _dir);
        IReadOnlyList<Result>? last = null;

        foreach (var raw in new[] { "FİLM x", "FILM x", "film", "FİLM" })
        {
            await manager.QueryAsync(raw, r => last = r);
            var r = Assert.Single(last!);
            Assert.EndsWith("sitesini aç", r.Title);
        }
    }

    // ---- WebSearchPlugin: {q}'suz kaynak ----

    [Theory]
    [InlineData("film ", "film", "")]
    [InlineData("film xyz", "film", "xyz")]
    [InlineData("film   a b c", "film", "a b c")]
    public async Task KeywordQuery_OpensSite_AndIgnoresExtraText(string raw, string keyword, string search)
    {
        var opener = new Opener();
        var r = Assert.Single(await Ask(Web(WithFilm(), opener), raw, keyword, search));

        Assert.Equal("Film sitesini aç", r.Title);
        Assert.Equal(Site, r.SubTitle);
        Assert.Equal(1000, r.Score);
        Assert.Equal("Alt+1", r.TypeLabel);
        Assert.True(await r.Action!());
        Assert.Equal([Site], opener.Opened);
    }

    [Theory]
    [InlineData("film")]
    [InlineData("FILM")]
    [InlineData("FİLM")]
    [InlineData("  film  ")]
    public async Task GlobalBareKeyword_GivesSameResultWith950(string raw)
    {
        var opener = new Opener();
        var r = Assert.Single(await Ask(Web(WithFilm(), opener), raw, string.Empty, raw.Trim()));

        Assert.Equal(950, r.Score);
        Assert.Equal("Film sitesini aç", r.Title);
        Assert.True(await r.Action!());
        Assert.Equal([Site], opener.Opened);
    }

    [Theory]
    [InlineData("filmler")]
    [InlineData("youtube")]    // {q}'lu kaynak: global davranış değişmez
    [InlineData("film abc")]   // global çok kelimeli sorgu
    [InlineData("")]
    [InlineData("   ")]
    public async Task GlobalOtherQueries_ProduceNothing(string raw) =>
        Assert.Empty(await Ask(Web(WithFilm(), new Opener()), raw, string.Empty, raw.Trim()));

    [Fact]
    public async Task DisabledSource_ProducesNothing_InAnyPath()
    {
        var plugin = Web(WithFilm(enabled: false), new Opener());

        Assert.Empty(await Ask(plugin, "film x", "film", "x"));
        Assert.Empty(await Ask(plugin, "film", string.Empty, "film"));
        Assert.DoesNotContain("film", plugin.Metadata.ActionKeywords);
    }

    [Fact]
    public void Metadata_IncludesGlobalKeyword_AndKeepsOtherSources()
    {
        var keywords = Web(WithFilm(), new Opener()).Metadata.ActionKeywords;
        Assert.Contains(PluginMetadata.GlobalKeyword, keywords);
        Assert.Contains("film", keywords);
        Assert.Contains("google", keywords);
    }

    [Fact]
    public async Task UnsafeTemplate_IsNotOpened_OnSitePath()
    {
        // Ayar penceresi reddeder, ama elle düzenlenmiş dosyadan gelirse açıcıya gitmemeli.
        var opener = new Opener();
        var settings = WithFilm(template: "file:///c:/windows/system32/calc.exe");
        var r = Assert.Single(await Ask(Web(settings, opener), "film", "film", string.Empty));

        Assert.False(await r.Action!());
        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task AddingQ_SwitchesSameSourceToSearch()
    {
        var opener = new Opener();
        var settings = WithFilm(template: "https://dizi.example/ara?q={q}");
        var r = Assert.Single(await Ask(Web(settings, opener), "film abc", "film", "abc"));

        Assert.Equal("abc", r.Title);
        await r.Action!();
        Assert.Equal(["https://dizi.example/ara?q=abc"], opener.Opened);
        Assert.Empty(await Ask(Web(settings, opener), "film", string.Empty, "film")); // global yolda site açılmaz
    }

    [Theory]
    [InlineData("a&b=c#d", "a%26b%3Dc%23d")]
    [InlineData("100% %41", "100%25%20%2541")]
    [InlineData("ı İ ş", "%C4%B1%20%C4%B0%20%C5%9F")]
    [InlineData("a+b /?", "a%2Bb%20%2F%3F")]
    public async Task QPlaceholder_EncodesSpecialCharacters(string search, string encoded)
    {
        var opener = new Opener();
        var settings = WithFilm(template: "https://s.example/r?q={q}&x=1");
        var r = Assert.Single(await Ask(Web(settings, opener), "film " + search, "film", search));
        await r.Action!();

        Assert.Equal([$"https://s.example/r?q={encoded}&x=1"], opener.Opened);
    }

    [Fact]
    public async Task LoneSurrogate_DoesNotThrow()
    {
        var settings = WithFilm(template: "https://s.example/r?q={q}");
        var r = Assert.Single(await Ask(Web(settings, new Opener()), "film \ud800", "film", "\ud800"));
        Assert.NotNull(r.Action);
    }

    [Fact]
    public async Task PreCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Web(WithFilm(), new Opener()).QueryAsync(new Query("film", string.Empty, "film"), cts.Token));
    }

    [Fact]
    public async Task ThroughPluginManager_BareAndSpacedKeyword_RunOnce_NoDuplicates()
    {
        var settings = WithFilm();
        var web = Web(settings, new Opener());
        using var manager = new PluginManager([web], settings, NullLogger<PluginManager>.Instance, _dir);

        foreach (var raw in new[] { "film", "film ", "film abc" })
        {
            IReadOnlyList<Result>? last = null;
            await manager.QueryAsync(raw, r => last = r);
            var r = Assert.Single(last!);
            Assert.Equal(WebSearchPlugin.PluginId, r.PluginId);
        }

        // Alakasız global sorgu WebSearch'ten sonuç getirmez.
        IReadOnlyList<Result>? other = null;
        await manager.QueryAsync("hello", r => other = r);
        Assert.Empty(other!);
    }

    // ---- SettingsService ----

    private sealed class Rig : IDisposable
    {
        public Rig(string dir, Settings live)
        {
            Live = live;
            Storage = new JsonStorage<Settings>(Path.Combine(dir, "settings.json"));
            var web = new WebSearchPlugin(live, Opener, NullLogger.Instance);
            var files = new FakePlugin("launchly.files", ["f"], NoResults);
            Manager = new PluginManager([web, files], live, NullLogger<PluginManager>.Instance, Path.Combine(dir, "plugins"));
            Service = new SettingsService(live, Storage, new FakeHotkey(), new FakeAuto(), Manager);
        }

        public Settings Live { get; }

        public PluginManager Manager { get; }

        public SettingsService Service { get; }

        public JsonStorage<Settings> Storage { get; }

        public Opener Opener { get; } = new();

        public void Dispose() => Manager.Dispose();
    }

    [Theory]
    [InlineData("f")]
    [InlineData("F")]
    public void Apply_KeywordOfAnotherPlugin_IsRejected_AndLiveUnchanged(string keyword)
    {
        using var rig = new Rig(_dir, new Settings());
        var draft = rig.Service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "Dosya", ActionKeyword = keyword, UrlTemplate = Site });

        var result = rig.Service.Apply(draft);

        Assert.False(result.Success);
        Assert.Contains("başka bir eklentiye", result.Error);
        Assert.Contains("7. satır", result.Error);
        Assert.DoesNotContain(rig.Live.SearchSources, s => s.Title == "Dosya");
        Assert.False(File.Exists(rig.Storage.FilePath));
    }

    [Fact]
    public void Apply_DisabledSource_MayUseReservedKeyword()
    {
        using var rig = new Rig(_dir, new Settings());
        var draft = rig.Service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "Dosya", ActionKeyword = "f", UrlTemplate = Site, Enabled = false });

        Assert.True(rig.Service.Apply(draft).Success);
    }

    [Fact]
    public void Apply_DuplicateKeyword_TurkishCaseVariants_IsRejected()
    {
        using var rig = new Rig(_dir, new Settings());
        var draft = rig.Service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "A", ActionKeyword = "film", UrlTemplate = Site });
        draft.SearchSources.Add(new SearchSource { Title = "B", ActionKeyword = "FİLM", UrlTemplate = "https://b.example/" });

        var result = rig.Service.Apply(draft);

        Assert.False(result.Success);
        Assert.Contains("birden fazla", result.Error);
        Assert.Contains("8. satır", result.Error);
    }

    [Fact]
    public void Apply_ErrorNamesRow_EvenWhenTitleEmpty()
    {
        using var rig = new Rig(_dir, new Settings());
        var draft = rig.Service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "", ActionKeyword = "x", UrlTemplate = Site });

        var result = rig.Service.Apply(draft);

        Assert.False(result.Success);
        Assert.Contains("7. satır", result.Error);
    }

    [Fact]
    public async Task Apply_SiteSource_WorksImmediately_ThenRemovalStopsIt()
    {
        using var rig = new Rig(_dir, new Settings());
        var draft = rig.Service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "Film", ActionKeyword = "film", UrlTemplate = Site });

        var result = rig.Service.Apply(draft);
        Assert.True(result.Success, result.Error);

        IReadOnlyList<Result>? last = null;
        await rig.Manager.QueryAsync("film", r => last = r);
        var hit = Assert.Single(last!);
        await hit.Action!();
        Assert.Equal([Site], rig.Opener.Opened);

        var persisted = rig.Storage.Load();
        Assert.Contains(persisted.SearchSources, s => s.ActionKeyword == "film" && !s.IsSearchable);

        var removal = rig.Service.CreateDraft();
        removal.SearchSources.RemoveAll(s => s.ActionKeyword == "film");
        Assert.True(rig.Service.Apply(removal).Success);
        await rig.Manager.QueryAsync("film", r => last = r);
        Assert.Empty(last!);
    }

    [Fact]
    public async Task Apply_ReplacesList_InsteadOfMutatingIt()
    {
        using var rig = new Rig(_dir, WithFilm());
        var before = rig.Live.SearchSources;
        var draft = rig.Service.CreateDraft();
        draft.SearchSources.RemoveAll(s => s.ActionKeyword == "film");

        Assert.True(rig.Service.Apply(draft).Success);

        Assert.NotSame(before, rig.Live.SearchSources);   // yerinde değişmez, yeni liste atanır
        Assert.Contains(before, s => s.ActionKeyword == "film");
        IReadOnlyList<Result>? last = null;
        await rig.Manager.QueryAsync("film", r => last = r);
        Assert.Empty(last!);
    }

    [Fact]
    public void PluginManager_ReservedKeywords_ExcludesOwnerAndDisabledPlugins()
    {
        var settings = new Settings();
        var web = Web(settings, new Opener());
        var files = new FakePlugin("launchly.files", ["f"], NoResults);
        using var manager = new PluginManager([web, files], settings, NullLogger<PluginManager>.Instance, _dir);

        var reserved = manager.ReservedKeywords(WebSearchPlugin.PluginId);
        Assert.Equal(["f"], reserved);
        Assert.DoesNotContain("google", reserved);

        settings.DisabledPlugins.Add("launchly.files");
        Assert.Empty(manager.ReservedKeywords(WebSearchPlugin.PluginId));
    }

    [Fact]
    public async Task PluginManager_MixedStarAndKeyword_RunsOncePerQuery()
    {
        var plugin = new FakePlugin("mixed", ["*", "k"], (_, _) => Task.FromResult<IReadOnlyList<Result>>([new Result { Title = "r" }]));
        using var manager = new PluginManager([plugin], new Settings(), NullLogger<PluginManager>.Instance, _dir);

        foreach (var raw in new[] { "k x", "zzz" })
        {
            IReadOnlyList<Result>? last = null;
            await manager.QueryAsync(raw, r => last = r);
            Assert.Single(last!);
        }

        Assert.Equal(2, plugin.QueryCount);
    }

    // ---- SettingsViewModel ----

    [Fact]
    public void ViewModel_SiteSource_AddEditApplyRevertRoundTrip()
    {
        using var rig = new Rig(_dir, new Settings());
        var vm = new SettingsViewModel(rig.Service);

        vm.AddSource();
        var row = vm.Sources[^1];
        Assert.True(row.Enabled);
        Assert.Equal("https://", row.UrlTemplate);

        row.Title = " Film ";
        row.ActionKeyword = " film ";
        row.UrlTemplate = Site;
        Assert.True(vm.Apply(), vm.Message);
        var live = Assert.Single(rig.Live.SearchSources, s => s.ActionKeyword == "film");
        Assert.Equal("Film", live.Title);
        Assert.Equal(Site, live.UrlTemplate);

        // {q} ekle -> aramaya dönüşür; Revert yapılmamış değişikliği atar.
        row.UrlTemplate = "https://dizi.example/?q={q}";
        vm.Revert();
        Assert.Equal(Site, vm.Sources.Single(s => s.ActionKeyword == "film").UrlTemplate);

        // Boş (varsayılan "https://") satır uygulamada satır numarasıyla reddedilir.
        vm.AddSource();
        Assert.False(vm.Apply());
        Assert.True(vm.IsError);
        Assert.Contains("8. satır", vm.Message);
    }

    // ---- UrlDetector ----

    [Theory]
    [InlineData("notes.md")]
    [InlineData("ornek.md")]
    [InlineData("readme.txt")]
    [InlineData("app.py")]
    [InlineData("archive.zip")]
    [InlineData("Notes.MD")]
    [InlineData("rapor.pdf")]
    [InlineData("foto.jpg")]
    [InlineData("backup.tar.gz")]
    [InlineData("  notes.md  ")]
    [InlineData("run.SH")]
    public void Url_RejectsBareFileNames(string input) =>
        Assert.False(UrlDetector.TryNormalize(input, out _));

    [Theory]
    [InlineData("https://notes.md", "https://notes.md/")]
    [InlineData("HTTPS://Notes.MD", "https://notes.md/")]
    [InlineData("www.notes.md", "https://www.notes.md/")]
    [InlineData("WWW.ornek.md", "https://www.ornek.md/")]
    [InlineData("notes.md/yol", "https://notes.md/yol")]
    [InlineData("notes.md?x=1", "https://notes.md/?x=1")]
    [InlineData("notes.md:8080", "https://notes.md:8080/")]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("github.io", "https://github.io/")]
    [InlineData("github.com", "https://github.com/")]
    public void Url_AcceptsExplicitSiteForms(string input, string expected)
    {
        Assert.True(UrlDetector.TryNormalize(input, out var url));
        Assert.Equal(expected, url);
    }
}
