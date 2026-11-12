using System.Globalization;

namespace Launchly.Core.Localization;

/// <summary>
/// Kullanıcıya görünen tüm metinlerin tek kaynağı (tr ve en). Metinler kodda (<see cref="Strings"/>) tutulur:
/// uydu derlemesi ya da .resx yoktur, dil çalışırken <see cref="SetLanguage(string?)"/> ile değişir.
/// Core ve Plugins burayı kullanır; <c>Launchly.Plugin</c> SDK'sı bağımsız kalır (eklentiler hazır metni Result'a koyar).
/// Log mesajları ve kod yorumları kapsam dışıdır.
/// </summary>
public static class Loc
{
    /// <summary>Dil ayarı: Windows arayüz dilini izle (Türkçeyse Türkçe, değilse İngilizce).</summary>
    public const string Auto = "Auto";

    public const string Turkish = "tr";

    public const string English = "en";

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    // Windows arayüz dili ilk erişimde okunur ve saklanır: uygulama sonradan iş parçacığı kültürünü değiştirse de
    // "Auto" çözümlemesi bu değerden yapılır (geri besleme olmaz).
    private static readonly string SystemUiLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    private static volatile string _language = Resolve(Auto);

    /// <summary>Yalnızca testler: Windows arayüz dilinin yerine geçer (makine diline bağlı kararsızlığı önler).</summary>
    internal static string? SystemLanguageOverride { get; set; }

    /// <summary>Dil değişince (yalnızca gerçekten değiştiyse) çağrılır; <see cref="SetLanguage(string?)"/>'ı çağıran iş parçacığında.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Şu an kullanılan dil: <c>tr</c> ya da <c>en</c> (hiçbir zaman <c>Auto</c> değildir).</summary>
    public static string Language => _language;

    /// <summary>Biçimlendirme kültürü (sayılar, tarihler).</summary>
    public static CultureInfo Culture => _language == Turkish ? TurkishCulture : EnglishCulture;

    /// <summary>Ayar değerini <c>Auto</c>, <c>tr</c> ya da <c>en</c>'e indirger; bilinmeyen/boş değer <c>Auto</c> olur.</summary>
    public static string NormalizeSetting(string? setting)
    {
        var value = setting?.Trim();
        if (string.Equals(value, Turkish, StringComparison.OrdinalIgnoreCase))
        {
            return Turkish;
        }

        return string.Equals(value, English, StringComparison.OrdinalIgnoreCase) ? English : Auto;
    }

    /// <summary>
    /// Ayarı gerçek dile çevirir. <c>Auto</c>: Windows arayüz dili Türkçe ise <c>tr</c>, değilse <c>en</c>.
    /// <paramref name="systemUiLanguage"/> verilmezse Windows arayüz dili kullanılır (testler verir).
    /// </summary>
    public static string Resolve(string? setting, string? systemUiLanguage = null)
    {
        var normalized = NormalizeSetting(setting);
        if (normalized != Auto)
        {
            return normalized;
        }

        var system = systemUiLanguage ?? SystemLanguageOverride ?? SystemUiLanguage;
        return system.StartsWith(Turkish, StringComparison.OrdinalIgnoreCase) ? Turkish : English;
    }

    /// <summary>Dili ayara göre değiştirir. Değiştiyse <see cref="LanguageChanged"/> tetiklenir ve true döner.</summary>
    public static bool SetLanguage(string? setting, string? systemUiLanguage = null)
    {
        var resolved = Resolve(setting, systemUiLanguage);
        if (resolved == _language)
        {
            return false;
        }

        _language = resolved;
        var handlers = LanguageChanged;
        if (handlers is not null)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Action>())
            {
                try
                {
                    handler();
                }
                catch (Exception)
                {
                    // Bir aboneyi (kapanmış pencere vb.) etkileyen hata diğerlerini engellemez.
                }
            }
        }

        return true;
    }

    /// <summary>Anahtarın şu anki dildeki metni. Anahtar yoksa İngilizce yedeğe, o da yoksa anahtarın kendisine düşer.</summary>
    public static string T(string key) => Lookup(key, _language);

    /// <summary>Biçimli metin (<c>{0}</c>, <c>{1}</c> ...). Argümansız <see cref="T(string)"/> biçimlendirme yapmaz; içindeki <c>{q}</c> olduğu gibi kalır.</summary>
    public static string T(string key, params object?[] args) => string.Format(Culture, Lookup(key, _language), args);

    /// <summary>Belirli bir dildeki metin (testler ve iki dilli karşılaştırmalar için).</summary>
    public static string In(string language, string key) => Lookup(key, language);

    private static string Lookup(string key, string language)
    {
        var table = language == Turkish ? Strings.Tr : Strings.En;
        if (table.TryGetValue(key, out var text))
        {
            return text;
        }

        return Strings.En.TryGetValue(key, out var fallback) ? fallback : key;
    }
}
