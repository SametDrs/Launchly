namespace Launchly.Plugin;

/// <summary>Eklentinin kimliği ve tetikleyicileri.</summary>
public sealed class PluginMetadata
{
    /// <summary>Global eklenti işareti: anahtar kelime olmadan her sorguda çalışır.</summary>
    public const string GlobalKeyword = "*";

    /// <summary>Kalıcı, benzersiz kimlik (ayarlarda eklentiyi anmak için kullanılır).</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Tetikleyici kelimeler (küçük/büyük harf duyarsız). <see cref="GlobalKeyword"/> = global.</summary>
    public IReadOnlyList<string> ActionKeywords { get; init; } = [GlobalKeyword];

    /// <summary>true ise kutu boşken de çalışır (global eklentiler için; ör. uygulama listesi). Varsayılan: false.</summary>
    public bool HandlesEmptyQuery { get; init; }

    /// <summary>Sorgu yazıldıktan sonra eklentiyi çalıştırmadan önce beklenecek süre (ms); pahalı eklentiler için.</summary>
    public int SearchDelayMs { get; init; }
}
