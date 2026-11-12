using System.Windows.Forms;
using Launchly.Core.Localization;

namespace Launchly.App.Tray;

/// <summary>Tray bağlam menüsü (Ayarlar / Çıkış). Metinler dil çalışırken değişince yenilenir; NotifyIcon'dan ayrı olduğu için testlenebilir.</summary>
public sealed class TrayMenu : IDisposable
{
    private readonly ToolStripItem _settingsItem;
    private readonly ToolStripItem _exitItem;

    public TrayMenu(Action settings, Action exit)
    {
        Strip = new ContextMenuStrip();
        _settingsItem = Strip.Items.Add(string.Empty, null, (_, _) => settings());
        _exitItem = Strip.Items.Add(string.Empty, null, (_, _) => exit());
        UpdateTexts();
        Loc.LanguageChanged += OnLanguageChanged;
    }

    public ContextMenuStrip Strip { get; }

    /// <summary>Menü öğelerinin şu anki metinleri (sırayla: Ayarlar, Çıkış).</summary>
    public IReadOnlyList<string> Texts => [_settingsItem.Text ?? string.Empty, _exitItem.Text ?? string.Empty];

    private void OnLanguageChanged() => UpdateTexts();

    private void UpdateTexts()
    {
        _settingsItem.Text = Loc.T("tray.settings");
        _exitItem.Text = Loc.T("tray.exit");
    }

    public void Dispose()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        Strip.Dispose();
    }
}
