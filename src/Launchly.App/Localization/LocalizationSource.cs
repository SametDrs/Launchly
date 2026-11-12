using System.ComponentModel;
using Launchly.Core.Localization;

namespace Launchly.App.Localization;

/// <summary>
/// XAML bağlamalarının kaynağı: <c>this[anahtar]</c> o anki dildeki metni verir. Dil değişince tek bir
/// <c>Item[]</c> bildirimi tüm <c>{loc:T ...}</c> bağlamalarını (açık pencereler dahil) yeniler.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    public static LocalizationSource Instance { get; } = new();

    private LocalizationSource()
    {
        Loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Loc.T(key);
}
