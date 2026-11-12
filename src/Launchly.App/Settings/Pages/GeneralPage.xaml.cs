using System.Windows.Controls;
using System.Windows.Input;
using Launchly.Core;
using Launchly.Core.Localization;

namespace Launchly.App.SettingsUi.Pages;

/// <summary>Kısayol (tuş yakalama) ve davranış ayarları.</summary>
public partial class GeneralPage : UserControl
{
    public GeneralPage()
    {
        InitializeComponent();
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        // Odak gezintisi (Tab / Shift+Tab) ve pencere kapatma (Alt+F4) yutulmaz.
        var current = Keyboard.Modifiers;
        if ((key == Key.Tab && (current & ~ModifierKeys.Shift) == ModifierKeys.None) ||
            (key == Key.F4 && current.HasFlag(ModifierKeys.Alt)))
        {
            return;
        }

        e.Handled = true;

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
        if (!viewModel.TryCaptureHotkey(mods, vk))
        {
            viewModel.IsError = true;
            viewModel.Message = Loc.T("general.hotkey.invalid");
            return;
        }

        viewModel.IsError = false;
        viewModel.Message = string.Empty;
    }
}
