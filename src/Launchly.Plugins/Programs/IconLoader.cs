using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launchly.Plugins.Programs;

/// <summary>Shell ikonunu dondurulmuş <see cref="BitmapSource"/> olarak verir; anahtara göre bellekte önbelleklenir.</summary>
internal sealed class IconLoader
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const int IconSize = 32;
    private const int SiigbfIconOnly = 0x4;
    private const string ParsingKeyPrefix = "parse:";

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dosya yolunun ikonu; alınamazsa null. Dönen nesne dondurulmuştur (her iş parçacığından kullanılabilir).</summary>
    public ImageSource? Get(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return _cache.GetOrAdd(path, Load);
    }

    /// <summary>
    /// AppsFolder öğesinin ikonu (<c>shell:AppsFolder\&lt;AUMID&gt;</c>, <c>IShellItemImageFactory</c>, 32x32);
    /// alınamazsa null (harf karosu gösterilir). Aynı önbellekte ön ekli anahtarla tutulur.
    /// </summary>
    public ImageSource? GetForParsingName(string parsingName)
    {
        if (string.IsNullOrEmpty(parsingName))
        {
            return null;
        }

        return _cache.GetOrAdd(ParsingKeyPrefix + parsingName, _ => LoadForParsingName(parsingName));
    }

    private static ImageSource? Load(string path)
    {
        var info = new ShellNative.ShFileInfo();
        try
        {
            if (ShellNative.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellNative.ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon) == IntPtr.Zero
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

    internal static ImageSource? LoadForParsingName(string parsingName)
    {
        try
        {
            return LoadForParsingNameCore(parsingName);
        }
        catch (Exception ex) when (StaRunner.IsApartmentError(ex) && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            try
            {
                return StaRunner.Run(() => LoadForParsingNameCore(parsingName)).GetAwaiter().GetResult();
            }
            catch (Exception inner) when (IsExpected(inner))
            {
                return null;
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is COMException or InvalidCastException or ArgumentException or InvalidOperationException;

    private static ImageSource? LoadForParsingNameCore(string parsingName)
    {
        object? factoryObj = null;
        var hbitmap = IntPtr.Zero;
        try
        {
            var iid = ShellNative.IidShellItemImageFactory;
            if (ShellNative.SHCreateItemFromParsingName(@"shell:AppsFolder\" + parsingName, IntPtr.Zero, ref iid, out factoryObj) != 0
                || factoryObj is null)
            {
                return null;
            }

            var factory = (ShellNative.IShellItemImageFactory)factoryObj;
            var size = new ShellNative.NativeSize { Width = IconSize, Height = IconSize };
            if (factory.GetImage(size, SiigbfIconOnly, out hbitmap) != 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            var source = FromHBitmap(hbitmap);
            source?.Freeze();
            return source;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                ShellNative.DeleteObject(hbitmap);
            }

            ShellNative.Release(factoryObj);
        }
    }

    // IShellItemImageFactory 32 bit, düz (ön çarpılmamış) alfalı bir DIB section döner; bu makinede ölçüldü: yarı saydam
    // piksellerde renk > alfa (ör. r255 a2), bu yüzden Pbgra32 değil Bgra32. Imaging.CreateBitmapSourceFromHBitmap alfayı
    // atar (siyah zemin); bu yüzden pikseller doğrudan okunur. DIB değilse WPF dönüşümüne düşülür.
    private static BitmapSource? FromHBitmap(IntPtr hbitmap)
    {
        var size = Marshal.SizeOf<ShellNative.DibSection>();
        if (ShellNative.GetObject(hbitmap, size, out var dib) == size
            && dib.dsBm.bmBitsPixel == 32
            && dib.dsBm.bmBits != IntPtr.Zero
            && dib.dsBm.bmWidth > 0
            && dib.dsBm.bmHeight > 0)
        {
            var width = dib.dsBm.bmWidth;
            var height = dib.dsBm.bmHeight;
            var stride = dib.dsBm.bmWidthBytes;
            var pixels = new byte[stride * height];
            Marshal.Copy(dib.dsBm.bmBits, pixels, 0, pixels.Length);

            if (dib.dsBmih.biHeight > 0)
            {
                // Alttan üste DIB: satırları çevir.
                var row = new byte[stride];
                for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
                {
                    Buffer.BlockCopy(pixels, top * stride, row, 0, stride);
                    Buffer.BlockCopy(pixels, bottom * stride, pixels, top * stride, stride);
                    Buffer.BlockCopy(row, 0, pixels, bottom * stride, stride);
                }
            }

            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        }

        return Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
