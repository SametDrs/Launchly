using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Launchly.Core;
using Microsoft.Win32;

namespace Launchly.App.Interop;

/// <summary>
/// Win11 Mica/Acrylic, koyu başlık çubuğu ve yuvarlak köşe (DwmSetWindowAttribute). Desteklenmiyorsa ya da Windows
/// saydamlık efektleri kapalıysa düz koyu renk. Renkler <see cref="BackdropPalette"/>'ten gelir.
/// </summary>
/// <remarks>
/// Teşhis (görev 11): <c>WindowStyle=None</c> penceresinde WS_CAPTION yoktur; DWM bu durumda canlı Mica/Acrylic çizmez,
/// yalnızca düz bir yedek renk (Acrylic'te gri) gösterir ve köşeleri yuvarlamaz. Kenarlıksız pencere bu yüzden
/// <see cref="PrepareBorderless"/> ile başlık stilli ama başlıksız (WindowChrome, CaptionHeight 0) açılır.
/// </remarks>
public static class WindowBackdrop
{
    public const string UnsupportedReason = "Windows 11 22H2 ve sonrası gerekir";
    public const string TransparencyOffReason = "Windows saydamlık efektleri kapalı";

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;      // Mica
    private const int DwmsbtTransientWindow = 3; // Acrylic
    private const int Win11Build22H2 = 22621;
    private const int GwlStyle = -16;
    private const int WsSysMenu = 0x00080000;

    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    public static bool IsSupported => Environment.OSVersion.Version.Build >= Win11Build22H2;

    /// <summary>Windows "Saydamlık efektleri" açık mı (yalnızca okunur; değer yoksa açık sayılır).</summary>
    public static bool IsTransparencyEnabled()
    {
        try
        {
            return Registry.GetValue(PersonalizeKey, "EnableTransparency", 1) is not int value || value != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Mica/Acrylic seçilebilir mi; değilse ayar penceresinde gösterilecek neden.</summary>
    public static (bool Supported, string? Reason) Availability()
    {
        if (!IsSupported)
        {
            return (false, UnsupportedReason);
        }

        return IsTransparencyEnabled() ? (true, null) : (false, TransparencyOffReason);
    }

    /// <summary>
    /// Kenarlıksız pencereyi DWM arka planı çizilebilecek hale getirir: başlık stili (WS_CAPTION) kalır, başlık ve
    /// kenarlık WindowChrome ile istemci alanına katılır, sistem menüsü (DWM'nin çizdiği kapat düğmesi) kaldırılır.
    /// Tutamak oluşmadan (ctor içinde) çağrılmalı. Windows 10'da hiçbir şey yapmaz; pencere <c>WindowStyle=None</c> kalır.
    /// </summary>
    public static void PrepareBorderless(Window window)
    {
        if (!IsSupported)
        {
            return;
        }

        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.ResizeMode = ResizeMode.NoResize;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowLong(hwnd, GwlStyle, GetWindowLong(hwnd, GwlStyle) & ~WsSysMenu);
        };
    }

    /// <summary>Sistem durumunu okuyup efekti uygular; gerçekte uygulanan türü döndürür.</summary>
    public static BackdropType Apply(Window window, BackdropType requested) =>
        Apply(window, BackdropPalette.For(requested, IsSupported, IsTransparencyEnabled()));

    /// <summary>Hesaplanmış paleti pencereye uygular (DWM + pencere düzeyi fırçalar).</summary>
    public static BackdropType Apply(Window window, BackdropPalette palette)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);

        // Koyu başlık çubuğu ve sistem çerçevesi; Windows 10 20H1+ da destekler, eskide sessizce başarısız olur.
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var applied = palette.Applied;
        if (IsSupported)
        {
            var type = applied switch
            {
                BackdropType.Mica => DwmsbtMainWindow,
                BackdropType.Acrylic => DwmsbtTransientWindow,
                _ => DwmsbtNone,
            };
            if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int)) != 0)
            {
                applied = BackdropType.None;
            }

            // Saydam modda çerçeve tüm istemci alanına yayılır; düz modda geri alınır (ayar çalışırken değişebilir).
            var margins = applied == BackdropType.None
                ? new Margins()
                : new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (applied != BackdropType.None && source?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            var corner = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        }

        if (applied != palette.Applied)
        {
            palette = BackdropPalette.For(BackdropType.None, supported: false, transparencyEnabled: false);
        }

        window.Resources["WindowBackgroundBrush"] = Frozen(palette.WindowBackground);
        window.Resources["FooterBrush"] = Frozen(palette.Footer);
        window.Resources["FieldBrush"] = Frozen(palette.Field);
        return applied;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
