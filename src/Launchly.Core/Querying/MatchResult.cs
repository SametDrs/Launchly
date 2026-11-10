namespace Launchly.Core.Querying;

/// <summary>Bulanık eşleştirme sonucu.</summary>
/// <param name="IsMatch">Eşleşti mi.</param>
/// <param name="Score">0..1000; yüksek = daha iyi. Eşleşme yoksa 0.</param>
/// <param name="Indices">Hedef metinde eşleşen karakterlerin konumları (vurgulama için).</param>
public readonly record struct MatchResult(bool IsMatch, int Score, IReadOnlyList<int> Indices)
{
    public static MatchResult None { get; } = new(false, 0, []);
}
