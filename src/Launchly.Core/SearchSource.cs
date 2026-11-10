namespace Launchly.Core;

/// <summary>Web arama kaynağı (ör. YouTube). <c>settings.json</c>'da saklanır; ayar penceresinden düzenlenir.</summary>
public sealed class SearchSource
{
    /// <summary>Şablondaki aranan metin yer tutucusu.</summary>
    public const string Placeholder = "{q}";

    /// <summary>Görünen ad (ör. "YouTube").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Tetikleyici kelime (ör. "youtube"); küçük/büyük harf duyarsız.</summary>
    public string ActionKeyword { get; set; } = string.Empty;

    /// <summary>Adres şablonu; <c>{q}</c> yerine <c>Uri.EscapeDataString(search)</c> konur.</summary>
    public string UrlTemplate { get; set; } = string.Empty;

    /// <summary>Segoe Fluent Icons karakteri; boşsa eklenti varsayılan simgeyi kullanır.</summary>
    public string Icon { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public SearchSource Clone() => (SearchSource)MemberwiseClone();

    /// <summary>Kaynağı doğrular: başlık, tek kelimelik anahtar kelime ve http(s) + <c>{q}</c> içeren şablon.</summary>
    public bool TryValidate(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            error = "Başlık boş olamaz.";
        }
        else if (string.IsNullOrWhiteSpace(ActionKeyword) || ActionKeyword.Any(char.IsWhiteSpace) || ActionKeyword.Trim() == "*")
        {
            error = "Anahtar kelime boş olamaz, boşluk içeremez ve \"*\" olamaz.";
        }
        else if (!UrlTemplate.Contains(Placeholder, StringComparison.Ordinal))
        {
            error = $"Adres şablonu {Placeholder} içermeli (ör. https://site.com/search?q={Placeholder}).";
        }
        else if (!Uri.TryCreate(UrlTemplate.Replace(Placeholder, "x", StringComparison.Ordinal), UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Adres şablonu http:// veya https:// ile başlayan geçerli bir adres olmalı.";
        }
        else
        {
            error = null;
        }

        return error is null;
    }

    /// <summary>Varsayılan kaynaklar (ayarda hiç yoksa kullanılır).</summary>
    public static List<SearchSource> CreateDefaults() =>
    [
        new() { Title = "Google", ActionKeyword = "google", UrlTemplate = "https://www.google.com/search?q={q}" },
        new() { Title = "YouTube", ActionKeyword = "youtube", UrlTemplate = "https://www.youtube.com/results?search_query={q}" },
        new() { Title = "Bing", ActionKeyword = "bing", UrlTemplate = "https://www.bing.com/search?q={q}" },
        new() { Title = "DuckDuckGo", ActionKeyword = "duckduckgo", UrlTemplate = "https://duckduckgo.com/?q={q}" },
        new() { Title = "GitHub", ActionKeyword = "github", UrlTemplate = "https://github.com/search?q={q}" },
        new() { Title = "Wikipedia", ActionKeyword = "wikipedia", UrlTemplate = "https://en.wikipedia.org/w/index.php?search={q}" },
    ];
}
