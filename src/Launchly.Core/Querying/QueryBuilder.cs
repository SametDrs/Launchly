using Launchly.Plugin;

namespace Launchly.Core.Querying;

/// <summary>Ham metni <see cref="Query"/>'ye çevirir.</summary>
public static class QueryBuilder
{
    /// <summary>
    /// İlk kelime <paramref name="activeKeywords"/> içindeyse (büyük/küçük harf duyarsız)
    /// ve arkasından en az bir boşluk geliyorsa <see cref="Query.ActionKeyword"/> olur ve kalanı <see cref="Query.Search"/> olur.
    /// Tek başına yazılan kelime ("f") anahtar kelime sayılmaz. Değilse anahtar kelime boştur ve tüm metin aranır.
    /// </summary>
    /// <param name="raw">Kutudaki metin.</param>
    /// <param name="activeKeywords">Etkin eklentilerin anahtar kelimeleri (global "*" sayılmaz).</param>
    public static Query Build(string? raw, IReadOnlyCollection<string> activeKeywords)
    {
        raw ??= string.Empty;
        // Sondaki boşluk korunur: anahtar kelime yalnızca arkasından boşluk geliyorsa geçerlidir ("f" global, "f " Files).
        var text = raw.TrimStart();
        if (text.Trim().Length == 0)
        {
            return new Query(raw, string.Empty, string.Empty);
        }

        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        var first = text[..end];
        if (end < text.Length &&
            first != PluginMetadata.GlobalKeyword &&
            activeKeywords.Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            return new Query(raw, first, text[end..].Trim());
        }

        return new Query(raw, string.Empty, text.Trim());
    }
}
