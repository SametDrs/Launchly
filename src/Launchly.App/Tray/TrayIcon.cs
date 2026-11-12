using System.Drawing;
using System.Windows.Forms;

namespace Launchly.App.Tray;

/// <summary>Bildirim alanı simgesi: sol tık pencereyi aç/kapatır, menüden çıkış.</summary>
public sealed class TrayIcon : IDisposable
{
    public const string IconUri = "pack://application:,,,/Assets/Launchly.ico";

    private readonly NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(Action toggle, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add("Çıkış", null, (_, _) => exit());

        _image = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _image,
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

    // Uygulama ikonunun tray boyutuna (16 px, DPI'ye göre 20/24) en yakın girişi.
    private static Icon LoadIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri(IconUri));
        if (info is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var stream = info.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }
}
