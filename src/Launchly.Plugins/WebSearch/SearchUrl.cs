using Launchly.Core;

namespace Launchly.Plugins.WebSearch;

/// <summary>Arama adresi üretimi ve şema denetimi.</summary>
public static class SearchUrl
{
    /// <summary>Şablondaki <c>{q}</c> yerine aranan metni URL kodlayarak koyar.</summary>
    public static string Build(string template, string search) =>
        template.Replace(SearchSource.Placeholder, Uri.EscapeDataString(search), StringComparison.Ordinal);

    /// <summary>Yalnızca mutlak http/https adresleri güvenlidir.</summary>
    public static bool IsSafe(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
