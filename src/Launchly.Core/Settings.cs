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

    /// <summary>Gösterilecek en fazla sonuç sayısı.</summary>
    public int MaxResults { get; set; } = 8;

    /// <summary>Windows açılışında başlat.</summary>
    public bool StartOnSystemStartup { get; set; }

    /// <summary>Arayüz dili.</summary>
    public string Language { get; set; } = "tr";
}
