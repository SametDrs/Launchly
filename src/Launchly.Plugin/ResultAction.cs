namespace Launchly.Plugin;

/// <summary>
/// Bir sonucun eylem menüsündeki (Ctrl+K / Actions) tek satır. "Aç" eylemi buraya konmaz: <see cref="Result.Action"/>
/// doluysa menünün ilk öğesi olarak arayüz ekler.
/// </summary>
public sealed class ResultAction
{
    /// <summary>"Yönetici olarak çalıştır" eyleminin kimliği. Ctrl+Shift+Enter, menü açmadan bu Id'li eylemi çalıştırır.</summary>
    public const string RunAsAdminId = "launchly.runas";

    /// <summary>İsteğe bağlı kimlik; arayüz belirli eylemleri (ör. <see cref="RunAsAdminId"/>) bununla tanır.</summary>
    public string? Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Segoe Fluent Icons karakteri; boş olabilir.</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>Yalnızca gösterim (ör. "Ctrl+Shift+Enter"); satırın sağında yazar.</summary>
    public string? Hint { get; init; }

    /// <summary>true ise eylem false döndüğünde güncel sorgu yeniden çalışır (ör. gizle/sabitle sonrası liste yenilenir).</summary>
    public bool RequeryAfter { get; init; }

    /// <summary>Eylem. true dönerse pencere gizlenir, false dönerse açık kalır (<see cref="Result.Action"/> ile aynı kural).</summary>
    public required Func<Task<bool>> Execute { get; init; }
}
