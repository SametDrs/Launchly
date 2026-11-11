using Launchly.Core.Querying;

namespace Launchly.Plugins.Programs;

/// <summary>.lnk ve AppsFolder taramalarını tek listede birleştiren saf yardımcılar (G/Ç yok).</summary>
internal static class ProgramCatalog
{
    /// <summary>Program sayılan hedef uzantıları (kısayol hedefi ya da AppsFolder dosya yolu).</summary>
    public static readonly string[] AllowedTargetExtensions = [".exe", ".bat", ".cmd", ".com", ".msc"];

    private static readonly string[] NoiseWords = ["uninstall", "kaldır", "kaldir"];

    /// <summary>Kaldırıcı gibi listede istenmeyen ad mı ("uninstall/kaldır").</summary>
    public static bool IsNoise(string? name) =>
        !string.IsNullOrEmpty(name) && NoiseWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Paketli (MSIX/Store) uygulama AUMID'i mi: <c>PaketAile!UygulamaId</c>.</summary>
    public static bool IsPackaged(string parsingName) =>
        parsingName.Contains('!') && !parsingName.Contains('\\') && !parsingName.Contains("://", StringComparison.Ordinal);

    /// <summary>
    /// Birleşik liste (ada göre sıralı):
    /// <list type="bullet">
    /// <item>Tüm .lnk girdileri.</item>
    /// <item>Paketli AppsFolder öğeleri her zaman (<see cref="ProgramKind.AppsFolder"/>).</item>
    /// <item>Paketsiz öğe, görünen adı (katlanmış, büyük/küçük harf duyarsız) bir .lnk ile aynıysa eklenmez; ayrıştırma adı o
    /// girdinin <see cref="ProgramEntry.AppUserModelId"/> alanına yazılır. Eşi yoksa <see cref="ProgramKind.AppsFolder"/> olarak eklenir.</item>
    /// <item>Elenenler: "uninstall/kaldır" adlılar, web bağlantıları (<c>http(s)://</c>) ve program olmayan dosya yolları
    /// (<c>.html</c>, <c>.chm</c>, <c>.url</c> ...; .lnk taraması da bunları atlar).</item>
    /// </list>
    /// Girdi listeleri değiştirilmez; .lnk girdileri kopyalanır.
    /// </summary>
    public static List<ProgramEntry> Merge(IReadOnlyList<ProgramEntry> lnkEntries, IReadOnlyList<(string ParsingName, string DisplayName)> appsItems)
    {
        var result = new List<ProgramEntry>(lnkEntries.Count + appsItems.Count);
        var byName = new Dictionary<string, List<ProgramEntry>>(StringComparer.Ordinal);
        foreach (var source in lnkEntries)
        {
            var copy = Copy(source);
            result.Add(copy);
            var key = NameKey(copy.Name);
            if (!byName.TryGetValue(key, out var list))
            {
                byName[key] = list = [];
            }

            list.Add(copy);
        }

        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawParsing, rawDisplay) in appsItems)
        {
            var parsing = rawParsing?.Trim() ?? string.Empty;
            var display = rawDisplay?.Trim() ?? string.Empty;
            if (parsing.Length == 0 || display.Length == 0 || IsNoise(display) || !seenApps.Add(parsing))
            {
                continue;
            }

            if (IsPackaged(parsing))
            {
                result.Add(new ProgramEntry { Name = display, Kind = ProgramKind.AppsFolder, AppUserModelId = parsing });
                continue;
            }

            if (IsNotAProgram(parsing))
            {
                continue;
            }

            if (byName.TryGetValue(NameKey(display), out var twins))
            {
                // .lnk kazanır (hedef yolu, yönetici olarak çalıştır vb. için). Kimliği başlangıç listesi eşleşmesine taşınır.
                var target = twins.FirstOrDefault(e => e.AppUserModelId.Length == 0);
                if (target is not null)
                {
                    target.AppUserModelId = parsing;
                }

                continue;
            }

            result.Add(new ProgramEntry { Name = display, Kind = ProgramKind.AppsFolder, AppUserModelId = parsing });
        }

        result.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return result;
    }

    /// <summary>
    /// Önbellekten (elle düzenlenmiş, bozuk ya da eski sürüm) gelen girdileri temizler: null alanlar boşa döner;
    /// adı olmayan, bilinmeyen türdeki ya da açılacak yolu/kimliği olmayan girdiler atılır.
    /// </summary>
    public static ProgramEntry[] Sanitize(IEnumerable<ProgramEntry?>? entries)
    {
        if (entries is null)
        {
            return [];
        }

        var list = new List<ProgramEntry>();
        foreach (var e in entries)
        {
            if (e is null)
            {
                continue;
            }

            e.Name = e.Name?.Trim() ?? string.Empty;
            e.ShortcutPath ??= string.Empty;
            e.TargetPath ??= string.Empty;
            e.AppUserModelId ??= string.Empty;

            var valid = e.Name.Length > 0 && e.Kind switch
            {
                ProgramKind.Shortcut => e.ShortcutPath.Length > 0,
                ProgramKind.AppsFolder => e.AppUserModelId.Length > 0,
                _ => false,   // Command girdileri taranmaz, önbellekte olmamalı; bilinmeyen tür (ileri sürüm) atılır.
            };

            if (valid)
            {
                list.Add(e);
            }
        }

        return list.ToArray();
    }

    // Web bağlantısı ya da program olmayan dosya yolu (belgeler, yardım dosyaları). steam:// gibi uygulama protokolleri kalır.
    private static bool IsNotAProgram(string parsingName)
    {
        if (parsingName.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || parsingName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!parsingName.Contains('\\') || parsingName.Contains("://", StringComparison.Ordinal))
        {
            return false;   // AUMID benzeri ad (ör. Microsoft.Windows.Explorer, Microsoft.AutoGenerated.{GUID})
        }

        var extension = Path.GetExtension(parsingName);
        return !AllowedTargetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static string NameKey(string name) => StringMatcher.Fold(name.Trim());

    private static ProgramEntry Copy(ProgramEntry e) => new()
    {
        Name = e.Name,
        ShortcutPath = e.ShortcutPath,
        TargetPath = e.TargetPath,
        Kind = e.Kind,
        AppUserModelId = e.AppUserModelId,
    };
}
