using System.Windows.Media;
using Launchly.Plugin;

namespace Launchly.App.ViewModels;

/// <summary>Sonuç listesindeki tek satır; bir eklenti <see cref="Result"/>'ını arayüze uyarlar.</summary>
public sealed class ResultItemViewModel
{
    // Simge karosu renkleri; başlığa göre sabit seçilir (aynı sonuç hep aynı renk).
    private static readonly string[] Palette =
    [
        "#2F80ED", "#34A853", "#6B7280", "#D98E1F", "#374151", "#8B5CF6", "#0EA5E9", "#EF4444",
    ];

    public ResultItemViewModel(Result result)
    {
        Result = result;
        Glyph = result.Icon;
        Image = result.IconImage as ImageSource;
        Initial = Image is null && string.IsNullOrEmpty(result.Icon) && result.Title.Length > 0
            ? char.ToUpperInvariant(result.Title[0]).ToString()
            : string.Empty;
        IconColor = Image is not null ? "Transparent" : Palette[(result.Title.Length == 0 ? 0 : result.Title.Sum(c => c)) % Palette.Length];
    }

    public Result Result { get; }

    /// <summary>Gerçek ikon görseli (dondurulmuş); yoksa null ve harf/simge karosu gösterilir.</summary>
    public ImageSource? Image { get; }

    public string Title => Result.Title;

    public string SubTitle => Result.SubTitle;

    public string TypeLabel => Result.TypeLabel;

    /// <summary>Segoe Fluent Icons karakteri (eklentinin verdiği <see cref="Result.Icon"/>); yoksa boş.</summary>
    public string Glyph { get; }

    /// <summary>Simge yoksa başlığın ilk harfi (ikon yazı tipinde Latin harf olmadığından ayrı alan).</summary>
    public string Initial { get; }

    /// <summary>Simge karosunun arka plan rengi (#RRGGBB).</summary>
    public string IconColor { get; }

    public bool HasSubTitle => !string.IsNullOrEmpty(Result.SubTitle);
}
