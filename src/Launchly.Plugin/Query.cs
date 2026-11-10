namespace Launchly.Plugin;

/// <summary>Kullanıcının yazdığı metnin ayrıştırılmış hali.</summary>
public sealed class Query
{
    public static Query Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    public Query(string raw, string actionKeyword, string search)
    {
        Raw = raw;
        ActionKeyword = actionKeyword;
        Search = search;
        Terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Kutuya yazılan metin, olduğu gibi.</summary>
    public string Raw { get; }

    /// <summary>Eşleşen anahtar kelime; yoksa boş (yalnızca global eklentiler çalışır).</summary>
    public string ActionKeyword { get; }

    /// <summary>Anahtar kelimeden sonra kalan metin (baş/son boşluksuz).</summary>
    public string Search { get; }

    /// <summary><see cref="Search"/>'ün boşluğa göre kelimeleri.</summary>
    public string[] Terms { get; }
}
