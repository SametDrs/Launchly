using Launchly.App.SettingsUi;
using Launchly.Core;
using Launchly.Core.Plugins;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Launchly.Plugins.WebSearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    // Gerçek %AppData% ve kayıt defteri kullanılmaz: geçici dizin + sahte bileşenler.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchly-settings-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeHotkey : IHotkeyRegistrar
    {
        public List<string> Calls { get; } = [];

        public HashSet<string> Taken { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Current { get; private set; } = "Alt+Space";

        public bool IsRegistered => Current is not null;

        public bool TryRegister(string hotkey, out string? error)
        {
            Calls.Add(hotkey);
            Current = null; // gerçek yönetici önce eskisini bırakır
            if (Taken.Contains(hotkey))
            {
                error = $"{hotkey} kullanımda";
                return false;
            }

            Current = hotkey;
            error = null;
            return true;
        }
    }

    private sealed class FakeAutoStart : IAutoStart
    {
        public bool Enabled { get; private set; }

        public int Writes { get; private set; }

        public bool Throw { get; set; }

        public bool IsEnabled() => Enabled;

        public void SetEnabled(bool enabled)
        {
            if (Throw)
            {
                throw new UnauthorizedAccessException("yok");
            }

            Writes++;
            Enabled = enabled;
        }
    }

    private sealed class NoopOpener : IUrlOpener
    {
        public void Open(string url)
        {
        }
    }

    private readonly Settings _live = new();
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAutoStart _auto = new();
    private readonly PluginManager _plugins;
    private readonly JsonStorage<Settings> _storage;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _storage = new JsonStorage<Settings>(Path.Combine(_dir, "settings.json"));
        var web = new WebSearchPlugin(_live, new NoopOpener(), NullLogger.Instance);
        _plugins = new PluginManager([web], _live, NullLogger<PluginManager>.Instance, Path.Combine(_dir, "plugins"));
        _service = new SettingsService(_live, _storage, _hotkey, _auto, _plugins);
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
    }

    [Fact]
    public void PluginManager_RefreshKeywords_PicksUpNewAndRemovedSources()
    {
        Assert.DoesNotContain("maps", _plugins.ActionKeywords);

        _live.SearchSources.Add(new SearchSource { Title = "Maps", ActionKeyword = "maps", UrlTemplate = "https://maps.example/?q={q}" });
        Assert.DoesNotContain("maps", _plugins.ActionKeywords); // yenilenmeden eski dizin

        _plugins.RefreshKeywords();
        Assert.Contains("maps", _plugins.ActionKeywords);
        Assert.Equal("maps", _plugins.BuildQuery("maps istanbul").ActionKeyword);

        _live.SearchSources.RemoveAll(s => s.ActionKeyword == "google");
        _plugins.RefreshKeywords();
        Assert.DoesNotContain("google", _plugins.ActionKeywords);
    }

    [Fact]
    public void Apply_NewSource_WorksImmediately_AndIsSaved()
    {
        var draft = _service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "Maps", ActionKeyword = "maps", UrlTemplate = "https://maps.example/?q={q}" });

        var result = _service.Apply(draft);

        Assert.True(result.Success, result.Error);
        Assert.Contains("maps", _plugins.ActionKeywords);
        Assert.Contains(_live.SearchSources, s => s.ActionKeyword == "maps");
        Assert.Contains(_storage.Load().SearchSources, s => s.ActionKeyword == "maps");
    }

    [Fact]
    public void Apply_DisabledSource_KeywordStopsWorking()
    {
        var draft = _service.CreateDraft();
        draft.SearchSources.First(s => s.ActionKeyword == "bing").Enabled = false;

        Assert.True(_service.Apply(draft).Success);
        Assert.DoesNotContain("bing", _plugins.ActionKeywords);
    }

    [Theory]
    [InlineData("https://maps.example/?q=", "{q}")]
    [InlineData("ftp://maps.example/{q}", "http")]
    [InlineData("not a url {q}", "http")]
    public void Apply_InvalidTemplate_IsRejected_AndNothingChanges(string template, string errorPart)
    {
        var before = _live.SearchSources.Count;
        var draft = _service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "Maps", ActionKeyword = "maps", UrlTemplate = template });

        var result = _service.Apply(draft);

        Assert.False(result.Success);
        Assert.Contains(errorPart, result.Error);
        Assert.Equal(before, _live.SearchSources.Count);
        Assert.DoesNotContain("maps", _plugins.ActionKeywords);
        Assert.False(File.Exists(_storage.FilePath));
    }

    [Fact]
    public void Apply_DuplicateEnabledKeyword_IsRejected()
    {
        var draft = _service.CreateDraft();
        draft.SearchSources.Add(new SearchSource { Title = "G2", ActionKeyword = "GOOGLE", UrlTemplate = "https://g2.example/?q={q}" });

        Assert.False(_service.Apply(draft).Success);
    }

    [Fact]
    public void Apply_HotkeyChange_RegistersNewOne()
    {
        var draft = _service.CreateDraft();
        draft.Hotkey = "Ctrl+Shift+K";

        var result = _service.Apply(draft);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Ctrl+Shift+K", _hotkey.Current);
        Assert.Equal("Ctrl+Shift+K", _live.Hotkey);
        Assert.Equal("Ctrl+Shift+K", _storage.Load().Hotkey);
    }

    [Fact]
    public void Apply_HotkeyConflict_RollsBackToOldHotkey_AndShowsError()
    {
        _hotkey.Taken.Add("Ctrl+Shift+K");
        var draft = _service.CreateDraft();
        draft.Hotkey = "Ctrl+Shift+K";
        draft.Backdrop = BackdropType.Acrylic;

        var result = _service.Apply(draft);

        Assert.False(result.Success);
        Assert.Contains("kullanımda", result.Error);
        Assert.Equal(["Ctrl+Shift+K", "Alt+Space"], _hotkey.Calls);
        Assert.Equal("Alt+Space", _hotkey.Current);
        Assert.Equal("Alt+Space", _live.Hotkey);
        Assert.Equal(BackdropType.Mica, _live.Backdrop); // hiçbir şey uygulanmadı
    }

    [Fact]
    public void Apply_InvalidHotkey_IsRejected_WithoutTouchingRegistrar()
    {
        var draft = _service.CreateDraft();
        draft.Hotkey = "K";

        Assert.False(_service.Apply(draft).Success);
        Assert.Empty(_hotkey.Calls);
    }

    [Fact]
    public void Apply_UnchangedHotkey_DoesNotReregister()
    {
        var draft = _service.CreateDraft();
        draft.HideOnFocusLost = false;

        Assert.True(_service.Apply(draft).Success);
        Assert.Empty(_hotkey.Calls);
        Assert.False(_live.HideOnFocusLost);
    }

    [Fact]
    public void Apply_UnchangedHotkey_RetriesWhenCurrentRegistrationFailed()
    {
        _hotkey.Taken.Add("Alt+Space");
        _hotkey.TryRegister("Alt+Space", out _);   // başlangıçta çakışma: kayıt yok
        _hotkey.Calls.Clear();
        Assert.False(_hotkey.IsRegistered);
        _hotkey.Taken.Clear();                     // çakışan uygulama kapandı

        var result = _service.Apply(_service.CreateDraft());

        Assert.True(result.Success, result.Error);
        Assert.Equal(["Alt+Space"], _hotkey.Calls);
        Assert.True(_hotkey.IsRegistered);
    }

    [Fact]
    public void Apply_UnchangedHotkey_StillFailing_ReportsError()
    {
        _hotkey.Taken.Add("Alt+Space");
        _hotkey.TryRegister("Alt+Space", out _);

        var result = _service.Apply(_service.CreateDraft());

        Assert.False(result.Success);
        Assert.Contains("kullanımda", result.Error);
    }

    [Fact]
    public void Apply_AutoStart_WritesOnlyOnChange()
    {
        var draft = _service.CreateDraft();
        draft.StartOnSystemStartup = true;
        Assert.True(_service.Apply(draft).Success);
        Assert.True(_auto.Enabled);
        Assert.Equal(1, _auto.Writes);

        Assert.True(_service.Apply(_service.CreateDraft()).Success);
        Assert.Equal(1, _auto.Writes);

        var off = _service.CreateDraft();
        off.StartOnSystemStartup = false;
        Assert.True(_service.Apply(off).Success);
        Assert.False(_auto.Enabled);
    }

    [Fact]
    public void Apply_AutoStartFailure_RestoresHotkey_AndLeavesSettingsUnchanged()
    {
        _auto.Throw = true;
        var draft = _service.CreateDraft();
        draft.Hotkey = "Ctrl+Shift+K";
        draft.StartOnSystemStartup = true;

        var result = _service.Apply(draft);

        Assert.False(result.Success);
        Assert.Equal("Alt+Space", _hotkey.Current);
        Assert.Equal("Alt+Space", _live.Hotkey);
        Assert.False(_live.StartOnSystemStartup);
    }

    [Fact]
    public void Apply_RaisesApplied_WithBackdrop()
    {
        BackdropType? seen = null;
        _service.Applied += s => seen = s.Backdrop;
        var draft = _service.CreateDraft();
        draft.Backdrop = BackdropType.None;

        Assert.True(_service.Apply(draft).Success);
        Assert.Equal(BackdropType.None, seen);
    }

    [Fact]
    public void ViewModel_ApplyAndRevert()
    {
        var vm = new SettingsViewModel(_service);
        vm.AddSource();
        vm.Sources[^1].Title = "Maps";
        vm.Sources[^1].ActionKeyword = "maps";
        vm.Sources[^1].UrlTemplate = "https://maps.example/?q={q}";
        Assert.True(vm.Apply());
        Assert.Contains("maps", _plugins.ActionKeywords);

        vm.AddSource(); // boş satır geçersiz
        Assert.False(vm.Apply());
        Assert.True(vm.IsError);

        vm.Revert();
        Assert.False(vm.IsError);
        Assert.Equal(_live.SearchSources.Count, vm.Sources.Count);
    }

    [Theory]
    [InlineData("{\"DisabledPlugins\":null}")]
    [InlineData("{\"SearchSources\":[null]}")]
    [InlineData("{\"SearchSources\":null,\"DisabledPlugins\":[null,\"x\"]}")]
    public void Load_NullListsAndItems_AreNormalized_AndPluginsKeepWorking(string json)
    {
        File.WriteAllText(_storage.FilePath, json);

        var settings = _storage.Load().Normalize();

        Assert.NotNull(settings.DisabledPlugins);
        Assert.DoesNotContain(null!, settings.DisabledPlugins);
        Assert.NotNull(settings.SearchSources);
        Assert.DoesNotContain(null!, settings.SearchSources);

        var web = new WebSearchPlugin(settings, new NoopOpener(), NullLogger.Instance);
        using var manager = new PluginManager([web], settings, NullLogger<PluginManager>.Instance, Path.Combine(_dir, "p2"));
        _ = manager.ActionKeywords;
        _ = web.Metadata.ActionKeywords;
        var service = new SettingsService(settings, _storage, _hotkey, _auto, manager);
        Assert.NotNull(service.CreateDraft().SearchSources);
    }

    [Fact]
    public void NullGuards_WorkEvenWithoutNormalize()
    {
        var settings = new Settings { DisabledPlugins = null!, SearchSources = [null!, new SearchSource { Title = "A", ActionKeyword = "a", UrlTemplate = "https://a/{q}" }] };
        var web = new WebSearchPlugin(settings, new NoopOpener(), NullLogger.Instance);
        using var manager = new PluginManager([web], settings, NullLogger<PluginManager>.Instance, Path.Combine(_dir, "p3"));

        Assert.Contains("a", manager.ActionKeywords);
        var service = new SettingsService(settings, _storage, _hotkey, _auto, manager);
        Assert.Single(service.CreateDraft().SearchSources);
    }

    [Fact]
    public void Hotkey_FormatRoundTrips_AndCaptureValidates()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Shift+K", out var d));
        Assert.True(HotkeyParser.TryFormat(d.Modifiers, d.VirtualKey, out var text));
        Assert.Equal("Ctrl+Shift+K", text);
        Assert.True(HotkeyParser.TryFormat(HotkeyParser.ModAlt, 0x20, out text));
        Assert.Equal("Alt+Space", text);
        Assert.True(HotkeyParser.TryFormat(HotkeyParser.ModWin, 0x7B, out text));
        Assert.Equal("Win+F12", text);
        Assert.False(HotkeyParser.TryFormat(0, 0x41, out _));
        Assert.False(HotkeyParser.TryFormat(HotkeyParser.ModAlt, 0xFF, out _));

        var vm = new SettingsViewModel(_service);
        Assert.True(vm.TryCaptureHotkey(HotkeyParser.ModControl, 0x20));
        Assert.Equal("Ctrl+Space", vm.Hotkey);
        Assert.False(vm.TryCaptureHotkey(0, 0x20));
    }
}
