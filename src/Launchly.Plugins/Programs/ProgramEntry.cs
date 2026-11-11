using System.Text.Json.Serialization;

namespace Launchly.Plugins.Programs;

/// <summary>Bir program girdisinin kaynağı; açma ve kimlik biçimini belirler.</summary>
public enum ProgramKind
{
    /// <summary>Start Menu .lnk kısayolu (<see cref="ProgramEntry.ShortcutPath"/>).</summary>
    Shortcut = 0,

    /// <summary><c>shell:AppsFolder</c> öğesi (paketli/Store uygulaması ya da eşi olmayan kabuk öğesi).</summary>
    AppsFolder = 1,

    /// <summary>Başlangıç listesinin yedek komutu (ör. <c>notepad.exe</c>, <c>ms-settings:</c>); taranmaz.</summary>
    Command = 2,
}

/// <summary>Listede gösterilen tek bir uygulama.</summary>
public sealed class ProgramEntry
{
    /// <summary>Görünen ad (Windows dilinde kabuk adı; alınamazsa kısayol dosya adı, uzantısız).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="ProgramKind.Shortcut"/>: .lnk dosyasının yolu (açma ve ikon için).
    /// <see cref="ProgramKind.Command"/>: çalıştırılacak komut. <see cref="ProgramKind.AppsFolder"/>: boş.
    /// </summary>
    public string ShortcutPath { get; set; } = string.Empty;

    /// <summary>Kısayolun hedef yolu; çözülemediyse (MSI/Store kısayolu) ya da AppsFolder öğesiyse boş.</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>Girdinin kaynağı. v1 önbellekte alan yoktur: <see cref="ProgramKind.Shortcut"/>.</summary>
    public ProgramKind Kind { get; set; } = ProgramKind.Shortcut;

    /// <summary>AppsFolder ayrıştırma adı (paketli uygulamada AUMID, ör. <c>Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</c>).</summary>
    public string AppUserModelId { get; set; } = string.Empty;

    /// <summary>Kalıcı kimlik (kullanım kaydının anahtarı); <see cref="StringComparer.OrdinalIgnoreCase"/> ile karşılaştırılır.</summary>
    [JsonIgnore]
    public string Id => Kind switch
    {
        ProgramKind.AppsFolder => "app:" + AppUserModelId,
        ProgramKind.Command => "cmd:" + ShortcutPath,
        _ => "lnk:" + ShortcutPath,
    };

    /// <summary>Kabuğa (ShellExecute) verilecek dizge.</summary>
    [JsonIgnore]
    public string LaunchPath => Kind == ProgramKind.AppsFolder
        ? @"shell:AppsFolder\" + AppUserModelId
        : ShortcutPath;
}

/// <summary>Diskteki önbellek içeriği (<c>programs.json</c>).</summary>
public sealed class ProgramCache
{
    /// <summary>Şema sürümü. 2: <see cref="ProgramEntry.Kind"/> ve <see cref="ProgramEntry.AppUserModelId"/> eklendi.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public DateTime ScannedAtUtc { get; set; }

    public List<ProgramEntry> Programs { get; set; } = [];
}
