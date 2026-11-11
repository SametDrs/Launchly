using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Launchly.Core;
using Launchly.Plugins.Programs;

namespace Launchly.App.SettingsUi;

/// <summary>Ayar penceresindeki gizlenen uygulama satırı.</summary>
public sealed record HiddenProgramItem(string Id, string Name);

/// <summary>Ayar penceresindeki tek web kaynağı satırı (düzenlenebilir).</summary>
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

/// <summary>Ayar penceresinin durumu: taslak değerler; Uygula <see cref="SettingsService"/>'e gider, Geri al taslağı canlı ayarlardan yeniler.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly ProgramUsageStore? _usage;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty] private string _hotkey = string.Empty;
    [ObservableProperty] private BackdropType _backdrop;
    [ObservableProperty] private bool _hideOnFocusLost;
    [ObservableProperty] private bool _startOnSystemStartup;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    /// <param name="usage">Gizlenen uygulamalar bölümünün kaynağı; verilmezse bölüm boş kalır.</param>
    public SettingsViewModel(SettingsService service, ProgramUsageStore? usage = null)
    {
        _service = service;
        _usage = usage;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Revert();

        if (usage is not null)
        {
            // Store değişikliği (menüden Gizle, buradan Göster) herhangi bir iş parçacığından gelebilir: UI'a taşınır.
            usage.Changed += OnUsageChanged;
            RefreshHidden();
        }
    }

    public ObservableCollection<SearchSourceItem> Sources { get; } = [];

    /// <summary>
    /// Gizlenen uygulamalar. Taslak/Uygula akışına girmez: "Göster" store'u hemen günceller, "Geri al" bunu geri almaz.
    /// </summary>
    public ObservableCollection<HiddenProgramItem> HiddenPrograms { get; } = [];

    public bool HasHiddenPrograms => HiddenPrograms.Count > 0;

    /// <summary>"Göster": gizlemeyi hemen kaldırır; liste <see cref="ProgramUsageStore.Changed"/> ile yenilenir.</summary>
    public void Unhide(HiddenProgramItem item) => _usage?.Unhide(item.Id);

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
        if (_usage is null)
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

    public void AddSource() => Sources.Add(new SearchSourceItem());

    public void RemoveSource(SearchSourceItem item) => Sources.Remove(item);

    /// <summary>Taslağı canlı ayarlardan yeniler (kaydedilmemiş değişiklikler atılır).</summary>
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

        Message = string.Empty;
        IsError = false;
    }

    /// <summary>Taslağı uygular; sonuç <see cref="Message"/> ve <see cref="IsError"/> ile görünür.</summary>
    public bool Apply()
    {
        var draft = _service.CreateDraft();
        draft.Hotkey = Hotkey;
        draft.Backdrop = Backdrop;
        draft.HideOnFocusLost = HideOnFocusLost;
        draft.StartOnSystemStartup = StartOnSystemStartup;
        draft.SearchSources = Sources.Select(s => s.ToSource()).ToList();

        var result = _service.Apply(draft);
        IsError = !result.Success || result.Error is not null;   // başarıyla dönen Error bir uyarıdır
        Message = result.Success ? result.Error ?? "Ayarlar uygulandı." : result.Error ?? "Ayarlar uygulanamadı.";
        return result.Success;
    }
}
