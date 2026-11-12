using System.Runtime.CompilerServices;
using Launchly.Core.Localization;

// Dil (Loc) süreç genelinde tek durumdur; dili değiştiren testler başkasını bozmasın diye testler sırayla çalışır.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Launchly.Tests;

/// <summary>
/// Testlerin dili makinenin Windows diline bağlı olmasın diye sabitlenir: "Auto" her zaman Türkçe çözülür
/// ve varsayılan dil <c>tr</c>'dir. İngilizce metin bekleyen ya da dil değişimini sınayan test <see cref="Use"/> ile dili açıkça seçer.
/// </summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Pin()
    {
        Loc.SystemLanguageOverride = Loc.Turkish;
        Loc.SetLanguage(Loc.Turkish);
    }

    /// <summary>Dili <paramref name="language"/> (tr/en) yapar; Dispose'da Türkçe'ye geri döner.</summary>
    public static IDisposable Use(string language)
    {
        Loc.SetLanguage(language);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Loc.SetLanguage(Loc.Turkish);
    }
}
