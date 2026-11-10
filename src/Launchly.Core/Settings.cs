namespace Launchly.Core;

/// <summary>
/// Kullanıcı ayarları. Tek dosyada (<see cref="DataLocation.SettingsPath"/>) tutulur.
/// Yeni alanlar sonraki görevlerde eklenir; eksik alanlar JSON'dan varsayılan değerle gelir.
/// </summary>
public sealed class Settings
{
    /// <summary>Ayar şeması sürümü; ileride göç (migration) için.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Pencereyi açan kısayol.</summary>
    public string Hotkey { get; set; } = "Alt+Space";

    /// <summary>Pencere arka planı: None (düz koyu), Mica veya Acrylic. Desteklenmiyorsa düz renge düşer.</summary>
    public BackdropType Backdrop { get; set; } = BackdropType.Mica;

    /// <summary>Gösterilecek en fazla sonuç sayısı.</summary>
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
}
