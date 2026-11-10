using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launchly.Plugins.Programs;

/// <summary>Shell ikonunu dondurulmuş <see cref="BitmapSource"/> olarak verir; yola göre bellekte önbelleklenir.</summary>
internal sealed class IconLoader
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>İkon; alınamazsa null. Dönen nesne dondurulmuştur (her iş parçacığından kullanılabilir).</summary>
    public ImageSource? Get(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return _cache.GetOrAdd(path, Load);
    }

    private static ImageSource? Load(string path)
    {
        var info = new ShFileInfo();
        try
        {
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon) == IntPtr.Zero
                || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or IOException)
        {
            return null;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero)
            {
                DestroyIcon(info.hIcon);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
