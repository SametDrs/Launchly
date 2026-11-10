using Launchly.Core.Plugins;
using Launchly.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Core;

/// <summary>Genel kısayolu kaydeden/bırakan bileşen (App'te Win32, testte sahte).</summary>
public interface IHotkeyRegistrar
{
    /// <summary>Önceki kaydı bırakıp yenisini kaydeder. Başarısızsa kullanıcıya gösterilecek açıklama <paramref name="error"/> içindedir.</summary>
    bool TryRegister(string hotkey, out string? error);

    /// <summary>Şu an bir kısayol kayıtlı mı (başlangıçta çakışma olduysa false).</summary>
    bool IsRegistered { get; }
}

/// <summary>Windows açılışında başlatma kaydı (App'te <c>HKCU\...\Run</c>, testte sahte).</summary>
public interface IAutoStart
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

public readonly record struct SettingsApplyResult(bool Success, string? Error);

/// <summary>
/// Ayar penceresinden gelen değişiklikleri doğrular ve yeniden başlatmadan uygular:
/// kısayol (başarısızsa eskisine döner), otomatik başlatma, web kaynakları (anahtar kelime dizini yenilenir),
/// ardından <c>settings.json</c>'a yazar. Hata olursa canlı ayarlara hiçbir şey uygulanmaz.
/// </summary>
public sealed class SettingsService
{
    private readonly Settings _live;
    private readonly JsonStorage<Settings> _storage;
    private readonly IHotkeyRegistrar _hotkey;
    private readonly IAutoStart _autoStart;
    private readonly PluginManager _plugins;
    private readonly ILogger _logger;

    public SettingsService(
        Settings live,
        JsonStorage<Settings> storage,
        IHotkeyRegistrar hotkey,
        IAutoStart autoStart,
        PluginManager plugins,
        ILogger<SettingsService>? logger = null)
    {
        _live = live;
        _storage = storage;
        _hotkey = hotkey;
        _autoStart = autoStart;
        _plugins = plugins;
        _logger = logger ?? NullLogger<SettingsService>.Instance;
    }

    /// <summary>Başarılı uygulamadan sonra (arka plan, odak davranışı gibi arayüz ayarları için).</summary>
    public event Action<Settings>? Applied;

    /// <summary>Canlı ayarların bağımsız kopyası (ayar penceresi bunu düzenler).</summary>
    public Settings CreateDraft() => new()
    {
        Version = _live.Version,
        Hotkey = _live.Hotkey,
        Backdrop = _live.Backdrop,
        MaxResults = _live.MaxResults,
        DisabledPlugins = [.. _live.DisabledPlugins ?? []],
        SearchSources = (_live.SearchSources ?? []).Where(s => s is not null).Select(s => s.Clone()).ToList(),
        HideOnFocusLost = _live.HideOnFocusLost,
        StartOnSystemStartup = _live.StartOnSystemStartup,
        Language = _live.Language,
    };

    public static bool TryValidate(Settings draft, out string? error)
    {
        if (!HotkeyParser.TryParse(draft.Hotkey, out _))
        {
            error = $"\"{draft.Hotkey}\" geçerli bir kısayol değil (Ctrl, Alt ya da Win'den en az biri ve bir tuş gerekir).";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in draft.SearchSources)
        {
            if (!source.TryValidate(out var sourceError))
            {
                error = $"\"{source.Title}\": {sourceError}";
                return false;
            }

            if (source.Enabled && !seen.Add(source.ActionKeyword.Trim()))
            {
                error = $"\"{source.ActionKeyword}\" anahtar kelimesi birden fazla etkin kaynakta kullanılıyor.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public SettingsApplyResult Apply(Settings draft)
    {
        if (!TryValidate(draft, out var validationError))
        {
            return new SettingsApplyResult(false, validationError);
        }

        var oldHotkey = _live.Hotkey;
        var hotkeyChanged = !string.Equals(draft.Hotkey, oldHotkey, StringComparison.OrdinalIgnoreCase);

        // Metin değişmese de mevcut kayıt başarısızsa (ör. başlangıçta çakışma) yeniden denenir.
        // Kısayol değişmediyse yeniden kayıt yalnızca denenir; başarısızlığı diğer ayarları engellemez (uyarı döner).
        string? hotkeyWarning = null;
        if (hotkeyChanged)
        {
            if (!_hotkey.TryRegister(draft.Hotkey, out var hotkeyError))
            {
                // Eski kayıt kaydedici tarafından bırakılmış olabilir: eskisini geri yükle.
                RestoreHotkey(oldHotkey);
                return new SettingsApplyResult(false, hotkeyError ?? "Kısayol kaydedilemedi.");
            }
        }
        else if (!_hotkey.IsRegistered && !_hotkey.TryRegister(draft.Hotkey, out var retryError))
        {
            hotkeyWarning = retryError ?? "Kısayol kaydedilemedi.";
            _logger.LogWarning("Kısayol yeniden kaydedilemedi: {Error}", hotkeyWarning);
        }

        if (draft.StartOnSystemStartup != _live.StartOnSystemStartup)
        {
            try
            {
                _autoStart.SetEnabled(draft.StartOnSystemStartup);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Otomatik başlatma ayarı yazılamadı");
                if (hotkeyChanged)
                {
                    RestoreHotkey(oldHotkey);
                }

                return new SettingsApplyResult(false, "Otomatik başlatma ayarı yazılamadı: " + ex.Message);
            }
        }

        _live.Hotkey = draft.Hotkey;
        _live.Backdrop = draft.Backdrop;
        _live.HideOnFocusLost = draft.HideOnFocusLost;
        _live.StartOnSystemStartup = draft.StartOnSystemStartup;
        _live.SearchSources = draft.SearchSources.Select(s => s.Clone()).ToList();

        try
        {
            _storage.Save(_live);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ayarlar kaydedilemedi");
            _plugins.RefreshKeywords();
            Applied?.Invoke(_live);
            var saveError = "Ayarlar uygulandı ancak dosyaya yazılamadı: " + ex.Message;
            if (hotkeyWarning is not null)
            {
                saveError += " Ayrıca kısayol kaydedilemedi: " + hotkeyWarning;
            }

            return new SettingsApplyResult(false, saveError);
        }

        _plugins.RefreshKeywords();
        Applied?.Invoke(_live);
        return new SettingsApplyResult(true, hotkeyWarning is null ? null : "Ayarlar kaydedildi ancak kısayol kaydedilemedi: " + hotkeyWarning);
    }

    private void RestoreHotkey(string hotkey)
    {
        if (!_hotkey.TryRegister(hotkey, out var error))
        {
            _logger.LogWarning("Eski kısayol geri yüklenemedi: {Error}", error);
        }
    }
}
