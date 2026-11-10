using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launchly.Core;

namespace Launchly.App.SettingsUi;

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

    [ObservableProperty] private string _hotkey = string.Empty;
    [ObservableProperty] private BackdropType _backdrop;
    [ObservableProperty] private bool _hideOnFocusLost;
    [ObservableProperty] private bool _startOnSystemStartup;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    public SettingsViewModel(SettingsService service)
    {
        _service = service;
        Revert();
    }

    public ObservableCollection<SearchSourceItem> Sources { get; } = [];

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
