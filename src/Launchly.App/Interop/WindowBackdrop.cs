using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Launchly.Core;

namespace Launchly.App.Interop;

/// <summary>Win11 Mica/Acrylic ve yuvarlak köşe (DwmSetWindowAttribute). Desteklenmiyorsa düz koyu renk.</summary>
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int Win11Build22H2 = 22621;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    public static bool IsSupported => Environment.OSVersion.Version.Build >= Win11Build22H2;

    /// <summary>Efekti uygular; gerçekte uygulanan türü döndürür (desteklenmiyorsa <see cref="BackdropType.None"/>).</summary>
    public static BackdropType Apply(Window window, BackdropType requested)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);

        var applied = BackdropType.None;
        if (requested != BackdropType.None && IsSupported)
        {
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var type = requested == BackdropType.Mica ? 2 : 3; // DWMSBT_MAINWINDOW / DWMSBT_TRANSIENTWINDOW
            if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int)) == 0)
            {
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                if (source?.CompositionTarget is { } target)
                {
                    target.BackgroundColor = Colors.Transparent;
                }
                applied = requested;
            }
        }

        if (applied == BackdropType.None && IsSupported)
        {
            // Önceden uygulanmış Mica/Acrylic'i kaldır (ayar çalışma anında değişebilir).
            var none = 1; // DWMSBT_NONE
            DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
            var margins = new Margins();
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }

        if (IsSupported)
        {
            var corner = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        }

        // Efekt yoksa düz koyu; varsa arkadaki efekti az bastıran yarı saydam katman.
        var color = applied switch
        {
            BackdropType.Mica => Color.FromArgb(0x40, 0x1F, 0x20, 0x24),
            BackdropType.Acrylic => Color.FromArgb(0x99, 0x1F, 0x20, 0x24),
            _ => Color.FromRgb(0x1F, 0x20, 0x24),
        };
        window.Resources["WindowBackgroundBrush"] = new SolidColorBrush(color);
        return applied;
    }
}
