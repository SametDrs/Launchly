using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launchly.App.SettingsUi;
using Launchly.App.SettingsUi.Pages;
using Launchly.Core;
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

    [Fact]
    public void AllPages_LoadWithTheme_AndShowTheirTitle()
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
                using var vm = new SettingsViewModel(service, null, () => manager.Plugins, () => (false, "Windows 11 22H2 ve sonrası gerekir"));

                var pages = new (FrameworkElement Page, string Title, string Expected)[]
                {
                    (new GeneralPage(), "Genel", "Kısayol"),
                    (new AppearancePage(), "Görünüm", "Windows 11 22H2 ve sonrası gerekir"),
                    (new SearchSourcesPage(), "Arama kaynakları", "Adreste {q} varsa arama yapar, yoksa siteyi açar."),
                    (new PluginsPage(), "Eklentiler", "launchly.files"),
                    (new AboutPage(), "Hakkında", "Üretici: Samet Dursun"),
                };

                foreach (var (page, title, expected) in pages)
                {
                    page.DataContext = vm;
                    page.Measure(new Size(680, 520));
                    page.Arrange(new Rect(0, 0, 680, 520));
                    page.UpdateLayout();

                    var texts = new List<string>();
                    Assert.True(CountTextBlocks(page, texts) > 0, $"{title} sayfası boş çizildi");
                    Assert.Contains(title, texts);
                    Assert.True(texts.Exists(t => t.Contains(expected, StringComparison.Ordinal)),
                        $"{title} sayfasında '{expected}' yok. Görünen metinler: {string.Join(" | ", texts)}");
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        });
    }
}
