using Microsoft.Extensions.Logging;

namespace Launchly.Core.Localization;

/// <summary>
/// Kurulumdan uygulamaya dil aktarımı. Kurulum (installer\Launchly.iss) yalnızca İLK kurulumda (settings.json yokken)
/// <c>%AppData%\Launchly\install-language.txt</c> dosyasına <c>tr</c> ya da <c>en</c> yazar. Uygulama açılışta dosyayı okur,
/// dil ayarı <c>Auto</c> ise o dili ayara yazar, dosyayı HER durumda siler. Böylece kullanıcının ayarlardan yaptığı seçim
/// (Auto dışı) hiçbir zaman ezilmez ve güncelleme/yeniden kurulum (dosya yazılmaz) dili değiştirmez.
/// </summary>
public static class InstallerLanguage
{
    public const string MarkerFileName = "install-language.txt";

    public static string MarkerPath => Path.Combine(DataLocation.RoamingDirectory, MarkerFileName);

    /// <summary>
    /// İşaret dosyasını tüketir. Dönüş: <paramref name="settings"/> değişti mi (çağıran kaydetmelidir).
    /// Dosya yoksa, okunamazsa ya da değeri tr/en değilse ayar değişmez.
    /// </summary>
    public static bool Consume(Settings settings, string markerPath, ILogger? logger = null)
    {
        string text;
        try
        {
            if (!File.Exists(markerPath))
            {
                return false;
            }

            text = File.ReadAllText(markerPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Kurulum dil işareti okunamadı: {Path}", markerPath);
            return false;
        }

        var changed = false;
        var language = Loc.NormalizeSetting(text);
        if (language != Loc.Auto && Loc.NormalizeSetting(settings.Language) == Loc.Auto)
        {
            settings.Language = language;
            changed = true;
            logger?.LogInformation("Kurulum dili uygulandı: {Language}", language);
        }

        try
        {
            File.Delete(markerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Kurulum dil işareti silinemedi: {Path}", markerPath);
        }

        return changed;
    }
}
