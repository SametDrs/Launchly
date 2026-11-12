using System.Text.RegularExpressions;
using Launchly.App.Hotkey;
using Launchly.App.Interop;
using Launchly.App.SettingsUi;
using Launchly.App.Tray;
using Launchly.App.ViewModels;
using Launchly.Core;
using Launchly.Core.Localization;
using Launchly.Core.Plugins;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Launchly.Plugins.Calculator;
using Launchly.Plugins.Files;
using Launchly.Plugins.SystemCommands;
using Launchly.Plugins.Url;
using Launchly.Plugins.WebSearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

/// <summary>Testlerin ortak yerelleştirme verisi.</summary>
internal static class LocTestData
{
    public static IReadOnlyDictionary<string, string> Table(string language) => language == "tr" ? Strings.Tr : Strings.En;

    /// <summary>
    /// <paramref name="language"/> arayüzünde GÖRÜNMEMESİ gereken metinler: karşı dildeki, bu dilde farklı karşılığı olan değerler
    /// (iki dilde aynı olanlar ve bu dilin kendi metinleriyle çakışanlar çıkarılır).
    /// </summary>
    public static HashSet<string> ForeignOnly(string language)
    {
        var own = Table(language);
        var other = Table(language == "tr" ? "en" : "tr");
        var ownValues = own.Values.ToHashSet(StringComparer.Ordinal);
        return other
            .Where(kv => own[kv.Key] != kv.Value && !ownValues.Contains(kv.Value))
            .Select(kv => kv.Value.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }
}

/// <summary>Yerelleştirme katmanı: tablolar, Auto çözümlemesi, olay, ayar alanı, kurulum işareti.</summary>
public class LocalizationTests
{
    // ---------------------------------------------------------------- Tablolar

    [Fact]
    public void EveryKey_IsDefinedAndFilled_InBothLanguages()
    {
        Assert.Equal(Strings.Tr.Keys.Order(StringComparer.Ordinal), Strings.En.Keys.Order(StringComparer.Ordinal));
        Assert.NotEmpty(Strings.Tr);

        foreach (var key in Strings.Tr.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Strings.Tr[key]), $"tr boş: {key}");
            Assert.False(string.IsNullOrWhiteSpace(Strings.En[key]), $"en boş: {key}");
            Assert.DoesNotContain(',', key);   // XAML dizin bağlaması virgülü ayırıcı sayar
        }
    }

    [Fact]
    public void FormatPlaceholders_MatchBetweenLanguages()
    {
        foreach (var key in Strings.Tr.Keys)
        {
            Assert.Equal(Placeholders(Strings.Tr[key]), Placeholders(Strings.En[key]));
        }

        static string[] Placeholders(string text) =>
            Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void Texts_DifferBetweenLanguages_ForTranslatedKeys()
    {
        // Gerçek çeviri sanity kontrolü: bilinen anahtarlarda iki dil farklı olmalı (ör. yanlışlıkla aynı metin girilmesin).
        foreach (var key in new[] { "main.settings", "main.actions", "action.open", "nav.general", "settings.apply", "type.application", "system.lock.title" })
        {
            Assert.NotEqual(Strings.Tr[key], Strings.En[key]);
        }
    }

    [Fact]
    public void Loc_UnknownKey_FallsBackToKey_AndPlainTextIsNotFormatted()
    {
        Assert.Equal("yok.boyle.anahtar", Loc.T("yok.boyle.anahtar"));
        Assert.Contains("{q}", Loc.T("sources.hint"));   // argümansız çağrı biçimlendirmez
        Assert.Equal("Search YouTube", Loc.In("en", "websearch.searchIn").Replace("{0}", "YouTube"));
        Assert.Equal("YouTube içinde ara", Loc.T("websearch.searchIn", "YouTube"));
    }

    // Kaynak kodda kullanılan her anahtar tablolarda var olmalı; tablolardaki her anahtar kullanılıyor olmalı.
    [Fact]
    public void SourceFiles_UseOnlyDefinedKeys_AndEveryKeyIsUsed()
    {
        var root = RepoRoot();
        var src = Path.Combine(root, "src");
        var files = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var all = new Dictionary<string, string>();

        foreach (var file in files.Where(f => !f.EndsWith("Strings.cs")))
        {
            var text = File.ReadAllText(file);
            all[file] = text;
            foreach (Match m in Regex.Matches(text, @"Loc\.(?:T|In)\((?:Loc\.English, )?\""([^\""]+)\"""))
            {
                used.Add(m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(text, @"\{loc:T ([^\s}]+)\}"))
            {
                used.Add(m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(text, @"new\(\""(starter\.[a-z]+)\"""))
            {
                used.Add(m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(text, @"new\(\""(lock|sleep|shutdown|restart|recycle)\"""))
            {
                used.Add("system." + m.Groups[1].Value + ".title");
                used.Add("system." + m.Groups[1].Value + ".sub");
                if (m.Groups[1].Value is "shutdown" or "restart" or "recycle")
                {
                    used.Add("system." + m.Groups[1].Value + ".confirm");
                }
            }
        }

        // Yorumlardaki örnekler ("...") ve çalışma anında birleştirilen anahtarlar ("system." + Key) elenir; ikincisi yukarıda ayrıca eklendi.
        used.RemoveWhere(k => !Regex.IsMatch(k, @"^[a-z][A-Za-z]*(\.[A-Za-z]+)+$"));
        Assert.NotEmpty(used);
        var undefined = used.Where(k => !Strings.Tr.ContainsKey(k)).ToList();
        Assert.True(undefined.Count == 0, "Tabloda olmayan anahtarlar: " + string.Join(", ", undefined));

        var unused = Strings.Tr.Keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(unused.Count == 0, "Hiçbir yerde kullanılmayan anahtarlar: " + string.Join(", ", unused));
    }

    // XAML'de kullanıcıya görünen düz yazı kalmamalı: Text/Content/ToolTip/Title/Header/Run metni ya bağlama ya da simge olmalı.
    [Fact]
    public void Xaml_HasNoHardcodedUserText()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        var attribute = new Regex(@"\b(?:Text|Content|ToolTip|Title|Header|AutomationProperties\.Name)=""([^""]*)""");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Launchly.App"), "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                foreach (Match m in attribute.Matches(line))
                {
                    var value = m.Groups[1].Value;
                    var isBinding = value.StartsWith('{');
                    var isGlyph = Regex.IsMatch(value, @"^(&#x[0-9A-Fa-f]+;)+$");
                    var isBrand = value == "Launchly";
                    if (!isBinding && !isGlyph && !isBrand)
                    {
                        offenders.Add($"{Path.GetFileName(file)}: {m.Value}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "Sabit metin kalmış: " + string.Join(" | ", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Launchly.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Depo kökü (Launchly.sln) bulunamadı");
        return dir!.FullName;
    }

    // ---------------------------------------------------------------- Auto çözümlemesi ve olay

    [Theory]
    [InlineData("Auto", "tr-TR", "tr")]
    [InlineData("Auto", "tr", "tr")]
    [InlineData("auto", "TR", "tr")]
    [InlineData("Auto", "en-US", "en")]
    [InlineData("Auto", "de-DE", "en")]
    [InlineData("Auto", "", "en")]
    [InlineData(null, "tr-TR", "tr")]
    [InlineData("", "en-US", "en")]
    [InlineData("klingon", "tr-TR", "tr")]   // bilinmeyen değer Auto sayılır
    [InlineData("tr", "en-US", "tr")]
    [InlineData("TR", "en-US", "tr")]
    [InlineData("en", "tr-TR", "en")]
    [InlineData(" en ", "tr-TR", "en")]
    public void Resolve_AutoFollowsWindowLanguage_ExplicitChoiceWins(string? setting, string system, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, system));

    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("Auto", "Auto")]
    [InlineData("TR", "tr")]
    [InlineData("En", "en")]
    [InlineData("fr", "Auto")]
    public void NormalizeSetting_CanonicalValues(string? setting, string expected) =>
        Assert.Equal(expected, Loc.NormalizeSetting(setting));

    [Fact]
    public void SetLanguage_Auto_UsesSystemLanguage_AndRaisesEventOnlyOnChange()
    {
        var events = 0;
        void Handler() => events++;
        Loc.LanguageChanged += Handler;
        try
        {
            using var _ = TestLanguage.Use("tr");

            Assert.True(Loc.SetLanguage("Auto", "en-US"));
            Assert.Equal("en", Loc.Language);
            Assert.Equal(1, events);

            Assert.False(Loc.SetLanguage("en"));         // zaten en
            Assert.False(Loc.SetLanguage("Auto", "en-GB"));
            Assert.Equal(1, events);

            Assert.True(Loc.SetLanguage("Auto", "tr-TR"));
            Assert.Equal("tr", Loc.Language);
            Assert.Equal(2, events);
        }
        finally
        {
            Loc.LanguageChanged -= Handler;
        }
    }

    [Fact]
    public void SetLanguage_FailingSubscriber_DoesNotBlockOthers()
    {
        var reached = false;
        void Bad() => throw new InvalidOperationException("abone hatası");
        void Good() => reached = true;
        Loc.LanguageChanged += Bad;
        Loc.LanguageChanged += Good;
        try
        {
            using var _ = TestLanguage.Use("en");
            Assert.True(reached);
        }
        finally
        {
            Loc.LanguageChanged -= Bad;
            Loc.LanguageChanged -= Good;
        }
    }

    // ---------------------------------------------------------------- Ayar dosyası

    private static Settings Load(string json)
    {
        using var dir = new ThemeTests.TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, json);
        return new JsonStorage<Settings>(path).Load().Normalize();
    }

    [Fact]
    public void NewSettings_LanguageIsAuto()
    {
        Assert.Equal("Auto", new Settings().Language);
        Assert.Equal(4, Settings.CurrentVersion);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Hotkey\":\"Alt+Space\"}")]
    [InlineData("{\"Version\":4}")]
    [InlineData("{\"Language\":null}")]
    [InlineData("{\"Version\":4,\"Language\":\"fr\"}")]   // bilinmeyen değer
    public void FileWithoutLanguage_OrInvalid_LoadsAsAuto(string json) =>
        Assert.Equal("Auto", Load(json).Language);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OldFiles_WithLegacyDefaultTr_BecomeAuto(int version)
    {
        // v1-v3 dosyaları Language alanını hep "tr" (kullanılmayan varsayılan) yazdı; seçim sayılmaz.
        var s = Load($"{{\"Version\":{version},\"Language\":\"tr\"}}");

        Assert.Equal("Auto", s.Language);
        Assert.Equal(4, s.Version);
    }

    [Fact]
    public void OldFile_WithEn_KeepsEn_AndV4Tr_IsAKeptChoice()
    {
        Assert.Equal("en", Load("{\"Version\":3,\"Language\":\"en\"}").Language);
        Assert.Equal("tr", Load("{\"Version\":4,\"Language\":\"tr\"}").Language);
        Assert.Equal("en", Load("{\"Version\":4,\"Language\":\"EN\"}").Language);
        Assert.Equal("tr", Load("{\"Language\":\"tr\"}").Language);   // Version yok: güncel sayılır, seçim korunur
    }

    [Fact]
    public void Migration_RunsOnce_ChosenTrSurvivesNextLoad()
    {
        using var dir = new ThemeTests.TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{\"Version\":3,\"Language\":\"tr\"}");
        var storage = new JsonStorage<Settings>(path);

        var first = storage.Load().Normalize();
        Assert.Equal("Auto", first.Language);
        first.Language = "tr";   // kullanıcı bilerek Türkçe seçti
        storage.Save(first);

        var second = new JsonStorage<Settings>(path).Load().Normalize();
        Assert.Equal("tr", second.Language);
        Assert.Contains("\"Language\": \"tr\"", File.ReadAllText(path));
    }

    // ---------------------------------------------------------------- Kurulum işareti

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    [InlineData(" EN\r\n")]
    public void Installer_Marker_IsAppliedWhenLanguageIsAuto_ThenDeleted(string marker)
    {
        using var dir = new ThemeTests.TempDir();
        var path = Path.Combine(dir.Path, InstallerLanguage.MarkerFileName);
        File.WriteAllText(path, marker);
        var settings = new Settings();

        Assert.True(InstallerLanguage.Consume(settings, path));

        Assert.Equal(marker.Trim().ToLowerInvariant(), settings.Language);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("tr", "en")]
    [InlineData("en", "tr")]
    [InlineData("en", "en")]
    public void Installer_Marker_NeverOverridesUserChoice_ButIsStillRemoved(string chosen, string marker)
    {
        using var dir = new ThemeTests.TempDir();
        var path = Path.Combine(dir.Path, InstallerLanguage.MarkerFileName);
        File.WriteAllText(path, marker);
        var settings = new Settings { Language = chosen };

        Assert.False(InstallerLanguage.Consume(settings, path));

        Assert.Equal(chosen, settings.Language);
        Assert.False(File.Exists(path));   // bir sonraki açılışta da uygulanmaz
    }

    [Theory]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("Auto")]
    public void Installer_Marker_Garbage_ChangesNothing(string marker)
    {
        using var dir = new ThemeTests.TempDir();
        var path = Path.Combine(dir.Path, InstallerLanguage.MarkerFileName);
        File.WriteAllText(path, marker);
        var settings = new Settings();

        Assert.False(InstallerLanguage.Consume(settings, path));

        Assert.Equal("Auto", settings.Language);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Installer_NoMarker_ChangesNothing()
    {
        using var dir = new ThemeTests.TempDir();
        var settings = new Settings();

        Assert.False(InstallerLanguage.Consume(settings, Path.Combine(dir.Path, InstallerLanguage.MarkerFileName)));

        Assert.Equal("Auto", settings.Language);
    }

    [Fact]
    public void Installer_FirstInstall_NoSettingsFile_MarkerBecomesStartupLanguage_UpdateDoesNot()
    {
        using var dir = new ThemeTests.TempDir();
        var settingsPath = Path.Combine(dir.Path, "settings.json");
        var marker = Path.Combine(dir.Path, InstallerLanguage.MarkerFileName);

        // İlk kurulum: ayar dosyası yok (JsonStorage varsayılanı yazar: Auto), işaret var.
        File.WriteAllText(marker, "en");
        var storage = new JsonStorage<Settings>(settingsPath);
        var first = storage.Load().Normalize();
        Assert.True(InstallerLanguage.Consume(first, marker));
        storage.Save(first);
        Assert.Equal("en", new JsonStorage<Settings>(settingsPath).Load().Normalize().Language);

        // Güncelleme/yeniden kurulum: kurulum betiği ayar dosyası varken işaret yazmaz. Yazsa bile Auto dışı ayar ezilmez.
        File.WriteAllText(marker, "tr");
        var again = new JsonStorage<Settings>(settingsPath).Load().Normalize();
        Assert.False(InstallerLanguage.Consume(again, marker));
        Assert.Equal("en", again.Language);
    }

    [Fact]
    public void InstallerScript_WritesMarkerOnlyOnFirstInstall_AndLocalizesItsMessages()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "Launchly.iss"));

        Assert.Contains("install-language.txt", script);
        Assert.Contains("settings.json", script);        // yalnızca ayar dosyası yokken
        Assert.Contains("[CustomMessages]", script);
        Assert.Contains("turkish.DeleteUserData", script);
        Assert.Contains("english.DeleteUserData", script);
        Assert.DoesNotContain("MsgBox('Ayarlar ve", script);   // sabit Türkçe sorgu kalmadı
        Assert.Equal(InstallerLanguage.MarkerFileName, "install-language.txt");
    }

    // ---------------------------------------------------------------- Hata ve doğrulama metinleri

    [Theory]
    [InlineData("tr", "Başlık boş olamaz.")]
    [InlineData("en", "The title cannot be empty.")]
    public void SearchSource_ValidationMessage_FollowsLanguage(string language, string expected)
    {
        using var _ = TestLanguage.Use(language);

        Assert.False(new SearchSource { Title = "", ActionKeyword = "x", UrlTemplate = "https://x.com" }.TryValidate(out var error));
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("tr", "2. satır (\"Film\")")]
    [InlineData("en", "Row 2 (\"Film\")")]
    public void SettingsValidation_RowLabel_FollowsLanguage(string language, string expectedLabel)
    {
        using var _ = TestLanguage.Use(language);
        var draft = new Settings
        {
            SearchSources =
            [
                new SearchSource { Title = "Google", ActionKeyword = "g", UrlTemplate = "https://g.com/?q={q}" },
                new SearchSource { Title = "Film", ActionKeyword = "has space", UrlTemplate = "https://film.com" },
            ],
        };

        Assert.False(SettingsService.TryValidate(draft, out var error));

        Assert.StartsWith(expectedLabel + ": ", error);
        Assert.EndsWith(Loc.T("source.keywordInvalid"), error);
    }

    [Theory]
    [InlineData("tr", "geçerli bir kısayol değil")]
    [InlineData("en", "is not a valid hotkey")]
    public void HotkeyManager_ErrorMessage_FollowsLanguage(string language, string fragment)
    {
        using var _ = TestLanguage.Use(language);
        using var manager = new HotkeyManager();

        Assert.False(manager.TryRegister(IntPtr.Zero, "bozuk", out var error));

        Assert.Contains(fragment, error);
    }

    [Theory]
    [InlineData("tr", "Windows 11 22H2 ve sonrası gerekir", "Windows saydamlık efektleri kapalı")]
    [InlineData("en", "Windows 11 22H2 or later is required", "Windows transparency effects are turned off")]
    public void BackdropReasons_FollowLanguage(string language, string unsupported, string transparencyOff)
    {
        using var _ = TestLanguage.Use(language);

        Assert.Equal(unsupported, WindowBackdrop.UnsupportedReason);
        Assert.Equal(transparencyOff, WindowBackdrop.TransparencyOffReason);
    }

    // ---------------------------------------------------------------- SettingsService ve ViewModel

    private sealed class Rig : IDisposable
    {
        private readonly ThemeTests.TempDir _dir = new();
        private readonly PluginManager _manager;

        public Rig(bool failingHotkey = false)
        {
            Live = new Settings();
            Clip = new NoClip();
            _manager = TestHelpers.CreateManager(
                [
                    new CalculatorPlugin(Clip, NullLogger.Instance),
                    new UrlPlugin(null!, NullLogger.Instance),
                    new SystemPlugin(null!, null!, NullLogger.Instance),
                ],
                Live);
            Path = System.IO.Path.Combine(_dir.Path, "settings.json");
            Service = new SettingsService(
                Live,
                new JsonStorage<Settings>(Path),
                failingHotkey ? new FailingHotkey() : new ThemeTests.OkHotkey(),
                new ThemeTests.NoAutoStart(),
                _manager);
            Vm = new SettingsViewModel(
                Service,
                plugins: () => _manager.Plugins,
                backdropAvailability: () => (false, WindowBackdrop.TransparencyOffReason));
        }

        public Settings Live { get; }

        public NoClip Clip { get; }

        public string Path { get; }

        public SettingsService Service { get; }

        public SettingsViewModel Vm { get; }

        public void Dispose()
        {
            Vm.Dispose();
            _manager.Dispose();
            _dir.Dispose();
        }
    }

    private sealed class NoClip : IClipboardService
    {
        public void SetText(string text)
        {
        }
    }

    private sealed class FailingHotkey : IHotkeyRegistrar, IAutoStart
    {
        public bool IsRegistered => false;

        public bool TryRegister(string hotkey, out string? error)
        {
            error = null;   // metin dolu gelmezse servis kendi (yerelleştirilmiş) metnini kullanır
            return false;
        }

        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }

    [Fact]
    public void CreateDraft_CopiesLanguage()
    {
        using var rig = new Rig();
        rig.Live.Language = "en";

        Assert.Equal("en", rig.Service.CreateDraft().Language);
    }

    [Fact]
    public void Apply_ChangesLanguageLive_SavesIt_AndRaisesEvent()
    {
        using var _ = TestLanguage.Use("tr");
        using var rig = new Rig();
        var raised = 0;
        Loc.LanguageChanged += () => raised++;
        var draft = rig.Service.CreateDraft();
        draft.Language = "en";

        var result = rig.Service.Apply(draft);

        Assert.True(result.Success);
        Assert.Equal("en", rig.Live.Language);
        Assert.Equal("en", Loc.Language);
        Assert.True(raised >= 1);
        Assert.Contains("\"Language\": \"en\"", File.ReadAllText(rig.Path));

        draft.Language = "Auto";   // test makinesinden bağımsız: Auto, testlerde Türkçe çözülür
        Assert.True(rig.Service.Apply(draft).Success);
        Assert.Equal("Auto", rig.Live.Language);
        Assert.Equal("tr", Loc.Language);
    }

    [Fact]
    public void Apply_InvalidLanguage_BecomesAuto()
    {
        using var _ = TestLanguage.Use("tr");
        using var rig = new Rig();
        var draft = rig.Service.CreateDraft();
        draft.Language = "klingon";

        Assert.True(rig.Service.Apply(draft).Success);

        Assert.Equal("Auto", rig.Live.Language);
    }

    [Fact]
    public void Apply_RejectedDraft_DoesNotChangeLanguage()
    {
        using var _ = TestLanguage.Use("tr");
        using var rig = new Rig();
        var draft = rig.Service.CreateDraft();
        draft.Language = "en";
        draft.Hotkey = "bozuk";

        var result = rig.Service.Apply(draft);

        Assert.False(result.Success);
        Assert.Equal("tr", Loc.Language);
        Assert.Equal("Auto", rig.Live.Language);
        Assert.Contains("geçerli bir kısayol değil", result.Error);   // hata metni hâlâ eski (tr) dilde
    }

    [Fact]
    public void ViewModel_LanguageChoice_DraftApplyRevert()
    {
        using var _ = TestLanguage.Use("tr");
        using var rig = new Rig();
        Assert.True(rig.Vm.IsLanguageAuto);
        var changed = new List<string>();
        rig.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        rig.Vm.IsLanguageEnglish = true;

        Assert.Equal("en", rig.Vm.Language);
        Assert.False(rig.Vm.IsLanguageAuto);
        Assert.False(rig.Vm.IsLanguageTurkish);
        Assert.Contains(nameof(SettingsViewModel.IsLanguageEnglish), changed);
        Assert.Equal("Auto", rig.Live.Language);   // Uygula'ya kadar taslak

        rig.Vm.Revert();
        Assert.Equal("Auto", rig.Vm.Language);

        rig.Vm.IsLanguageEnglish = true;
        Assert.True(rig.Vm.Apply());
        Assert.Equal("en", rig.Live.Language);
        Assert.Equal("en", Loc.Language);
        Assert.Equal("Settings applied.", rig.Vm.Message);   // mesaj yeni dilde

        rig.Vm.IsLanguageTurkish = true;
        Assert.True(rig.Vm.Apply());
        Assert.Equal("Ayarlar uygulandı.", rig.Vm.Message);
    }

    [Fact]
    public void ViewModel_Texts_FollowLanguage_WithoutRestart_AndKeepToggleDraft()
    {
        using var _ = TestLanguage.Use("tr");
        using var rig = new Rig();
        var calc = rig.Vm.Plugins.Single(p => p.Id == CalculatorPlugin.PluginId);
        calc.IsEnabled = false;   // taslak, dil değişince korunmalı
        Assert.Equal("Hesap Makinesi", calc.Name);
        Assert.Equal("Genel", calc.Keywords);
        Assert.Equal(Loc.In("tr", "backdrop.transparencyOff"), rig.Vm.BackdropUnavailableReason);
        var changed = new List<string>();
        calc.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        Loc.SetLanguage("en");

        Assert.Equal("Calculator", calc.Name);
        Assert.Equal(Loc.In("en", "plugin.calculator.desc"), calc.Description);
        Assert.Equal("Global", calc.Keywords);
        Assert.Contains(nameof(PluginToggleItem.Name), changed);
        Assert.False(calc.IsEnabled);
        Assert.Equal("Windows transparency effects are turned off", rig.Vm.BackdropUnavailableReason);
        Assert.Equal("Programs", Loc.In("en", "plugin.programs.name"));
        Assert.All(rig.Vm.Plugins, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
    }

    [Theory]
    [InlineData("tr", "bilinmiyor")]
    [InlineData("en", "unknown")]
    public void FormatVersion_Unknown_FollowsLanguage(string language, string expected)
    {
        using var _ = TestLanguage.Use(language);

        Assert.Equal(expected, SettingsViewModel.FormatVersion(null));
        Assert.Equal("1.2.3 (abcdef1)", SettingsViewModel.FormatVersion("1.2.3+abcdef1234"));
    }

    [Fact]
    public void ViewModel_ErrorMessages_ComeFromLocalizedService()
    {
        using var _ = TestLanguage.Use("en");
        using var rig = new Rig(failingHotkey: true);
        rig.Vm.Hotkey = "Alt+Q";   // kısayol değişti ve kaydedici başarısız

        Assert.False(rig.Vm.Apply());

        Assert.Equal("The hotkey could not be registered.", rig.Vm.Message);
        Assert.True(rig.Vm.IsError);
    }

    // ---------------------------------------------------------------- Ana pencere ViewModel ve tray

    [Theory]
    [InlineData("tr", "Aç")]
    [InlineData("en", "Open")]
    public void ActionMenu_OpenItem_FollowsLanguage(string language, string expected)
    {
        using var _ = TestLanguage.Use(language);
        var menu = MainViewModel.BuildMenu(new Result { Title = "x", Action = () => Task.FromResult(true) });

        Assert.Equal(expected, Assert.Single(menu).Title);
    }

    [Fact]
    public void TrayMenu_Texts_SwitchWithoutRestart_AndStopListeningAfterDispose()
    {
        using var _ = TestLanguage.Use("tr");
        var menu = new TrayMenu(() => { }, () => { });
        try
        {
            Assert.Equal(["Ayarlar", "Çıkış"], menu.Texts);

            Loc.SetLanguage("en");
            Assert.Equal(["Settings", "Exit"], menu.Texts);
        }
        finally
        {
            menu.Dispose();
        }

        Loc.SetLanguage("tr");   // Dispose sonrası olay menüye dokunmaz
        Assert.Equal(["Settings", "Exit"], menu.Texts);
    }

    // ---------------------------------------------------------------- Eklenti sonuçları

    private static Task<IReadOnlyList<Result>> Ask(IPlugin plugin, string raw, string keyword = "", string? search = null) =>
        plugin.QueryAsync(new Query(raw, keyword, search ?? raw), CancellationToken.None);

    [Fact]
    public void PluginMetadata_NameAndDescription_FilledAndTranslatedInBothLanguages()
    {
        using var dir = new ThemeTests.TempDir();
        var clip = new NoClip();
        var settings = new Settings();
        using var programs = new Launchly.Plugins.Programs.ProgramsPlugin(NullLogger.Instance, Path.Combine(dir.Path, "cache.json"), null!, null!);
        IPlugin[] plugins =
        [
            programs,
            new WebSearchPlugin(settings, null!, NullLogger.Instance),
            new CalculatorPlugin(clip, NullLogger.Instance),
            new FilesPlugin([], null!, NullLogger.Instance, clip),
            new SystemPlugin(null!, null!, NullLogger.Instance),
            new UrlPlugin(null!, NullLogger.Instance),
        ];

        var tr = new List<(string Name, string Description)>();
        var en = new List<(string Name, string Description)>();
        using (TestLanguage.Use("tr"))
        {
            tr.AddRange(plugins.Select(p => (p.Metadata.Name, p.Metadata.Description)));
        }

        using (TestLanguage.Use("en"))
        {
            en.AddRange(plugins.Select(p => (p.Metadata.Name, p.Metadata.Description)));
        }

        Assert.All(tr.Concat(en), x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.Name));
            Assert.False(string.IsNullOrWhiteSpace(x.Description));
        });
        for (var i = 0; i < plugins.Length; i++)
        {
            Assert.NotEqual(tr[i].Description, en[i].Description);
        }

    }

    [Theory]
    [InlineData("tr", "YouTube içinde ara", "Aramak için yazın", "Film sitesini aç")]
    [InlineData("en", "Search YouTube", "Type to search", "Open Film website")]
    public async Task WebSearch_Results_FollowLanguage(string language, string search, string prompt, string site)
    {
        using var _ = TestLanguage.Use(language);
        var settings = new Settings();
        settings.SearchSources.Add(new SearchSource { Title = "Film", ActionKeyword = "film", UrlTemplate = "https://film.example.com" });
        var plugin = new WebSearchPlugin(settings, null!, NullLogger.Instance);

        Assert.Equal(search, Assert.Single(await Ask(plugin, "youtube x", "youtube", "x")).SubTitle);
        Assert.Equal(prompt, Assert.Single(await Ask(plugin, "youtube ", "youtube", "")).SubTitle);
        Assert.Equal(site, Assert.Single(await Ask(plugin, "film", "film", "")).Title);
    }

    [Theory]
    [InlineData("tr", "Adresi aç", "URL")]
    [InlineData("en", "Open URL", "URL")]
    public async Task Url_Result_FollowsLanguage(string language, string title, string type)
    {
        using var _ = TestLanguage.Use(language);
        var r = Assert.Single(await Ask(new UrlPlugin(null!, NullLogger.Instance), "github.com"));

        Assert.Equal(title, r.Title);
        Assert.Equal(type, r.TypeLabel);
    }

    [Theory]
    [InlineData("tr", "2+2 = 4  (Enter: panoya kopyala)", "Hesap Makinesi")]
    [InlineData("en", "2+2 = 4  (Enter: copy to clipboard)", "Calculator")]
    public async Task Calculator_Result_FollowsLanguage(string language, string subTitle, string type)
    {
        using var _ = TestLanguage.Use(language);
        var r = Assert.Single(await Ask(new CalculatorPlugin(new NoClip(), NullLogger.Instance), "2+2"));

        Assert.Equal("4", r.Title);
        Assert.Equal(subTitle, r.SubTitle);
        Assert.Equal(type, r.TypeLabel);
    }

    private sealed class CapturingConfirm : IConfirmation
    {
        public List<(string Title, string Message)> Asked { get; } = [];

        public bool Confirm(string title, string message)
        {
            Asked.Add((title, message));
            return false;   // asla çalıştırma
        }
    }

    private sealed class NoSystem : ISystemCommands
    {
        public void Lock() => throw new InvalidOperationException("çalıştırılmamalı");

        public void Sleep() => throw new InvalidOperationException("çalıştırılmamalı");

        public void Shutdown() => throw new InvalidOperationException("çalıştırılmamalı");

        public void Restart() => throw new InvalidOperationException("çalıştırılmamalı");

        public void EmptyRecycleBin() => throw new InvalidOperationException("çalıştırılmamalı");
    }

    [Theory]
    [InlineData("tr", "Kapat", "Bilgisayarı kapat", "Bilgisayar kapatılsın mı?", "Sistem")]
    [InlineData("en", "Shutdown", "Shut down the computer", "Shut down the computer?", "System")]
    public async Task System_Results_AndConfirmation_FollowLanguage(string language, string title, string subTitle, string confirm, string type)
    {
        using var _ = TestLanguage.Use(language);
        var confirmation = new CapturingConfirm();
        var plugin = new SystemPlugin(new NoSystem(), confirmation, NullLogger.Instance);

        var r = Assert.Single(await Ask(plugin, "shutdown"));   // "shutdown" her dilde eşleşir (İngilizce başlık da aranır)

        Assert.Equal(title, r.Title);
        Assert.Equal(subTitle, r.SubTitle);
        Assert.Equal(type, r.TypeLabel);
        Assert.False(await r.Action!());   // onay reddedildi; komut çalışmaz
        Assert.Equal([(title, confirm)], confirmation.Asked);
    }

    [Fact]
    public async Task System_MatchesLocalizedTitle_AndEnglishTitleInEveryLanguage()
    {
        var plugin = new SystemPlugin(new NoSystem(), new CapturingConfirm(), NullLogger.Instance);

        using (TestLanguage.Use("tr"))
        {
            Assert.Equal("Kilitle", Assert.Single(await Ask(plugin, "kilitle")).Title);
            Assert.Equal("Kilitle", Assert.Single(await Ask(plugin, "lock")).Title);
            Assert.Equal("kilitle", Assert.Single(await Ask(plugin, "lock")).AutoComplete);
        }

        using (TestLanguage.Use("en"))
        {
            Assert.Equal("Lock", Assert.Single(await Ask(plugin, "lock")).Title);
            Assert.Empty(await Ask(plugin, "kilitle"));
        }
    }

    [Theory]
    [InlineData("tr", "Dosya", "Klasör", "Dosya konumunu aç", "Yolu kopyala")]
    [InlineData("en", "File", "Folder", "Open file location", "Copy path")]
    public async Task Files_TypeLabelsAndActions_FollowLanguage(string language, string file, string folder, string reveal, string copy)
    {
        using var _ = TestLanguage.Use(language);
        using var dir = new ThemeTests.TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "notlar.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir.Path, "notlar-klasoru"));
        var plugin = new FilesPlugin([dir.Path], null!, NullLogger.Instance, new NoClip());

        var results = await Ask(plugin, "f notlar", "f", "notlar");

        Assert.Equal(file, results.Single(r => r.Title == "notlar.txt").TypeLabel);
        var directory = results.Single(r => r.Title == "notlar-klasoru");
        Assert.Equal(folder, directory.TypeLabel);
        Assert.Equal([reveal, copy], directory.ContextActions.Select(a => a.Title));
    }
}
