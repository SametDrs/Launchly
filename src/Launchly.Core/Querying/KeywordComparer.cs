namespace Launchly.Core.Querying;

/// <summary>
/// Anahtar kelime eşitliği: büyük/küçük harf duyarsız ve Türkçe i harfleri (<c>i İ ı I</c>) birbirine eşit sayılır.
/// "FİLM", "FILM", "film" ve "fılm" aynı kelimedir; kültür ayarına bağlı değildir.
/// </summary>
public sealed class KeywordComparer : IEqualityComparer<string>
{
    public static readonly KeywordComparer Instance = new();

    private KeywordComparer()
    {
    }

    public bool Equals(string? x, string? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && Fold(x) == Fold(y));

    public int GetHashCode(string obj) => Fold(obj).GetHashCode();

    private static string Fold(string s) =>
        s.Replace('İ', 'i').Replace('ı', 'i').Replace('I', 'i').ToLowerInvariant();
}
