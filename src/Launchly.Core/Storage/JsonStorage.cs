using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Core.Storage;

/// <summary>
/// <typeparamref name="T"/> nesnesini JSON dosyasında saklar.
/// Kayıt: önce geçici dosyaya yazılır, sonra asıl dosyanın yerine konur; önceki sağlam sürüm <c>.bak</c> olur.
/// Yükleme: asıl dosya bozuk ya da okunamazsa <c>.bak</c> denenir; o da yoksa varsayılan nesne döner.
/// Bozuk asıl dosya <c>.corrupt</c> uzantısıyla kenara alınır ki sağlam yedeğin üzerine yazılmasın.
/// </summary>
public class JsonStorage<T> where T : class, new()
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Yerel, elle düzenlenebilir dosya: "+" ve Türkçe karakterler kaçışsız yazılsın.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;
    private readonly object _sync = new();

    public JsonStorage(string filePath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        _logger = logger ?? NullLogger.Instance;
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    public string CorruptPath => FilePath + ".corrupt";

    private string TempPath => FilePath + ".tmp";

    /// <summary>
    /// Veriyi yükler. Hiçbir sağlam kaynak yoksa varsayılan nesne döner.
    /// Asıl dosya yoksa ya da yedekten dönüldüyse sonuç hemen diske yazılır.
    /// </summary>
    public T Load()
    {
        lock (_sync)
        {
            if (TryRead(FilePath, out var data))
            {
                return data;
            }

            if (File.Exists(FilePath))
            {
                QuarantineCorruptFile();
            }

            if (TryRead(BackupPath, out var backup))
            {
                _logger.LogWarning("{File} okunamadı; yedekten yüklendi: {Backup}", FilePath, BackupPath);
                SaveIfMainMissing(backup);
                return backup;
            }

            _logger.LogInformation("{File} bulunamadı; varsayılan içerik oluşturuluyor", FilePath);
            var fresh = new T();
            SaveIfMainMissing(fresh);
            return fresh;
        }
    }

    /// <summary>Veriyi diske yazar; önceki dosya <c>.bak</c> olarak saklanır.</summary>
    public void Save(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_sync)
        {
            SaveCore(data);
        }
    }

    // Bozuk dosya kenara alınamadıysa yazmayız; yoksa File.Replace bozuk içeriği .bak'a taşır ve sağlam yedek kaybolur.
    private void SaveIfMainMissing(T data)
    {
        if (!File.Exists(FilePath))
        {
            SaveCore(data);
        }
    }

    private void SaveCore(T data)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(data, SerializerOptions);
        File.WriteAllText(TempPath, json);

        if (File.Exists(FilePath))
        {
            File.Replace(TempPath, FilePath, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(TempPath, FilePath, overwrite: true);
        }
    }

    private bool TryRead(string path, out T data)
    {
        data = null!;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            if (result is null)
            {
                _logger.LogWarning("{File} boş (null) içerik döndürdü", path);
                return false;
            }

            data = result;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "{File} okunamadı", path);
            return false;
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            File.Move(FilePath, CorruptPath, overwrite: true);
            _logger.LogWarning("Bozuk dosya kenara alındı: {Corrupt}", CorruptPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Bozuk dosya kenara alınamadı: {File}", FilePath);
        }
    }
}
