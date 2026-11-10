namespace Launchly.App.ViewModels;

/// <summary>Sonuç listesindeki tek satır. Şimdilik sahte veri taşır (03-sorgu-cekirdegi gerçek sonuçlara geçirir).</summary>
/// <param name="Glyph">Segoe Fluent Icons karakteri.</param>
/// <param name="IconColor">Simge karosunun arka plan rengi (#RRGGBB).</param>
public sealed record ResultItemViewModel(string Title, string TypeLabel, string Glyph, string IconColor);
