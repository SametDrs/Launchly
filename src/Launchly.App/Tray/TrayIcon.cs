using System.Drawing;
using System.Windows.Forms;

namespace Launchly.App.Tray;

/// <summary>Bildirim alanı simgesi: sol tık pencereyi aç/kapatır, menüden çıkış.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action toggle, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Launchly",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                toggle();
            }
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
