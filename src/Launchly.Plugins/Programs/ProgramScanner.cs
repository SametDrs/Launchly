using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Programs;

/// <summary>Start Menu klasörlerindeki .lnk dosyalarından uygulama listesi çıkarır.</summary>
internal sealed class ProgramScanner
{
    private readonly IReadOnlyList<string> _roots;
    private readonly Func<string, string> _resolveTarget;
    private readonly Func<string, string> _displayName;
    private readonly ILogger _logger;

    /// <param name="roots">Taranacak klasörler.</param>
    /// <param name="resolveTarget">Kısayol hedefi çözücü; varsayılan IShellLink.</param>
    /// <param name="logger">Atlanan klasör/kısayol uyarıları için.</param>
    /// <param name="displayName">
    /// Kısayolun görünen adı; varsayılan kabuk adı (<c>SHGetFileInfo(SHGFI_DISPLAYNAME)</c>, <c>desktop.ini</c> çevirisi dahil).
    /// Boş dönerse ya da hata verirse dosya adı kullanılır.
    /// </param>
    public ProgramScanner(
        IReadOnlyList<string> roots,
        Func<string, string>? resolveTarget = null,
        ILogger? logger = null,
        Func<string, string>? displayName = null)
    {
        _roots = roots;
        _resolveTarget = resolveTarget ?? ShortcutResolver.ResolveTarget;
        _displayName = displayName ?? ShellDisplayName.Get;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Kullanıcı + ortak Start Menu "Programs" klasörleri.</summary>
    public static IReadOnlyList<string> DefaultRoots() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
    ];

    /// <summary>
    /// Tüm kökleri tarar; yinelenenler (aynı ad + aynı hedef) elenir, ad sırasıyla döner.
    /// Okunamayan bir alt klasör ya da çözülemeyen bir kısayol yalnızca kendini atlatır (loglanır), tarama sürer.
    /// </summary>
    public List<ProgramEntry> Scan(CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ProgramEntry>();

        foreach (var root in _roots)
        {
            if (Directory.Exists(root))
            {
                ScanDirectory(root, seen, list, cancellationToken);
            }
        }

        list.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }

    private void ScanDirectory(string dir, HashSet<string> seen, List<ProgramEntry> list, CancellationToken cancellationToken)
    {
        List<string> files;
        List<string> subdirs;
        try
        {
            var info = new DirectoryInfo(dir);
            files = info.EnumerateFiles("*.lnk")
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .Select(f => f.FullName)
                .ToList();
            subdirs = info.EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0)
                .Select(d => d.FullName)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Klasör taranamadı, atlandı: {Directory}", dir);
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (ProgramCatalog.IsNoise(fileName))
            {
                continue;
            }

            string target;
            try
            {
                target = _resolveTarget(file);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Kısayol çözülemedi, atlandı: {Path}", file);
                continue;
            }

            if (target.Length > 0 && !ProgramCatalog.AllowedTargetExtensions.Contains(Path.GetExtension(target), StringComparer.OrdinalIgnoreCase))
            {
                continue;   // belge, klasör, URL vb.
            }

            var name = DisplayNameOf(file, fileName);
            if (ProgramCatalog.IsNoise(name))
            {
                continue;   // çevirisinde "kaldır" geçen kaldırıcı
            }

            if (seen.Add(name + "|" + target))
            {
                list.Add(new ProgramEntry { Name = name, ShortcutPath = file, TargetPath = target });
            }
        }

        foreach (var sub in subdirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanDirectory(sub, seen, list, cancellationToken);
        }
    }

    private string DisplayNameOf(string file, string fileName)
    {
        string name;
        try
        {
            name = _displayName(file)?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Kısayolun görünen adı alınamadı, dosya adı kullanıldı: {Path}", file);
            return fileName;
        }

        // .lnk uzantısı kabukta her zaman gizlidir (NeverShowExt); yine de güvence.
        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4].TrimEnd();
        }

        return name.Length > 0 ? name : fileName;
    }
}
