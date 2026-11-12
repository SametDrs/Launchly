using System.ComponentModel;
using System.Windows;
using Launchly.App.Interop;
using Launchly.App.SettingsUi.Pages;

namespace Launchly.App.SettingsUi;

/// <summary>
/// Sol menülü ayar penceresi. Sayfalar bir kez oluşturulup yeniden kullanılır; taslak tek <see cref="SettingsViewModel"/>'de
/// tutulduğundan sayfa değiştirmek değişiklikleri kaybetmez. Ana pencereyle aynı tema (<see cref="BackdropController"/>).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly BackdropController? _backdrop;
    private readonly Dictionary<SettingsPage, FrameworkElement> _pages;
    private bool _closing;

    public SettingsWindow(SettingsViewModel viewModel, BackdropController? backdrop = null)
    {
        _viewModel = viewModel;
        _backdrop = backdrop;
        DataContext = viewModel;
        InitializeComponent();

        _pages = new Dictionary<SettingsPage, FrameworkElement>
        {
            [SettingsPage.General] = new GeneralPage(),
            [SettingsPage.Appearance] = new AppearancePage(),
            [SettingsPage.SearchSources] = new SearchSourcesPage(),
            [SettingsPage.Plugins] = new PluginsPage(),
            [SettingsPage.About] = new AboutPage(),
        };

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowPage(viewModel.SelectedPage);

        // İlk gösterilişte tema (Mica/Acrylic/düz) ve koyu başlık çubuğu; sonra Uygula ve sistem değişiklikleri controller'dan.
        SourceInitialized += (_, _) => _backdrop?.Attach(this);
    }

    /// <summary>Pencereyi açar/öne getirir; her açılışta taslak canlı ayarlardan yenilenir.</summary>
    public void ShowSettings()
    {
        if (!IsVisible)
        {
            _viewModel.Revert();
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Uygulama kapanırken gerçekten kapatır ve abonelikleri bırakır.</summary>
    public void CloseForExit()
    {
        _closing = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _backdrop?.Detach(this);
        _viewModel.Dispose();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Pencere yeniden kullanılır; kapatma yalnızca gizler.
        if (!_closing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedPage))
        {
            ShowPage(_viewModel.SelectedPage);
        }
    }

    private void ShowPage(SettingsPage page)
    {
        if (_pages.TryGetValue(page, out var content) && !ReferenceEquals(PageHost.Content, content))
        {
            PageHost.Content = content;
        }
    }

    private void OnApply(object sender, RoutedEventArgs e) => _viewModel.Apply();

    private void OnRevert(object sender, RoutedEventArgs e) => _viewModel.Revert();

    private void OnClose(object sender, RoutedEventArgs e) => Hide();
}
