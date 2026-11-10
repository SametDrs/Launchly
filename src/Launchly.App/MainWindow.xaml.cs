using System.Windows;
using System.Windows.Input;
using Launchly.App.ViewModels;

namespace Launchly.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Deactivated += (_, _) => HideLauncher();
        PreviewKeyDown += OnPreviewKeyDown;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedIndex) && viewModel.SelectedIndex >= 0)
            {
                ResultList.ScrollIntoView(ResultList.Items[viewModel.SelectedIndex]);
            }
        };
    }

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
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    public void HideLauncher()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt basılıyken WPF tuşu Key.System olarak bildirir.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (Keyboard.Modifiers == ModifierKeys.Alt && TryGetDigit(key, out var number))
        {
            _viewModel.SelectByNumber(number);
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Escape:
                HideLauncher();
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
                // Çalıştırma 03-sorgu-cekirdegi/04-programs görevlerinde; şimdilik boş.
                e.Handled = true;
                break;
        }
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
