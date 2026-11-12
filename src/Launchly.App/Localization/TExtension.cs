using System.Windows.Data;
using System.Windows.Markup;

namespace Launchly.App.Localization;

/// <summary>
/// XAML metin anahtarı: <c>Text="{loc:T main.settings}"</c>. Metin bağlama olarak verilir; dil çalışırken değişince yenilenir.
/// Anahtarlar <c>Launchly.Core.Localization.Strings</c> içindedir (tr ve en).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
