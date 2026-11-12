using System.Runtime.InteropServices;
using System.Windows.Interop;
using Launchly.Core;
using Launchly.Core.Localization;

namespace Launchly.App.Hotkey;

/// <summary>Win32 <c>RegisterHotKey</c> ile global kısayol. Mesajlar <see cref="HwndSource"/> kancasıyla yakalanır.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C4C;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;

    public event Action? Pressed;

    /// <summary>Kısayol şu an başarıyla kayıtlı mı.</summary>
    public bool IsRegistered => _registered;

    /// <summary>Kısayolu kaydeder. Başarısızsa kullanıcıya gösterilecek açıklama <paramref name="error"/> içindedir.</summary>
    public bool TryRegister(IntPtr hwnd, string hotkeyText, out string? error)
    {
        Unregister();

        if (!HotkeyParser.TryParse(hotkeyText, out var def))
        {
            error = Loc.T("hotkey.invalid", hotkeyText);
            return false;
        }

        if (!RegisterHotKey(hwnd, HotkeyId, def.Modifiers | ModNoRepeat, def.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == ErrorHotkeyAlreadyRegistered
                ? Loc.T("hotkey.inUse", hotkeyText)
                : Loc.T("hotkey.failed", hotkeyText, code);
            return false;
        }

        _hwnd = hwnd;
        _registered = true;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        error = null;
        return true;
    }

    private void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
