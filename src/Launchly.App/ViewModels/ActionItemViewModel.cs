using Launchly.Plugin;

namespace Launchly.App.ViewModels;

/// <summary>Eylem menüsündeki tek satır; bir <see cref="ResultAction"/>'ı arayüze uyarlar.</summary>
public sealed class ActionItemViewModel
{
    public ActionItemViewModel(ResultAction action)
    {
        Action = action;
    }

    public ResultAction Action { get; }

    public string Title => Action.Title;

    /// <summary>Segoe Fluent Icons karakteri; boş olabilir.</summary>
    public string Glyph => Action.Glyph ?? string.Empty;

    /// <summary>Satırın sağında gösterilen ipucu (ör. "Ctrl+Shift+Enter"); yoksa boş.</summary>
    public string Hint => Action.Hint ?? string.Empty;

    public bool HasHint => !string.IsNullOrEmpty(Action.Hint);
}
