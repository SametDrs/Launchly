namespace Launchly.Plugins.Programs;

/// <summary>Start Menu'de bulunan tek bir uygulama kısayolu.</summary>
public sealed class ProgramEntry
{
    /// <summary>Görünen ad (kısayol dosya adı, uzantısız).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>.lnk dosyasının yolu; açma ve ikon için kullanılır.</summary>
    public string ShortcutPath { get; set; } = string.Empty;

    /// <summary>Kısayolun hedef yolu; çözülemediyse (MSI/Store kısayolu) boş.</summary>
    public string TargetPath { get; set; } = string.Empty;
}

/// <summary>Diskteki önbellek içeriği (<c>programs.json</c>).</summary>
public sealed class ProgramCache
{
    public int Version { get; set; } = 1;

    public DateTime ScannedAtUtc { get; set; }

    public List<ProgramEntry> Programs { get; set; } = [];
}
