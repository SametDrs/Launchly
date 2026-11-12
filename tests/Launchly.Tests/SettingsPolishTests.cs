using Launchly.App.SettingsUi;
using Launchly.Core;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Launchly.Plugins.Calculator;
using Launchly.Plugins.Files;
using Launchly.Plugins.Programs;
using Launchly.Plugins.SystemCommands;
using Launchly.Plugins.Url;
using Launchly.Plugins.WebSearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

/// <summary>Ayar penceresi düzeltmeleri: varsayılan kaynaklar, v2→v3 geçişi, eklenti açıklamaları, durum mesajı zamanlayıcısı.</summary>
public sealed class SettingsPolishTests
{
    private const string Bing = "{\"Title\":\"Bing\",\"ActionKeyword\":\"bing\",\"UrlTemplate\":\"https://www.bing.com/search?q={q}\",\"Icon\":\"\",\"Enabled\":true}";
    private const string Ddg = "{\"Title\":\"DuckDuckGo\",\"ActionKeyword\":\"duckduckgo\",\"UrlTemplate\":\"https://duckduckgo.com/?q={q}\",\"Icon\":\"\",\"Enabled\":true}";
    private const string Wiki = "{\"Title\":\"Wikipedia\",\"ActionKeyword\":\"wikipedia\",\"UrlTemplate\":\"https://en.wikipedia.org/w/index.php?search={q}\",\"Icon\":\"\",\"Enabled\":true}";
    private const string Google = "{\"Title\":\"Google\",\"ActionKeyword\":\"google\",\"UrlTemplate\":\"https://www.google.com/search?q={q}\",\"Icon\":\"\",\"Enabled\":true}";

    private static Settings Load(string json)
    {
        using var dir = new ThemeTests.TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, json);
        return new JsonStorage<Settings>(path).Load().Normalize();
    }

    private static string[] Keywords(Settings s) => s.SearchSources.Select(x => x.ActionKeyword).ToArray();

    [Fact]
    public void Defaults_AreGoogleYouTubeGitHub()
    {
        Assert.Equal(["google", "youtube", "github"], Keywords(new Settings()));
        Assert.Equal(3, Settings.CurrentVersion);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OldVersions_LoseUnchangedBingDuckWikipedia(int version)
    {
        var s = Load($"{{\"Version\":{version},\"SearchSources\":[{Google},{Bing},{Ddg},{Wiki}]}}");

        Assert.Equal(["google"], Keywords(s));
        Assert.Equal(3, s.Version);
    }

    [Fact]
    public void V1File_GetsBothMigrations_InOrder()
    {
        var s = Load($"{{\"Version\":1,\"Backdrop\":\"Mica\",\"SearchSources\":[{Bing},{Google}]}}");

        Assert.Equal(BackdropType.Acrylic, s.Backdrop);
        Assert.Equal(["google"], Keywords(s));
        Assert.Equal(3, s.Version);
    }

    [Fact]
    public void V3File_IsNotTouched()
    {
        var s = Load($"{{\"Version\":3,\"SearchSources\":[{Bing},{Wiki}]}}");

        Assert.Equal(["bing", "wikipedia"], Keywords(s));
    }

    [Fact]
    public void ModifiedOrCustomRecords_AreKept()
    {
        const string changedUrl = "{\"Title\":\"Bing\",\"ActionKeyword\":\"bing\",\"UrlTemplate\":\"https://www.bing.com/search?q={q}&setlang=tr\",\"Icon\":\"\",\"Enabled\":true}";
        const string changedKeyword = "{\"Title\":\"DuckDuckGo\",\"ActionKeyword\":\"ddg\",\"UrlTemplate\":\"https://duckduckgo.com/?q={q}\",\"Icon\":\"\",\"Enabled\":true}";
        const string custom = "{\"Title\":\"Wikipedia TR\",\"ActionKeyword\":\"vikipedi\",\"UrlTemplate\":\"https://tr.wikipedia.org/w/index.php?search={q}\",\"Icon\":\"\",\"Enabled\":true}";

        var s = Load($"{{\"Version\":2,\"SearchSources\":[{changedUrl},{changedKeyword},{custom},{Wiki}]}}");

        Assert.Equal(["bing", "ddg", "vikipedi"], Keywords(s));
    }

    [Fact]
    public void Migration_RunsOnce_ReAddedSourceSurvivesNextLoad()
    {
        using var dir = new ThemeTests.TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, $"{{\"Version\":2,\"SearchSources\":[{Google},{Bing}]}}");
        var storage = new JsonStorage<Settings>(path);

        var first = storage.Load().Normalize();
        Assert.Equal(["google"], Keywords(first));

        // Kullanıcı Bing'i birebir aynı haliyle elle geri ekler, kaydeder.
        first.SearchSources.Add(new SearchSource { Title = "Bing", ActionKeyword = "bing", UrlTemplate = "https://www.bing.com/search?q={q}" });
        storage.Save(first);

        var second = storage.Load().Normalize();
        Assert.Equal(["google", "bing"], Keywords(second));
        Assert.Equal(3, second.Version);
    }

    [Fact]
    public void Plugins_AllHaveNonEmptyDescription_AndSdkDefaultIsEmpty()
    {
        var clip = new NoClip();
        var settings = new Settings();
        using var dir = new ThemeTests.TempDir();
        Directory.CreateDirectory(dir.Path);
        using var programs = new ProgramsPlugin(NullLogger.Instance, Path.Combine(dir.Path, "cache.json"), null!, null!);
        IPlugin[] plugins =
        [
            programs,
            new WebSearchPlugin(settings, null!, NullLogger.Instance),
            new CalculatorPlugin(clip, NullLogger.Instance),
            new FilesPlugin([], null!, NullLogger.Instance, clip),
            new SystemPlugin(null!, null!, NullLogger.Instance),
            new UrlPlugin(null!, NullLogger.Instance),
        ];

        Assert.Equal(6, plugins.Length);
        foreach (var p in plugins)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Metadata.Description), p.Metadata.Name + " açıklaması boş");
        }

        Assert.Equal(string.Empty, new PluginMetadata { Id = "x", Name = "x" }.Description);
        Assert.False(new PluginToggleItem { Id = "x", Name = "x", Keywords = "" }.HasDescription);
    }

    private sealed class NoClip : IClipboardService
    {
        public void SetText(string text) { }
    }

    // ---- Durum mesajı ----

    private sealed class FakeScheduler : IDelayScheduler
    {
        public sealed class Entry(TimeSpan delay, Action callback) : IDisposable
        {
            public TimeSpan Delay { get; } = delay;
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
            public void Fire() => callback();   // iptal edilmiş olsa bile çalıştırılabilir: yarış durumunu taklit eder
        }

        public List<Entry> Entries { get; } = [];

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var e = new Entry(delay, callback);
            Entries.Add(e);
            return e;
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly ThemeTests.TempDir _dir = new();
        private readonly Launchly.Core.Plugins.PluginManager _manager;
        public FakeScheduler Scheduler { get; } = new();
        public SettingsViewModel Vm { get; }

        public Rig()
        {
            Directory.CreateDirectory(_dir.Path);
            var live = new Settings();
            _manager = TestHelpers.CreateManager([], live);
            var service = new SettingsService(live, new JsonStorage<Settings>(Path.Combine(_dir.Path, "s.json")),
                new ThemeTests.OkHotkey(), new ThemeTests.NoAutoStart(), _manager);
            Vm = new SettingsViewModel(service, scheduler: Scheduler);
        }

        public void Dispose()
        {
            Vm.Dispose();
            _manager.Dispose();
            _dir.Dispose();
        }
    }

    [Fact]
    public void SuccessMessage_ClearsAfterTwoSeconds()
    {
        using var rig = new Rig();

        Assert.True(rig.Vm.Apply());

        Assert.Equal("Ayarlar uygulandı.", rig.Vm.Message);
        var timer = Assert.Single(rig.Scheduler.Entries);
        Assert.Equal(TimeSpan.FromSeconds(2), timer.Delay);

        timer.Fire();

        Assert.Equal(string.Empty, rig.Vm.Message);
        Assert.False(rig.Vm.IsError);
    }

    [Fact]
    public void SecondApply_CancelsOldTimer_AndStaleCallbackCannotClearNewMessage()
    {
        using var rig = new Rig();
        rig.Vm.Apply();
        var first = rig.Scheduler.Entries[0];

        rig.Vm.Apply();

        Assert.True(first.Disposed);
        Assert.Equal(2, rig.Scheduler.Entries.Count);

        first.Fire();   // eski zamanlayıcı yine de tetiklense
        Assert.Equal("Ayarlar uygulandı.", rig.Vm.Message);

        rig.Scheduler.Entries[1].Fire();
        Assert.Equal(string.Empty, rig.Vm.Message);
    }

    [Fact]
    public void ErrorMessage_NeverAutoClears_AndOldSuccessTimerCannotClearIt()
    {
        using var rig = new Rig();
        rig.Vm.Apply();
        var success = rig.Scheduler.Entries[0];

        rig.Vm.Hotkey = "bozuk kısayol";
        Assert.False(rig.Vm.Apply());

        Assert.True(rig.Vm.IsError);
        Assert.True(success.Disposed);
        Assert.Single(rig.Scheduler.Entries);   // hata için zamanlayıcı kurulmadı

        success.Fire();
        Assert.True(rig.Vm.IsError);
        Assert.NotEqual(string.Empty, rig.Vm.Message);
    }

    [Fact]
    public void Dispose_StopsPendingTimer()
    {
        var rig = new Rig();
        rig.Vm.Apply();
        var timer = rig.Scheduler.Entries[0];

        rig.Vm.Dispose();

        Assert.True(timer.Disposed);
        rig.Dispose();
    }
}
