using Microsoft.Extensions.Logging;

namespace Launchly.Core;

/// <summary>
/// Kullanıcı ayarları. Tek dosyada (<see cref="DataLocation.SettingsPath"/>) tutulur.
/// Yeni alanlar sonraki görevlerde eklenir; eksik alanlar JSON'dan varsayılan değerle gelir.
/// </summary>
public sealed class Settings
{
    /// <summary>Güncel ayar şeması sürümü.</summary>
    /// <remarks>2: varsayılan tema Mica'dan Acrylic'e geçti (v1 dosyasındaki "Mica" bir kez Acrylic'e çevrilir).</remarks>
    public const int CurrentVersion = 2;

    /// <summary>Ayar şeması sürümü (göç için). Alanı olmayan dosya güncel sürümle okunur ve göçe girmez.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Pencereyi açan kısayol.</summary>
    public string Hotkey { get; set; } = "Alt+Space";

    /// <summary>Pencere arka planı: None (düz koyu), Mica veya Acrylic. Desteklenmiyorsa düz renge düşer.</summary>
    public BackdropType Backdrop { get; set; } = BackdropType.Acrylic;

    /// <summary>Kutu boşken (ana ekran listesi) gösterilecek en fazla sonuç. Sabittir, ayar dosyasına yazılmaz.</summary>
    public const int EmptyQueryMaxResults = 6;

    /// <summary>Gösterilecek en fazla sonuç sayısı (yazarken; boş kutuda ayrıca <see cref="EmptyQueryMaxResults"/>).</summary>
    public int MaxResults { get; set; } = 8;

    /// <summary>Kullanıcının kapattığı eklentilerin kimlikleri (<c>PluginMetadata.Id</c>).</summary>
    public List<string> DisabledPlugins { get; set; } = [];

    /// <summary>Web arama kaynakları (WebSearch eklentisi). Ayarda yoksa varsayılanlar gelir.</summary>
    public List<SearchSource> SearchSources { get; set; } = SearchSource.CreateDefaults();

    /// <summary>Pencere odağı kaybedince (başka pencereye tıklanınca) gizlensin mi.</summary>
    public bool HideOnFocusLost { get; set; } = true;

    /// <summary>Windows açılışında başlat.</summary>
    public bool StartOnSystemStartup { get; set; }

    /// <summary>Arayüz dili.</summary>
    public string Language { get; set; } = "tr";

    /// <summary>
    /// Elle düzenlenmiş/bozuk JSON'dan gelen null değerleri düzeltir: null liste boş (kaynaklarda varsayılan) olur,
    /// listelerdeki null öğeler atılır, null metinler varsayılana/boşa döner. Yüklemeden hemen sonra çağrılır.
    /// </summary>
    public Settings Normalize(ILogger? logger = null)
    {
        Migrate(logger);

        DisabledPlugins = (DisabledPlugins ?? []).Where(id => id is not null).ToList();
        SearchSources = SearchSources is null
            ? SearchSource.CreateDefaults()
            : SearchSources.Where(s => s is not null).ToList();
        foreach (var source in SearchSources)
        {
            source.Normalize();
        }

        if (!HotkeyParser.TryParse(Hotkey, out _))
        {
            // Boş, bozuk ya da eski sürümden kalma geçersiz kısayol (ör. yalnızca Shift): varsayılana dön.
            logger?.LogWarning("Geçersiz kısayol \"{Hotkey}\", varsayılana (Alt+Space) dönüldü", Hotkey);
            Hotkey = "Alt+Space";
        }

        Language ??= "tr";
        return this;
    }

    /// <summary>
    /// Eski şema sürümlerinden tek seferlik geçiş. Sonuç diske bir sonraki kayıtta (çıkış ya da Uygula) yazılır.
    /// v1 → v2: "Mica" eski varsayılandı, kullanıcı seçimi sayılmaz ve Acrylic olur; None/Acrylic korunur.
    /// v2 dosyasındaki Mica açık seçimdir ve korunur.
    /// </summary>
    private void Migrate(ILogger? logger)
    {
        if (Version >= CurrentVersion)
        {
            return;
        }

        var from = Version;
        if (Version < 2 && Backdrop == BackdropType.Mica)
        {
            Backdrop = BackdropType.Acrylic;
            logger?.LogInformation("Ayar geçişi v{From} → v{To}: tema Mica (eski varsayılan) → Acrylic", from, CurrentVersion);
        }
        else
        {
            logger?.LogInformation("Ayar geçişi v{From} → v{To}: tema {Backdrop} korundu", from, CurrentVersion, Backdrop);
        }

        Version = CurrentVersion;
    }
}
