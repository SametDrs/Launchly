namespace Launchly.Plugins.Programs;

/// <summary>Start Menu klasörlerindeki .lnk dosyalarından uygulama listesi çıkarır.</summary>
internal sealed class ProgramScanner
{
    private static readonly string[] AllowedTargetExtensions = [".exe", ".bat", ".cmd", ".com", ".msc"];
    private static readonly string[] NoiseWords = ["uninstall", "kaldır", "kaldir"];

    private readonly IReadOnlyList<string> _roots;
    private readonly Func<string, string> _resolveTarget;

    public ProgramScanner(IReadOnlyList<string> roots, Func<string, string>? resolveTarget = null)
    {
        _roots = roots;
        _resolveTarget = resolveTarget ?? ShortcutResolver.ResolveTarget;
    }

    /// <summary>Kullanıcı + ortak Start Menu "Programs" klasörleri.</summary>
    public static IReadOnlyList<string> DefaultRoots() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
    ];

    /// <summary>Tüm kökleri tarar; yinelenenler (aynı ad + aynı hedef) elenir, ad sırasıyla döner.</summary>
    public List<ProgramEntry> Scan(CancellationToken cancellationToken = default)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ProgramEntry>();

        foreach (var root in _roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(file);
                if (IsNoise(name))
                {
                    continue;
                }

                var target = _resolveTarget(file);
                if (target.Length > 0 && !AllowedTargetExtensions.Contains(Path.GetExtension(target), StringComparer.OrdinalIgnoreCase))
                {
                    continue;   // belge, klasör, URL vb.
                }

                if (seen.Add(name + "|" + target))
                {
                    list.Add(new ProgramEntry { Name = name, ShortcutPath = file, TargetPath = target });
                }
            }
        }

        list.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }

    private static bool IsNoise(string name) =>
        NoiseWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
}
