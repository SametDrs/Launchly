using System.Windows.Media;
using Launchly.Core;

namespace Launchly.App.Interop;

/// <summary>
/// Tema yüzey renkleri: istenen türe, sistem desteğine ve Windows saydamlık ayarına göre uygulanan tür ve
/// <c>WindowBackgroundBrush</c> / <c>FooterBrush</c> / <c>FieldBrush</c> renkleri. Saf fonksiyon (test edilebilir).
/// </summary>
/// <remarks>
/// Alfa değerleri teşhisle seçildi (bkz. backlog-log.md, görev 11). Acrylic 0x99'da arkadaki renkler hâlâ
/// açıkça seçiliyor; beyaz bir pencerenin önünde bile ikincil metin (#9A9CA6) ~3:1, ana metin ~7,6:1 kontrastta kalıyor.
/// Mica yalnızca duvar kağıdı tonunu gösterdiği için daha ince (0x40) kaplanır.
/// </remarks>
public readonly record struct BackdropPalette(BackdropType Applied, Color WindowBackground, Color Footer, Color Field)
{
    public const byte MicaAlpha = 0x40;
    public const byte AcrylicAlpha = 0x99;

    /// <summary>Düz koyu zemin (#1F2024).</summary>
    public static readonly Color Base = Color.FromRgb(0x1F, 0x20, 0x24);

    /// <summary>Saydam modda alt çubuk: zemini %20 koyulaştıran siyah katman.</summary>
    public static readonly Color TranslucentFooter = Color.FromArgb(0x33, 0x00, 0x00, 0x00);

    /// <summary>Saydam modda giriş alanı: zemini ~%8 açan beyaz katman (opak #2B2D33'e yakın görünür).</summary>
    public static readonly Color TranslucentField = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);

    /// <summary>Düz modda alt çubuk: <see cref="TranslucentFooter"/>'ın #1F2024 üzerindeki opak karşılığı.</summary>
    public static readonly Color SolidFooter = Color.FromRgb(0x19, 0x1A, 0x1D);

    /// <summary>Düz modda giriş alanı (eski opak FieldBrush).</summary>
    public static readonly Color SolidField = Color.FromRgb(0x2B, 0x2D, 0x33);

    public bool IsTranslucent => Applied != BackdropType.None;

    public static BackdropPalette For(BackdropType requested, bool supported, bool transparencyEnabled)
    {
        var applied = supported && transparencyEnabled ? requested : BackdropType.None;
        return applied switch
        {
            BackdropType.Mica => Translucent(applied, MicaAlpha),
            BackdropType.Acrylic => Translucent(applied, AcrylicAlpha),
            _ => new BackdropPalette(BackdropType.None, Base, SolidFooter, SolidField),
        };
    }

    private static BackdropPalette Translucent(BackdropType applied, byte alpha) =>
        new(applied, Color.FromArgb(alpha, Base.R, Base.G, Base.B), TranslucentFooter, TranslucentField);
}
