using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Launchly.Core;

namespace Launchly.App.SettingsUi;

public partial class SettingsWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly SettingsViewModel _viewModel;
    private bool _closing;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Koyu başlık çubuğu.
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
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

    /// <summary>Uygulama kapanırken gerçekten kapatır.</summary>
    public void CloseForExit()
    {
        _closing = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Pencere yeniden kullanılır; kapatma yalnızca gizler.
        if (!_closing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        uint mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= HotkeyParser.ModAlt;
        if (m.HasFlag(ModifierKeys.Control)) mods |= HotkeyParser.ModControl;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= HotkeyParser.ModShift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= HotkeyParser.ModWin;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!_viewModel.TryCaptureHotkey(mods, vk))
        {
            _viewModel.IsError = true;
            _viewModel.Message = "Bu tuş birleşimi kısayol olamaz (en az bir değiştirici ve desteklenen bir tuş gerekir).";
            return;
        }

        _viewModel.IsError = false;
        _viewModel.Message = string.Empty;
    }

    private void OnAddSource(object sender, RoutedEventArgs e) => _viewModel.AddSource();

    private void OnRemoveSource(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SearchSourceItem item })
        {
            _viewModel.RemoveSource(item);
        }
    }

    private void OnApply(object sender, RoutedEventArgs e) => _viewModel.Apply();

    private void OnRevert(object sender, RoutedEventArgs e) => _viewModel.Revert();

    private void OnClose(object sender, RoutedEventArgs e) => Hide();
}
