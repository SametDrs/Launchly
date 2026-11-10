using System.Text.Json.Serialization;

namespace Launchly.Core;

/// <summary>Pencere arka plan efekti.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackdropType
{
    None,
    Mica,
    Acrylic,
}
