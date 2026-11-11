using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Launchly.App.ViewModels;
using Launchly.Core;

namespace Launchly.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Core.Settings _settings;

    public MainWindow(MainViewModel viewModel, Core.Settings settings)
    {
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;
        InitializeComponent();

        Deactivated += (_, _) =>
        {
            if (_settings.HideOnFocusLost)
            {
                HideLauncher();
            }
        };
        SettingsButton.Click += (_, _) =>
        {
            HideLauncher();
            SettingsRequested?.Invoke();
        };
        ActionsButton.Click += (_, _) =>
        {
            _viewModel.ToggleActionMenu();
            FocusSearchBox();   // düğme odak almaz ama tık pencereyi etkinleştirir; tuşlar kutudan gelmeye devam etsin
        };
        ActionList.PreviewMouseLeftButtonUp += (_, e) =>
        {
            // Tıklanan öğe fare basışında seçilir (SelectedActionIndex); bırakınca çalışır. Çift tetiklemede
            // (Enter + tık) ikinci çağrı, menü zaten kapandığı için yok sayılır.
            if (e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(ActionList, source) is ListViewItem)
            {
                _ = ExecuteSelectedAsync();
                e.Handled = true;
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedActionIndex)
                && viewModel.SelectedActionIndex is var actionIndex and >= 0
                && actionIndex < ActionList.Items.Count)
            {
                ActionList.ScrollIntoView(ActionList.Items[actionIndex]);
            }

            if (e.PropertyName == nameof(MainViewModel.SelectedIndex)
                && viewModel.SelectedIndex >= 0
                && viewModel.SelectedIndex < ResultList.Items.Count)
            {
                ResultList.ScrollIntoView(ResultList.Items[viewModel.SelectedIndex]);
            }
        };
    }

    /// <summary>Alt çubuktaki Settings düğmesine basılınca.</summary>
    public event Action? SettingsRequested;

    public bool IsLauncherVisible => IsVisible;

    public void ToggleLauncher()
    {
        if (IsVisible)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    /// <summary>Pencereyi ekranın üst-ortasında açar; kutuyu temizleyip odaklar.</summary>
    public void ShowLauncher()
    {
        _viewModel.Reset();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.16;

        Show();
        Activate();
        Focus();
        FocusSearchBox();
    }

    public void HideLauncher()
    {
        _viewModel.CancelPendingExecute();
        _viewModel.CloseActionMenu();
        if (IsVisible)
        {
            Hide();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt basılıyken WPF tuşu Key.System olarak bildirir.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        var modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.Alt && TryGetDigit(key, out var number))
        {
            _viewModel.SelectByNumber(number);   // menü açıkken etkisiz
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && key == Key.K)
        {
            _viewModel.ToggleActionMenu();
            e.Handled = true;
            return;
        }

        // Düz Enter'dan önce: menü açmadan yönetici olarak çalıştır (desteklemeyen sonuçta hiçbir şey yapmaz).
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Enter)
        {
            _ = ExecuteSelectedAsAdminAsync();
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Escape:
                // Menü açıksa yalnızca menü kapanır; değilse pencere gizlenir.
                if (_viewModel.HandleEscape())
                {
                    HideLauncher();
                }

                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _ = ExecuteSelectedAsync();
                e.Handled = true;
                break;
            case Key.Tab:
                if (_viewModel.AutoCompleteSelected())
                {
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                }

                e.Handled = true;
                break;
        }
    }

    private async Task ExecuteSelectedAsync()
    {
        // Eylem başarısız olursa ViewModel false döner (loglanır); pencere açık kalır.
        if (await _viewModel.ExecuteSelectedAsync())
        {
            HideLauncher();
        }
    }

    private async Task ExecuteSelectedAsAdminAsync()
    {
        if (await _viewModel.ExecuteSelectedAsAdminAsync())
        {
            HideLauncher();
        }
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private static bool TryGetDigit(Key key, out int number)
    {
        number = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => 0,
        };
        return number != 0;
    }
}
