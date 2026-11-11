namespace Launchly.Plugins.Programs;

/// <summary>
/// Kullanım kaydı yokken (ya da az iken) boş kutuda gösterilen sabit başlangıç listesi.
/// Her öğe sırayla çözülür: AUMID eşleşmesi -> hedef dosya adı eşleşmesi -> <see cref="ProgramKind.Command"/> yedeği.
/// </summary>
internal static class StarterApps
{
    private static readonly Definition[] Definitions =
    [
        new("Dosya Gezgini", "Microsoft.Windows.Explorer", ["explorer.exe"], "explorer.exe", false),
        new("Varsayılan tarayıcı", null, [], null, true),
        new("Ayarlar", "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", [], "ms-settings:", false),
        new("Komut İstemi", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", ["wt.exe", "cmd.exe"], "cmd.exe", false),
        new("Not Defteri", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", ["notepad.exe"], "notepad.exe", false),
        new("Hesap Makinesi", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ["calc.exe"], "calc.exe", false),
    ];

    /// <summary>Tanım sayısı (tarayıcı bulunamazsa sonuç bir eksik olur).</summary>
    public static int Count => Definitions.Length;

    /// <summary>
    /// Başlangıç listesini mevcut girdilere göre çözer (tablo sırası). <paramref name="defaultBrowserPath"/> null/boş dönerse
    /// ya da hata verirse, ya da o exe'ye giden kısayol yoksa tarayıcı atlanır. Aynı girdiye çözülen öğe bir kez yer alır.
    /// </summary>
    public static List<ProgramEntry> Resolve(IReadOnlyList<ProgramEntry> entries, Func<string?>? defaultBrowserPath)
    {
        var result = new List<ProgramEntry>(Definitions.Length);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in Definitions)
        {
            var targets = def.TargetFiles;
            if (def.IsBrowser)
            {
                var browser = SafeFileName(defaultBrowserPath);
                if (browser is null)
                {
                    continue;
                }

                targets = [browser];
            }

            var entry = ResolveOne(entries, def, targets);
            if (entry is not null && ids.Add(entry.Id))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static ProgramEntry? ResolveOne(IReadOnlyList<ProgramEntry> entries, Definition def, string[] targets)
    {
        if (def.Aumid is not null)
        {
            var byAumid = entries.FirstOrDefault(e => string.Equals(e.AppUserModelId, def.Aumid, StringComparison.OrdinalIgnoreCase));
            if (byAumid is not null)
            {
                return byAumid;
            }
        }

        foreach (var target in targets)
        {
            var byTarget = entries.FirstOrDefault(e =>
                e.Kind == ProgramKind.Shortcut
                && e.TargetPath.Length > 0
                && string.Equals(Path.GetFileName(e.TargetPath), target, StringComparison.OrdinalIgnoreCase));
            if (byTarget is not null)
            {
                return byTarget;
            }
        }

        return def.FallbackCommand is null
            ? null
            : new ProgramEntry { Name = def.Name, Kind = ProgramKind.Command, ShortcutPath = def.FallbackCommand };
    }

    private static string? SafeFileName(Func<string?>? resolver)
    {
        try
        {
            var path = resolver?.Invoke()?.Trim().Trim('"');
            var name = string.IsNullOrEmpty(path) ? null : Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception)
        {
            return null;   // tarayıcı bulunamadı: öğe atlanır
        }
    }

    private sealed record Definition(string Name, string? Aumid, string[] TargetFiles, string? FallbackCommand, bool IsBrowser);
}
