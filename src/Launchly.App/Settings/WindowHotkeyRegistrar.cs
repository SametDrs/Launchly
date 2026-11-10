using Launchly.App.Hotkey;
using Launchly.Core;

namespace Launchly.App.SettingsUi;

/// <summary><see cref="HotkeyManager"/>'ı ana pencerenin tutamağına bağlayan <see cref="IHotkeyRegistrar"/>.</summary>
public sealed class WindowHotkeyRegistrar(HotkeyManager manager, IntPtr hwnd) : IHotkeyRegistrar
{
    public bool TryRegister(string hotkey, out string? error) => manager.TryRegister(hwnd, hotkey, out error);
}
