namespace Launchly.Core.Querying;

/// <summary>
/// Sade bulanık eşleştirici. Büyük/küçük harf ve Türkçe harf farkı (ı/İ/i, ç, ğ, ö, ş, ü) gözetmez.
/// Puan bantları (yüksekten düşüğe): tam eşleşme 1000, önek 800-899, kelime başı alt dize 600-699,
/// kısaltma 500-599 (<c>vsc</c> -> Visual Studio Code), ortada alt dize 300-399, sıralı bulanık 1-299.
/// Boşluklu sorguda her kelime ayrı eşleşmeli (sırası serbest); puan ortalamadır.
/// </summary>
public static class StringMatcher
{
    /// <summary>Boş sorgu her şeyle 0 puanla eşleşir (süzme yok).</summary>
    public static MatchResult Match(string? query, string? target)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new MatchResult(true, 0, []);
        }

        if (string.IsNullOrEmpty(target))
        {
            return MatchResult.None;
        }

        var folded = Fold(target);
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 1)
        {
            return MatchTerm(Fold(terms[0]), target, folded);
        }

        var total = 0;
        var indices = new SortedSet<int>();
        foreach (var term in terms)
        {
            var m = MatchTerm(Fold(term), target, folded);
            if (!m.IsMatch)
            {
                return MatchResult.None;
            }

            total += m.Score;
            indices.UnionWith(m.Indices);
        }

        return new MatchResult(true, total / terms.Length, indices.ToArray());
    }

    private static MatchResult MatchTerm(string q, string original, string t)
    {
        if (q.Length > t.Length)
        {
            return MatchResult.None;
        }

        if (q == t)
        {
            return new MatchResult(true, 1000, Range(0, q.Length));
        }

        var extra = t.Length - q.Length;

        if (t.StartsWith(q, StringComparison.Ordinal))
        {
            return new MatchResult(true, 899 - Math.Min(extra, 99), Range(0, q.Length));
        }

        // Kelime başında alt dize (ilk uygun konum).
        var firstAnywhere = -1;
        for (var pos = t.IndexOf(q, StringComparison.Ordinal); pos >= 0;
             pos = pos + 1 < t.Length ? t.IndexOf(q, pos + 1, StringComparison.Ordinal) : -1)
        {
            firstAnywhere = firstAnywhere < 0 ? pos : firstAnywhere;
            if (IsWordStart(original, pos))
            {
                return new MatchResult(true, 699 - Math.Min(pos, 50) - Math.Min(extra, 49), Range(pos, q.Length));
            }
        }

        var acronym = MatchAcronym(q, original);
        if (acronym.IsMatch)
        {
            return acronym;
        }

        if (firstAnywhere >= 0)
        {
            return new MatchResult(true, 399 - Math.Min(firstAnywhere, 50) - Math.Min(extra, 49), Range(firstAnywhere, q.Length));
        }

        return MatchFuzzy(q, original, t);
    }

    /// <summary>Sorgu, kelime baş harflerinin sıralı bir alt kümesiyse eşleşir.</summary>
    private static MatchResult MatchAcronym(string q, string original)
    {
        if (q.Length < 2)
        {
            return MatchResult.None;
        }

        var initials = new List<int>();
        for (var i = 0; i < original.Length; i++)
        {
            if (IsWordStart(original, i) && char.IsLetterOrDigit(original[i]))
            {
                initials.Add(i);
            }
        }

        var matched = new List<int>(q.Length);
        var k = 0;
        foreach (var idx in initials)
        {
            if (k < q.Length && Fold(original[idx]) == q[k])
            {
                matched.Add(idx);
                k++;
            }
        }

        if (k < q.Length)
        {
            return MatchResult.None;
        }

        var score = 599 - 5 * (initials.Count - q.Length) - (matched[0] == initials[0] ? 0 : 20);
        return new MatchResult(true, Math.Max(score, 500), matched);
    }

    /// <summary>Sıralı alt dizi eşleşmesi: ileri tarama sonunu, geri tarama en sıkı başlangıcı bulur.</summary>
    private static MatchResult MatchFuzzy(string q, string original, string t)
    {
        var qi = 0;
        var end = -1;
        for (var i = 0; i < t.Length && qi < q.Length; i++)
        {
            if (t[i] == q[qi])
            {
                qi++;
                end = i;
            }
        }

        if (qi < q.Length)
        {
            return MatchResult.None;
        }

        var indices = new int[q.Length];
        qi = q.Length - 1;
        for (var i = end; i >= 0 && qi >= 0; i--)
        {
            if (t[i] == q[qi])
            {
                indices[qi--] = i;
            }
        }

        var score = 150 - Math.Min(indices[0], 20) - Math.Min(t.Length - q.Length, 20);
        for (var k = 0; k < indices.Length; k++)
        {
            if (IsWordStart(original, indices[k]))
            {
                score += 12;
            }

            if (k > 0)
            {
                var gap = indices[k] - indices[k - 1] - 1;
                score += gap == 0 ? 10 : -Math.Min(gap, 8);
            }
        }

        return new MatchResult(true, Math.Clamp(score, 1, 299), indices);
    }

    private static bool IsWordStart(string s, int i) =>
        i == 0
        || !char.IsLetterOrDigit(s[i - 1])
        || (char.IsUpper(s[i]) && char.IsLower(s[i - 1]));

    private static int[] Range(int start, int count) => Enumerable.Range(start, count).ToArray();

    /// <summary>Eşleştiricinin karşılaştırma biçimi: küçük harf, Türkçe harfler katlanmış (ı/İ -> i, ç -> c ...).</summary>
    public static string Fold(string s) => string.Create(s.Length, s, static (span, src) =>
    {
        for (var i = 0; i < src.Length; i++)
        {
            span[i] = Fold(src[i]);
        }
    });

    private static char Fold(char c) => c switch
    {
        'ı' or 'İ' => 'i',
        'ç' or 'Ç' => 'c',
        'ğ' or 'Ğ' => 'g',
        'ö' or 'Ö' => 'o',
        'ş' or 'Ş' => 's',
        'ü' or 'Ü' => 'u',
        _ => char.ToLowerInvariant(c),
    };
}
