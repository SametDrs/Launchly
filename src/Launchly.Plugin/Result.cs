namespace Launchly.Plugin;

/// <summary>Sonuç listesindeki tek satır.</summary>
public sealed class Result
{
    public required string Title { get; init; }

    public string SubTitle { get; init; } = string.Empty;

    /// <summary>Segoe Fluent Icons karakteri (1-2 karakter). Boşsa arayüz başlığın ilk harfini gösterir.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Gerçek ikon görseli (Windows'ta dondurulmuş <c>System.Windows.Media.ImageSource</c>); doluysa <see cref="Icon"/> yerine gösterilir. SDK WPF'ye bağlı olmasın diye <c>object</c>.</summary>
    public object? IconImage { get; init; }

    /// <summary>Sıralama puanı; yüksek olan üstte.</summary>
    public int Score { get; init; }

    /// <summary>Sağda gösterilen tür etiketi (ör. "Application").</summary>
    public string TypeLabel { get; init; } = string.Empty;

    /// <summary>Tab'a basılınca arama kutusuna yazılacak metin; boşsa Tab bir şey yapmaz.</summary>
    public string AutoComplete { get; init; } = string.Empty;

    /// <summary>Enter ile çalışır. true dönerse pencere gizlenir, false dönerse açık kalır. null = eylem yok.</summary>
    public Func<Task<bool>>? Action { get; init; }

    /// <summary>
    /// Eylem menüsündeki ek eylemler (Ctrl+K). Varsayılan boş: eylemi olmayan eklentiler değişmeden derlenir.
    /// "Aç" buraya konmaz; <see cref="Action"/> doluysa menüye ilk öğe olarak arayüz ekler.
    /// </summary>
    public IReadOnlyList<ResultAction> ContextActions { get; init; } = [];

    /// <summary>Sonucu üreten eklentinin kimliği; <c>PluginManager</c> doldurur.</summary>
    public string PluginId { get; set; } = string.Empty;
}
