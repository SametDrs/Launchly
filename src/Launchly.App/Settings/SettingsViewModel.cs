using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Launchly.Core;
using Launchly.Plugin;
using Launchly.Plugins.Programs;
using Launchly.Plugins.WebSearch;

namespace Launchly.App.SettingsUi;

/// <summary>Ayar penceresinin sol menüsündeki sayfalar.</summary>
public enum SettingsPage
{
    General,
    Appearance,
    SearchSources,
    Plugins,
    About,
}

/// <summary>Bir geri çağrıyı gecikmeyle çalıştıran zamanlayıcı; testte sahtesi verilir (gerçek bekleme olmaz).</summary>
public interface IDelayScheduler
{
    /// <summary>Gecikme sonunda <paramref name="callback"/> bir kez çalışır; dönen nesne Dispose edilirse hiç çalışmaz.</summary>
    IDisposable Schedule(TimeSpan delay, Action callback);
}

/// <summary>UI iş parçacığında <see cref="DispatcherTimer"/> ile çalışan gerçek zamanlayıcı.</summary>
public sealed class DispatcherDelayScheduler : IDelayScheduler
{
    private readonly Dispatcher _dispatcher;

    public DispatcherDelayScheduler(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            callback();
        };
        timer.Start();
        return new Handle(timer);
    }

    private sealed class Handle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}

/// <summary>Ayar penceresindeki gizlenen uygulama satırı.</summary>
public sealed record HiddenProgramItem(string Id, string Name);

/// <summary>Eklentiler sayfasındaki tek eklenti satırı; <see cref="IsEnabled"/> taslaktır, Uygula ile <c>DisabledPlugins</c>'e yazılır.</summary>
public sealed partial class PluginToggleItem : ObservableObject
{
    [ObservableProperty] private bool _isEnabled = true;

    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Gösterim için anahtar kelimeler ("Global" = anahtar kelimesiz her sorguda).</summary>
    public required string Keywords { get; init; }

    /// <summary>Adın altında görünen tek cümlelik açıklama (boş olabilir).</summary>
    public string Description { get; init; } = string.Empty;

    public bool HasDescription => Description.Length > 0;

    /// <summary>Gizlenen uygulamalar bu eklentinin altında gösterilir.</summary>
    public bool IsPrograms => Id == ProgramsPlugin.PluginId;

    public static string FormatKeywords(IReadOnlyList<string> keywords)
    {
        var named = keywords
            .Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim() != PluginMetadata.GlobalKeyword)
            .Select(k => k.Trim())
            .ToList();
        var global = keywords.Any(k => k?.Trim() == PluginMetadata.GlobalKeyword);
        return (global, named.Count) switch
        {
            (true, 0) => "Global",
            (true, _) => "Global · " + string.Join(", ", named),
            (false, 0) => "—",
            _ => string.Join(", ", named),
        };
    }
}

/// <summary>Ayar penceresindeki tek arama kaynağı satırı (düzenlenebilir).</summary>
public sealed partial class SearchSourceItem : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _actionKeyword = string.Empty;
    [ObservableProperty] private string _urlTemplate = string.Empty;
    [ObservableProperty] private string _icon = string.Empty;
    [ObservableProperty] private bool _enabled = true;

    public static SearchSourceItem From(SearchSource s) => new()
    {
        Title = s.Title,
        ActionKeyword = s.ActionKeyword,
        UrlTemplate = s.UrlTemplate,
        Icon = s.Icon,
        Enabled = s.Enabled,
    };

    public SearchSource ToSource() => new()
    {
        Title = Title.Trim(),
        ActionKeyword = ActionKeyword.Trim(),
        UrlTemplate = UrlTemplate.Trim(),
        Icon = Icon,
        Enabled = Enabled,
    };
}

/// <summary>
/// Ayar penceresinin durumu: tüm sayfaların taslak değerleri tek örnekte tutulur (sayfa değiştirmek taslağı sıfırlamaz).
/// Uygula tüm sayfaları birlikte <see cref="SettingsService"/>'e gönderir, Geri al taslağı canlı ayarlardan yeniler.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>Başarı mesajının ekranda kalma süresi.</summary>
    public static readonly TimeSpan SuccessMessageLifetime = TimeSpan.FromSeconds(2);

    public const string RepositoryUrl = "https://github.com/SametDrs/Launchly";

    private readonly SettingsService _service;
    private readonly ProgramUsageStore? _usage;
    private readonly Func<IReadOnlyList<PluginMetadata>> _plugins;
    private readonly Func<(bool Supported, string? Reason)> _backdropAvailability;
    private readonly IUrlOpener? _urlOpener;
    private readonly Dispatcher _dispatcher;
    private readonly IDelayScheduler _scheduler;
    private IDisposable? _messageTimer;
    private int _messageToken;
    private bool _disposed;

    // Taslakta görünmeyen (kayıtlı olmayan) eklenti Id'leri: Uygula'da korunur.
    private List<string> _unknownDisabled = [];

    [ObservableProperty] private SettingsPage _selectedPage = SettingsPage.General;
    [ObservableProperty] private string _hotkey = string.Empty;
    [ObservableProperty] private BackdropType _backdrop;
    [ObservableProperty] private bool _hideOnFocusLost;
    [ObservableProperty] private bool _startOnSystemStartup;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isBackdropSupported = true;
    [ObservableProperty] private string? _backdropUnavailableReason;

    /// <param name="usage">Gizlenen uygulamalar bölümünün kaynağı; verilmezse bölüm boş kalır.</param>
    /// <param name="plugins">Eklentiler sayfasının listesi (salt okunur); her Geri al'da yeniden okunur (WebSearch kelimeleri değişebilir).</param>
    /// <param name="backdropAvailability">Mica/Acrylic seçilebilir mi, değilse neden; verilmezse destekleniyor sayılır.</param>
    /// <param name="urlOpener">Hakkında sayfasındaki repo bağlantısı için.</param>
    public SettingsViewModel(
        SettingsService service,
        ProgramUsageStore? usage = null,
        Func<IReadOnlyList<PluginMetadata>>? plugins = null,
        Func<(bool Supported, string? Reason)>? backdropAvailability = null,
        IUrlOpener? urlOpener = null,
        IDelayScheduler? scheduler = null)
    {
        _service = service;
        _usage = usage;
        _plugins = plugins ?? (() => []);
        _backdropAvailability = backdropAvailability ?? (() => (true, null));
        _urlOpener = urlOpener;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _scheduler = scheduler ?? new DispatcherDelayScheduler(_dispatcher);
        Revert();

        if (usage is not null)
        {
            // Store değişikliği (menüden Gizle, buradan Göster) herhangi bir iş parçacığından gelebilir: UI'a taşınır.
            usage.Changed += OnUsageChanged;
            RefreshHidden();
        }
    }

    public ObservableCollection<SearchSourceItem> Sources { get; } = [];

    public ObservableCollection<PluginToggleItem> Plugins { get; } = [];

    /// <summary>
    /// Gizlenen uygulamalar. Taslak/Uygula akışına girmez: "Göster" store'u hemen günceller, "Geri al" bunu geri almaz.
    /// </summary>
    public ObservableCollection<HiddenProgramItem> HiddenPrograms { get; } = [];

    public bool HasHiddenPrograms => HiddenPrograms.Count > 0;

    /// <summary>Uygulama sürümü (<see cref="AssemblyInformationalVersionAttribute"/>; derleme meta verisi kısaltılır).</summary>
    public string AppVersion { get; } = FormatVersion(
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public string RepositoryLink => RepositoryUrl;

    public static string FormatVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "bilinmiyor";
        }

        // "1.0.0+<commit>" → "1.0.0 (abc1234)"
        var plus = informational.IndexOf('+');
        if (plus < 0)
        {
            return informational;
        }

        var meta = informational[(plus + 1)..];
        var version = informational[..plus];
        return meta.Length == 0 ? version : $"{version} ({meta[..Math.Min(7, meta.Length)]})";
    }

    /// <summary>"Göster": gizlemeyi hemen kaldırır; liste <see cref="ProgramUsageStore.Changed"/> ile yenilenir.</summary>
    public void Unhide(HiddenProgramItem item) => _usage?.Unhide(item.Id);

    /// <summary>Repo bağlantısını varsayılan tarayıcıda açar; hata mesaj alanında gösterilir.</summary>
    public void OpenRepository()
    {
        if (_urlOpener is null)
        {
            return;
        }

        try
        {
            _urlOpener.Open(RepositoryUrl);
        }
        catch (Exception ex)
        {
            SetStatus("Bağlantı açılamadı: " + ex.Message, isError: true, autoClear: false);
        }
    }

    private void OnUsageChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            RefreshHidden();
        }
        else
        {
            _dispatcher.BeginInvoke(RefreshHidden);
        }
    }

    private void RefreshHidden()
    {
        if (_usage is null || _disposed)
        {
            return;
        }

        HiddenPrograms.Clear();
        foreach (var h in _usage.Hidden)
        {
            HiddenPrograms.Add(new HiddenProgramItem(h.Id, h.Name.Length > 0 ? h.Name : h.Id));
        }

        OnPropertyChanged(nameof(HasHiddenPrograms));
    }

    public bool IsBackdropNone
    {
        get => Backdrop == BackdropType.None;
        set { if (value) Backdrop = BackdropType.None; }
    }

    public bool IsBackdropMica
    {
        get => Backdrop == BackdropType.Mica;
        set { if (value) Backdrop = BackdropType.Mica; }
    }

    public bool IsBackdropAcrylic
    {
        get => Backdrop == BackdropType.Acrylic;
        set { if (value) Backdrop = BackdropType.Acrylic; }
    }

    partial void OnBackdropChanged(BackdropType value)
    {
        OnPropertyChanged(nameof(IsBackdropNone));
        OnPropertyChanged(nameof(IsBackdropMica));
        OnPropertyChanged(nameof(IsBackdropAcrylic));
    }

    /// <summary>Mica/Acrylic desteğini yeniden okur (açılışta ve Windows saydamlık ayarı değişince).</summary>
    public void RefreshBackdropAvailability()
    {
        var (supported, reason) = _backdropAvailability();
        IsBackdropSupported = supported;
        BackdropUnavailableReason = supported ? null : reason;
    }

    /// <summary>Tuş yakalama kutusundan gelen birleşimi kısayol metnine çevirir; geçersizse false.</summary>
    public bool TryCaptureHotkey(uint modifiers, uint virtualKey)
    {
        if (!HotkeyParser.TryFormat(modifiers, virtualKey, out var text))
        {
            return false;
        }

        Hotkey = text;
        return true;
    }

    public void AddSource() => Sources.Add(new SearchSourceItem { Enabled = true, UrlTemplate = "https://" });

    public void RemoveSource(SearchSourceItem item) => Sources.Remove(item);

    /// <summary>Tüm sayfaların taslağını canlı ayarlardan yeniler (kaydedilmemiş değişiklikler atılır). Seçili sayfa korunur.</summary>
    public void Revert()
    {
        var draft = _service.CreateDraft();
        Hotkey = draft.Hotkey;
        Backdrop = draft.Backdrop;
        HideOnFocusLost = draft.HideOnFocusLost;
        StartOnSystemStartup = draft.StartOnSystemStartup;
        Sources.Clear();
        foreach (var s in draft.SearchSources)
        {
            Sources.Add(SearchSourceItem.From(s));
        }

        var disabled = new HashSet<string>(draft.DisabledPlugins, StringComparer.Ordinal);
        Plugins.Clear();
        foreach (var plugin in _plugins())
        {
            Plugins.Add(new PluginToggleItem
            {
                Id = plugin.Id,
                Name = plugin.Name,
                Description = plugin.Description ?? string.Empty,
                Keywords = PluginToggleItem.FormatKeywords(plugin.ActionKeywords),
                IsEnabled = !disabled.Contains(plugin.Id),
            });
        }

        var known = Plugins.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        _unknownDisabled = draft.DisabledPlugins.Where(id => !known.Contains(id)).ToList();

        RefreshBackdropAvailability();
        SetStatus(string.Empty, isError: false, autoClear: false);
    }

    /// <summary>
    /// Alt çubuk mesajını ayarlar. Önceki zamanlayıcı her zaman iptal edilir; jeton, iptal edilemeyen
    /// (zaten tetiklenmiş) eski geri çağrının yeni mesajı silmesini engeller.
    /// </summary>
    private void SetStatus(string message, bool isError, bool autoClear)
    {
        _messageTimer?.Dispose();
        _messageTimer = null;
        var token = ++_messageToken;
        IsError = isError;
        Message = message;

        if (autoClear && !_disposed)
        {
            _messageTimer = _scheduler.Schedule(SuccessMessageLifetime, () =>
            {
                if (token == _messageToken && !_disposed)
                {
                    _messageTimer = null;
                    Message = string.Empty;
                    IsError = false;
                }
            });
        }
    }

    /// <summary>Tüm sayfaların taslağını birlikte uygular; sonuç <see cref="Message"/> ve <see cref="IsError"/> ile görünür.</summary>
    public bool Apply()
    {
        var draft = _service.CreateDraft();
        draft.Hotkey = Hotkey;
        draft.Backdrop = Backdrop;
        draft.HideOnFocusLost = HideOnFocusLost;
        draft.StartOnSystemStartup = StartOnSystemStartup;
        draft.SearchSources = Sources.Select(s => s.ToSource()).ToList();
        draft.DisabledPlugins = [.. _unknownDisabled, .. Plugins.Where(p => !p.IsEnabled).Select(p => p.Id)];

        var result = _service.Apply(draft);
        var isError = !result.Success || result.Error is not null;   // başarıyla dönen Error bir uyarıdır
        var message = result.Success ? result.Error ?? "Ayarlar uygulandı." : result.Error ?? "Ayarlar uygulanamadı.";
        SetStatus(message, isError, autoClear: !isError);   // yalnızca düz başarı kaybolur; hata/uyarı kalır
        return result.Success;
    }

    /// <summary><see cref="ProgramUsageStore.Changed"/> aboneliğini bırakır (pencere gerçekten kapanırken).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageTimer?.Dispose();
        _messageTimer = null;
        if (_usage is not null)
        {
            _usage.Changed -= OnUsageChanged;
        }
    }
}
