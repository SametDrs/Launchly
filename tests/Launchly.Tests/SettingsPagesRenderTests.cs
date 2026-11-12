using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launchly.App.SettingsUi;
using Launchly.App.SettingsUi.Pages;
using Launchly.App.Interop;
using Launchly.Core;
using Launchly.Core.Localization;
using Launchly.Core.Storage;

namespace Launchly.Tests;

/// <summary>
/// Ayar sayfaları gerçek tema sözlüğüyle (Themes/Dark.xaml) yüklenir ve çizilir: eksik kaynak, bağlama hatası ya da boş sayfa yakalanır.
/// WPF nesneleri tek bir STA iş parçacığında oluşturulur; pencere gösterilmez.
/// </summary>
public class SettingsPagesRenderTests
{
    private sealed class Ok : IHotkeyRegistrar, IAutoStart
    {
        public bool IsRegistered => true;

        public bool TryRegister(string hotkey, out string? error)
        {
            error = null;
            return true;
        }

        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }

    private static void RunSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA iş parçacığı bitmedi");
        error?.Throw();
    }

    private static void EnsureThemeResources()
    {
        // Sayfalar StaticResource ile uygulama kaynaklarını arar; testte App.xaml yok, tema sözlüğü elle eklenir.
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (app.Resources.MergedDictionaries.Count == 0)
        {
            app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/Launchly;component/Themes/Dark.xaml", UriKind.Relative)));
        }
    }

    // Satır içi Run'larla yazılmış TextBlock'ta Text özelliği boş kalır; metin Inlines'tan toplanır.
    private static string TextOf(TextBlock tb) =>
        tb.Inlines.Count == 0
            ? tb.Text
            : string.Concat(tb.Inlines.Select(i => i switch
            {
                System.Windows.Documents.Run run => run.Text,
                System.Windows.Documents.LineBreak => "\n",
                System.Windows.Documents.Span span => new System.Windows.Documents.TextRange(span.ContentStart, span.ContentEnd).Text,
                _ => string.Empty,
            }));

    private static int CountTextBlocks(DependencyObject root, List<string> texts)
    {
        var count = 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { Visibility: not Visibility.Visible })
            {
                continue;   // gizli dalın metni sayılmaz (pencere gösterilmediği için IsVisible kullanılamaz)
            }

            if (child is TextBlock tb && tb.ActualWidth > 0 && tb.ActualHeight > 0)
            {
                texts.Add(TextOf(tb));
                count++;
            }

            count += CountTextBlocks(child, texts);
        }

        return count;
    }


    // Her dilde: sayfa başlığı ve sayfada görünmesi gereken bir metin (sayfa sırası: Genel, Görünüm, Kaynaklar, Eklentiler, Hakkında).
    private static readonly Dictionary<string, (string Title, string Expected)[]> ExpectedTexts = new()
    {
        ["tr"] =
        [
            ("Genel", "Kısayol"),
            ("Görünüm", "Windows 11 22H2 ve sonrası gerekir"),
            ("Arama kaynakları", "Adreste {q} varsa arama yapar, yoksa siteyi açar."),
            ("Eklentiler", "launchly.files"),
            ("Hakkında", "Üretici: Samet Dursun"),
        ],
        ["en"] =
        [
            ("General", "Hotkey"),
            ("Appearance", "Windows 11 22H2 or later is required"),
            ("Search sources", "If the address contains {q} it searches, otherwise it opens the site."),
            ("Plugins", "launchly.files"),
            ("About", "Made by: Samet Dursun"),
        ],
    };

    private static FrameworkElement[] CreatePages() =>
        [new GeneralPage(), new AppearancePage(), new SearchSourcesPage(), new PluginsPage(), new AboutPage()];

    private static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

    // Sayfayı çizer ve görünen metinleri toplar. Karşı dile ait (yalnızca o dilde farklı olan) metin kalmamalıdır.
    private static void AssertPage(FrameworkElement page, SettingsViewModel vm, string language, (string Title, string Expected) expected)
    {
        page.DataContext = vm;
        page.Measure(new Size(680, 520));
        page.Arrange(new Rect(0, 0, 680, 520));
        page.UpdateLayout();

        var texts = new List<string>();
        Assert.True(CountTextBlocks(page, texts) > 0, $"[{language}] {expected.Title} sayfası boş çizildi");
        Assert.Contains(expected.Title, texts);
        Assert.True(texts.Exists(t => t.Contains(expected.Expected, StringComparison.Ordinal)),
            $"[{language}] {expected.Title} sayfasında '{expected.Expected}' yok. Görünen metinler: {string.Join(" | ", texts)}");

        var foreign = LocTestData.ForeignOnly(language);
        var mixed = texts.Where(t => foreign.Contains(t.Trim())).ToList();
        Assert.True(mixed.Count == 0, $"[{language}] {expected.Title} sayfasında karşı dilden metin var: {string.Join(" | ", mixed)}");
    }

    [Fact]
    public void AllPages_RenderInBothLanguages_AndSwitchWithoutRestart()
    {
        RunSta(() =>
        {
            EnsureThemeResources();
            var dir = Path.Combine(Path.GetTempPath(), "launchly-pages-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var settings = new Settings();
                using var manager = TestHelpers.CreateManager([new FakePlugin("launchly.files", ["f"], (_, _) => FakePlugin.Results())], settings);
                var ok = new Ok();
                var service = new SettingsService(settings, new JsonStorage<Settings>(Path.Combine(dir, "settings.json")), ok, ok, manager);
                using var vm = new SettingsViewModel(service, null, () => manager.Plugins, () => (false, WindowBackdrop.UnsupportedReason));

                // Aynı sayfa örnekleri tr -> en -> tr: dil çalışırken değişir, yeniden oluşturma yoktur.
                var pages = CreatePages();
                foreach (var language in new[] { "tr", "en", "tr" })
                {
                    Loc.SetLanguage(language);
                    Flush();
                    for (var i = 0; i < pages.Length; i++)
                    {
                        AssertPage(pages[i], vm, language, ExpectedTexts[language][i]);
                    }
                }

                AssertPlaceholderAndMarkupExtensionFollowLanguage();

                // Dil en iken yeni oluşturulan sayfalar da baştan İngilizce çizilir.
                Loc.SetLanguage("en");
                Flush();
                var fresh = CreatePages();
                for (var i = 0; i < fresh.Length; i++)
                {
                    AssertPage(fresh[i], vm, "en", ExpectedTexts["en"][i]);
                }
            }
            finally
            {
                Loc.SetLanguage("tr");
                Directory.Delete(dir, recursive: true);
            }
        });
    }

    // AllPages testinin STA iş parçacığında çağrılır (Application ve tema kaynakları tek iş parçacığına aittir).
    private static void AssertPlaceholderAndMarkupExtensionFollowLanguage()
    {
        Loc.SetLanguage("tr");
        Flush();
        var box = new TextBox { Style = (Style)Application.Current.FindResource("SearchTextBoxStyle") };
        box.Measure(new Size(400, 60));
        box.Arrange(new Rect(0, 0, 400, 60));
        box.UpdateLayout();
        var placeholder = (TextBlock)box.Template.FindName("Placeholder", box);
        Assert.Equal("Aramak için yazın…", placeholder.Text);

        var button = (Button)System.Windows.Markup.XamlReader.Parse(
            "<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:loc=\"clr-namespace:Launchly.App.Localization;assembly=Launchly\" Content=\"{loc:T main.settings}\" />");
        Assert.Equal("Ayarlar", button.Content);

        Loc.SetLanguage("en");
        Flush();

        Assert.Equal("Type to search…", placeholder.Text);
        Assert.Equal("Settings", button.Content);
    }
}
