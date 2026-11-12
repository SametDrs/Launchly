using System.Windows.Media;
using Launchly.App.Interop;
using Launchly.App.SettingsUi;
using Launchly.Core;
using Launchly.Core.Plugins;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Launchly.Plugins.WebSearch;
using Microsoft.Extensions.Logging.Abstractions;
using static Launchly.Tests.FakePlugin;

namespace Launchly.Tests;

/// <summary>Görev 11: tema paleti, ayar geçişi (v1 → v2), simge dosyası.</summary>
public class ThemeTests
{
    // ---- BackdropPalette.For ----

    [Theory]
    [InlineData(BackdropType.Mica)]
    [InlineData(BackdropType.Acrylic)]
    [InlineData(BackdropType.None)]
    public void Palette_Unsupported_FallsBackToSolid(BackdropType requested)
    {
        var p = BackdropPalette.For(requested, supported: false, transparencyEnabled: true);

        Assert.Equal(BackdropType.None, p.Applied);
        Assert.False(p.IsTranslucent);
        Assert.Equal(255, p.WindowBackground.A);
        Assert.Equal(255, p.Footer.A);
        Assert.Equal(255, p.Field.A);
    }

    [Theory]
    [InlineData(BackdropType.Mica)]
    [InlineData(BackdropType.Acrylic)]
    public void Palette_TransparencyOff_FallsBackToSolid(BackdropType requested)
    {
        var p = BackdropPalette.For(requested, supported: true, transparencyEnabled: false);

        Assert.Equal(BackdropType.None, p.Applied);
        Assert.Equal(BackdropPalette.Base, p.WindowBackground);
        Assert.Equal(BackdropPalette.SolidFooter, p.Footer);
        Assert.Equal(BackdropPalette.SolidField, p.Field);
    }

    [Theory]
    [InlineData(BackdropType.Mica, BackdropPalette.MicaAlpha)]
    [InlineData(BackdropType.Acrylic, BackdropPalette.AcrylicAlpha)]
    public void Palette_Translucent_UsesExpectedAlpha(BackdropType requested, byte alpha)
    {
        var p = BackdropPalette.For(requested, supported: true, transparencyEnabled: true);

        Assert.Equal(requested, p.Applied);
        Assert.True(p.IsTranslucent);
        Assert.Equal(Color.FromArgb(alpha, 0x1F, 0x20, 0x24), p.WindowBackground);
        Assert.Equal(BackdropPalette.TranslucentFooter, p.Footer);
        Assert.Equal(BackdropPalette.TranslucentField, p.Field);
    }

    [Fact]
    public void Palette_Alphas_KeepTextReadable()
    {
        // Acrylic arkası bulanık pencereleri gösterir; çok saydam katman metni okunmaz yapar.
        Assert.InRange(BackdropPalette.AcrylicAlpha, 0x80, 0xCC);
        Assert.InRange(BackdropPalette.MicaAlpha, 0x20, 0x80);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Palette_None_IsAlwaysOpaque(bool supported, bool transparency)
    {
        var p = BackdropPalette.For(BackdropType.None, supported, transparency);

        Assert.Equal(BackdropType.None, p.Applied);
        Assert.Equal(BackdropPalette.Base, p.WindowBackground);
        Assert.Equal(255, p.WindowBackground.A);
    }

    [Fact]
    public void Palette_UnknownEnumValue_IsSolid()
    {
        var p = BackdropPalette.For((BackdropType)42, supported: true, transparencyEnabled: true);

        Assert.Equal(BackdropType.None, p.Applied);
        Assert.False(p.IsTranslucent);
    }

    // ---- Settings.Normalize: v1 → v2 geçişi ----

    [Fact]
    public void NewSettings_AreVersion3_WithAcrylic()
    {
        var s = new Settings();

        Assert.Equal(3, Settings.CurrentVersion);
        Assert.Equal(3, s.Version);
        Assert.Equal(BackdropType.Acrylic, s.Backdrop);
        Assert.Equal(BackdropType.Acrylic, s.Normalize().Backdrop);
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Backdrop\":\"Mica\"}", BackdropType.Acrylic)]     // eski varsayılan → yeni varsayılan
    [InlineData("{\"Version\":1,\"Backdrop\":\"None\"}", BackdropType.None)]
    [InlineData("{\"Version\":1,\"Backdrop\":\"Acrylic\"}", BackdropType.Acrylic)]
    [InlineData("{\"Version\":2,\"Backdrop\":\"Mica\"}", BackdropType.Mica)]        // v2'de Mica açık seçimdir
    [InlineData("{\"Version\":0,\"Backdrop\":\"Mica\"}", BackdropType.Acrylic)]
    [InlineData("{\"Backdrop\":\"Mica\"}", BackdropType.Mica)]                      // Version yok: yeni dosya sayılır
    [InlineData("{\"Version\":1}", BackdropType.Acrylic)]
    public void Normalize_MigratesBackdrop(string json, BackdropType expected)
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, json);

        var s = new JsonStorage<Settings>(path).Load().Normalize();

        Assert.Equal(expected, s.Backdrop);
        Assert.Equal(3, s.Version);
    }

    [Fact]
    public void Migration_RunsOnce_SaveAndReloadDoesNotMigrateAgain()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{\"Version\":1,\"Hotkey\":\"Alt+Space\",\"Backdrop\":\"Mica\"}");
        var storage = new JsonStorage<Settings>(path);

        var first = storage.Load().Normalize();
        Assert.Equal(BackdropType.Acrylic, first.Backdrop);
        storage.Save(first);

        var text = File.ReadAllText(path);
        Assert.Contains("\"Version\": 3", text);
        Assert.Contains("\"Backdrop\": \"Acrylic\"", text);

        // Kullanıcı sonra Mica seçer: yeniden açılışta Mica kalır.
        first.Backdrop = BackdropType.Mica;
        storage.Save(first);
        var second = storage.Load().Normalize();
        Assert.Equal(BackdropType.Mica, second.Backdrop);
        Assert.Equal(3, second.Version);
    }

    [Fact]
    public void Version_StaysAt3_AfterCreateDraftAndApply()
    {
        using var dir = new TempDir();
        var storage = new JsonStorage<Settings>(Path.Combine(dir.Path, "settings.json"));
        var live = new Settings { Version = 1, Backdrop = BackdropType.Mica }.Normalize();
        using var manager = TestHelpers.CreateManager([], live);
        var service = new SettingsService(live, storage, new OkHotkey(), new NoAutoStart(), manager);

        var draft = service.CreateDraft();
        Assert.Equal(3, draft.Version);
        draft.Backdrop = BackdropType.Mica;
        Assert.True(service.Apply(draft).Success);

        Assert.Equal(3, live.Version);
        Assert.Equal(BackdropType.Mica, live.Backdrop);
        var reloaded = storage.Load();
        Assert.Equal(3, reloaded.Version);
        Assert.Equal(BackdropType.Mica, reloaded.Normalize().Backdrop);
    }

    // ---- Simge ----

    [Fact]
    public void Icon_HasEightExpectedSizes()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "Launchly.App", "Assets", "Launchly.ico");
        using var reader = new BinaryReader(File.OpenRead(path));

        Assert.Equal(0, reader.ReadUInt16());   // ayrılmış
        Assert.Equal(1, reader.ReadUInt16());   // tür: simge
        var count = reader.ReadUInt16();
        Assert.Equal(8, count);

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = reader.ReadBytes(16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            Assert.Equal(width, height);
            sizes.Add(width);
        }

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], sizes.Order());
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Launchly.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Launchly.sln bulunamadı.");
    }

    internal sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "launchly-theme-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    internal sealed class OkHotkey : IHotkeyRegistrar
    {
        public bool IsRegistered => true;

        public bool TryRegister(string hotkey, out string? error)
        {
            error = null;
            return true;
        }
    }

    internal sealed class NoAutoStart : IAutoStart
    {
        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }
}

/// <summary>Görev 11: eklentileri açıp kapatma (DisabledPlugins) ve çok sayfalı ayar penceresinin durumu.</summary>
public sealed class PluginToggleAndPagesTests : IDisposable
{
    private const string FilesId = "launchly.files";

    private sealed class NoopOpener : IUrlOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string url) => Opened.Add(url);
    }

    private readonly ThemeTests.TempDir _dir = new();
    private readonly Settings _live = new();
    private readonly FakePlugin _files = new(FilesId, ["f"], (q, _) => Results(R("file:" + q.Search)));
    private readonly FakePlugin _global = new("g", ["*"], (_, _) => Results(R("global")));
    private readonly WebSearchPlugin _web;
    private readonly PluginManager _manager;
    private readonly JsonStorage<Settings> _storage;
    private readonly SettingsService _service;

    public PluginToggleAndPagesTests()
    {
        _web = new WebSearchPlugin(_live, new NoopOpener(), NullLogger.Instance);
        _manager = TestHelpers.CreateManager([_files, _global, _web], _live);
        _manager.InitializeAsync().GetAwaiter().GetResult();
        _storage = new JsonStorage<Settings>(Path.Combine(_dir.Path, "settings.json"));
        _service = new SettingsService(_live, _storage, new ThemeTests.OkHotkey(), new ThemeTests.NoAutoStart(), _manager);
    }

    public void Dispose()
    {
        _manager.Dispose();
        _dir.Dispose();
    }

    private SettingsViewModel CreateViewModel(Func<(bool, string?)>? availability = null, IUrlOpener? opener = null) =>
        new(_service, null, () => _manager.Plugins, availability, opener);

    // ---- SettingsService ----

    [Fact]
    public async Task Apply_DisabledPlugins_CopiedAsNewList_Saved_AndPluginStopsRunning()
    {
        var before = _live.DisabledPlugins;
        var draft = _service.CreateDraft();
        draft.DisabledPlugins.Add(FilesId);

        Assert.True(_service.Apply(draft).Success);

        Assert.Equal([FilesId], _live.DisabledPlugins);
        Assert.NotSame(before, _live.DisabledPlugins);
        Assert.NotSame(draft.DisabledPlugins, _live.DisabledPlugins);   // taslak sonradan değişse de canlı ayar etkilenmez
        Assert.Equal([FilesId], _storage.Load().DisabledPlugins);

        await _manager.QueryAsync("f deneme", _ => { });
        Assert.Equal(0, _files.QueryCount);
        Assert.DoesNotContain("f", _manager.ActionKeywords);

        // Yeniden açılınca geri gelir.
        var on = _service.CreateDraft();
        on.DisabledPlugins.Clear();
        Assert.True(_service.Apply(on).Success);
        IReadOnlyList<Result> last = [];
        await _manager.QueryAsync("f deneme", r => last = r);
        Assert.Equal(1, _files.QueryCount);
        Assert.Equal(["file:deneme"], last.Select(r => r.Title));
    }

    [Fact]
    public void Apply_ValidationFailure_LeavesDisabledPluginsUnchanged()
    {
        var draft = _service.CreateDraft();
        draft.DisabledPlugins.Add(FilesId);
        draft.Hotkey = "K";   // geçersiz

        Assert.False(_service.Apply(draft).Success);
        Assert.Empty(_live.DisabledPlugins);
        Assert.False(File.Exists(_storage.FilePath));
    }

    [Fact]
    public void Apply_DisablingPlugin_FreesItsKeyword_InTheSameApply()
    {
        var clash = _service.CreateDraft();
        clash.SearchSources.Add(new SearchSource { Title = "F", ActionKeyword = "f", UrlTemplate = "https://f.example/?q={q}" });
        Assert.False(_service.Apply(clash).Success);   // Files eklentisi "f" kelimesini kullanıyor

        var freed = _service.CreateDraft();
        freed.SearchSources.Add(new SearchSource { Title = "F", ActionKeyword = "f", UrlTemplate = "https://f.example/?q={q}" });
        freed.DisabledPlugins.Add(FilesId);
        Assert.True(_service.Apply(freed).Success);
        Assert.Equal("f", _manager.BuildQuery("f x").ActionKeyword);

        // Kelimesi artık bir kaynakta olan eklenti yeniden açılamaz (çakışma doğrulamada yakalanır).
        var reenable = _service.CreateDraft();
        reenable.DisabledPlugins.Clear();
        Assert.False(_service.Apply(reenable).Success);
        Assert.Equal([FilesId], _live.DisabledPlugins);
    }

    // ---- SettingsViewModel ----

    [Fact]
    public void ViewModel_ListsPlugins_WithKeywords()
    {
        using var vm = CreateViewModel();

        Assert.Equal(
            new[] { FilesId, "g", SearchSource.WebSearchPluginId }.Order(StringComparer.Ordinal),
            vm.Plugins.Select(p => p.Id).Order(StringComparer.Ordinal));
        Assert.All(vm.Plugins, p => Assert.True(p.IsEnabled));
        Assert.Equal("f", vm.Plugins.Single(p => p.Id == FilesId).Keywords);
        Assert.Equal("Global", vm.Plugins.Single(p => p.Id == "g").Keywords);
    }

    [Fact]
    public void ViewModel_ToggleOff_Apply_WritesId_AndRevertRestoresToggle()
    {
        using var vm = CreateViewModel();
        var files = vm.Plugins.Single(p => p.Id == FilesId);

        files.IsEnabled = false;
        Assert.True(vm.Apply());
        Assert.Contains(FilesId, _live.DisabledPlugins);

        // Kaydedilmemiş değişiklik Geri al ile geri döner.
        vm.Plugins.Single(p => p.Id == FilesId).IsEnabled = true;
        vm.Revert();
        Assert.False(vm.Plugins.Single(p => p.Id == FilesId).IsEnabled);

        vm.Plugins.Single(p => p.Id == FilesId).IsEnabled = true;
        Assert.True(vm.Apply());
        Assert.DoesNotContain(FilesId, _live.DisabledPlugins);
    }

    [Fact]
    public void ViewModel_UnknownDisabledIds_ArePreserved()
    {
        _live.DisabledPlugins = ["eski.eklenti"];
        using var vm = CreateViewModel();
        Assert.DoesNotContain(vm.Plugins, p => p.Id == "eski.eklenti");

        vm.Plugins.Single(p => p.Id == FilesId).IsEnabled = false;
        Assert.True(vm.Apply());

        Assert.Equal(["eski.eklenti", FilesId], _live.DisabledPlugins.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ViewModel_BackdropUnsupported_DisablesOptions_AndShowsReason()
    {
        var supported = false;
        using var vm = CreateViewModel(() => supported ? (true, null) : (false, WindowBackdrop.TransparencyOffReason));

        Assert.False(vm.IsBackdropSupported);
        Assert.Equal("Windows saydamlık efektleri kapalı", vm.BackdropUnavailableReason);

        supported = true;
        vm.RefreshBackdropAvailability();
        Assert.True(vm.IsBackdropSupported);
        Assert.Null(vm.BackdropUnavailableReason);
    }

    [Fact]
    public void ViewModel_DefaultAvailability_IsSupported()
    {
        using var vm = new SettingsViewModel(_service);

        Assert.True(vm.IsBackdropSupported);
        Assert.Null(vm.BackdropUnavailableReason);
        Assert.Empty(vm.Plugins);
    }

    [Fact]
    public void ViewModel_ChangingPage_KeepsDraft_AndApplyIncludesAllPages()
    {
        using var vm = CreateViewModel();
        vm.SelectedPage = SettingsPage.General;
        Assert.True(vm.TryCaptureHotkey(HotkeyParser.ModControl | HotkeyParser.ModShift, 0x4B));
        vm.SelectedPage = SettingsPage.Appearance;
        vm.IsBackdropNone = true;
        vm.SelectedPage = SettingsPage.Plugins;
        vm.Plugins.Single(p => p.Id == FilesId).IsEnabled = false;
        vm.SelectedPage = SettingsPage.About;

        Assert.Equal("Ctrl+Shift+K", vm.Hotkey);
        Assert.Equal(BackdropType.None, vm.Backdrop);

        Assert.True(vm.Apply());
        Assert.Equal("Ctrl+Shift+K", _live.Hotkey);
        Assert.Equal(BackdropType.None, _live.Backdrop);
        Assert.Contains(FilesId, _live.DisabledPlugins);

        vm.Revert();
        Assert.Equal(SettingsPage.About, vm.SelectedPage);   // Geri al sayfayı değiştirmez
    }

    [Fact]
    public void ViewModel_BackdropRadioProperties_AreExclusive()
    {
        using var vm = CreateViewModel();
        Assert.True(vm.IsBackdropAcrylic);

        vm.IsBackdropMica = true;
        Assert.Equal(BackdropType.Mica, vm.Backdrop);
        Assert.False(vm.IsBackdropAcrylic);
        Assert.False(vm.IsBackdropNone);

        vm.IsBackdropMica = false;   // radyo düğmesinin "seçimi kaldır" bildirimi değeri değiştirmez
        Assert.Equal(BackdropType.Mica, vm.Backdrop);
    }

    [Fact]
    public void ViewModel_OpenRepository_UsesOpener_AndReportsFailure()
    {
        var opener = new NoopOpener();
        using var vm = CreateViewModel(opener: opener);

        vm.OpenRepository();
        Assert.Equal([SettingsViewModel.RepositoryUrl], opener.Opened);
        Assert.Equal("https://github.com/SametDrs/Launchly", vm.RepositoryLink);

        using var failing = CreateViewModel(opener: new ThrowingOpener());
        failing.OpenRepository();
        Assert.True(failing.IsError);
        Assert.Contains("açılamadı", failing.Message);
    }

    private sealed class ThrowingOpener : IUrlOpener
    {
        public void Open(string url) => throw new InvalidOperationException("tarayıcı yok");
    }

    [Fact]
    public void ViewModel_Dispose_UnsubscribesFromUsageStore()
    {
        using var usage = new Launchly.Plugins.Programs.ProgramUsageStore(
            Path.Combine(_dir.Path, "usage.json"), writeDelay: TimeSpan.FromHours(1));
        var vm = new SettingsViewModel(_service, usage);
        usage.Hide("lnk:a", "Alpha");
        Assert.Single(vm.HiddenPrograms);

        vm.Dispose();
        usage.Hide("lnk:b", "Beta");
        Assert.Single(vm.HiddenPrograms);   // artık dinlemiyor
        vm.Dispose();                        // ikinci çağrı zararsız
    }

    [Theory]
    [InlineData("1.0.0+abcdef1234567", "1.0.0 (abcdef1)")]
    [InlineData("1.0.0+abc", "1.0.0 (abc)")]
    [InlineData("1.0.0+", "1.0.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(null, "bilinmiyor")]
    [InlineData("  ", "bilinmiyor")]
    public void FormatVersion_ShortensBuildMetadata(string? input, string expected) =>
        Assert.Equal(expected, SettingsViewModel.FormatVersion(input));

    [Fact]
    public void AppVersion_MatchesProjectVersion()
    {
        using var vm = CreateViewModel();

        Assert.StartsWith("1.0.0", vm.AppVersion);
    }

    [Theory]
    [InlineData(new[] { "*" }, "Global")]
    [InlineData(new[] { "*", "g", "yt" }, "Global · g, yt")]
    [InlineData(new string[0], "—")]
    [InlineData(new[] { "f", " " }, "f")]
    public void FormatKeywords_Variants(string[] keywords, string expected) =>
        Assert.Equal(expected, PluginToggleItem.FormatKeywords(keywords));

    [Fact]
    public void ProgramsItem_IsFlagged()
    {
        var item = new PluginToggleItem { Id = Launchly.Plugins.Programs.ProgramsPlugin.PluginId, Name = "Programs", Keywords = "Global" };

        Assert.True(item.IsPrograms);
        Assert.False(new PluginToggleItem { Id = FilesId, Name = "Files", Keywords = "f" }.IsPrograms);
    }
}
