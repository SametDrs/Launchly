using System.Runtime.InteropServices;
using System.Text;

namespace Launchly.Plugins.Programs;

/// <summary>Kısayolun kabukta görünen (Windows dilindeki) adı.</summary>
internal static class ShellDisplayName
{
    private const uint ShgfiDisplayName = 0x200;

    /// <summary>
    /// <c>SHGetFileInfo(SHGFI_DISPLAYNAME)</c>: <c>desktop.ini</c> <c>[LocalizedFileNames]</c> çevirisini uygular
    /// ("Command Prompt.lnk" -> "Komut İstemi"). Alınamazsa boş metin. STA ve MTA'da çalışır (makinede doğrulandı).
    /// </summary>
    public static string Get(string path)
    {
        var info = new ShellNative.ShFileInfo();
        var ok = ShellNative.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellNative.ShFileInfo>(), ShgfiDisplayName);
        return ok == IntPtr.Zero ? string.Empty : info.szDisplayName ?? string.Empty;
    }
}

/// <summary>Varsayılan tarayıcının exe yolu (http protokolünün ilişkisi).</summary>
internal static class DefaultBrowser
{
    private const uint AssocfIsProtocol = 0x1000;
    private const uint AssocfNoTruncate = 0x20;
    private const int AssocstrExecutable = 2;

    /// <summary>Exe yolu; bulunamazsa null.</summary>
    public static string? GetExecutablePath()
    {
        try
        {
            uint size = 1024;
            var sb = new StringBuilder((int)size);
            var hr = ShellNative.AssocQueryString(AssocfIsProtocol | AssocfNoTruncate, AssocstrExecutable, "http", "open", sb, ref size);
            var path = hr == 0 ? sb.ToString().Trim() : string.Empty;
            return path.Length > 0 ? path : null;
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Tek iş parçacıklı daire (STA) gerektiren kabuk çağrıları için ayrılmış iş parçacığı.</summary>
internal static class StaRunner
{
    /// <summary><paramref name="func"/>'ı yeni bir STA iş parçacığında çalıştırır; sonuç/istisna Task ile döner.</summary>
    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Launchly.ShellSta",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <summary>Kabuk COM nesnesinin yanlış dairede çağrıldığını gösteren hata mı (E_NOINTERFACE, RPC_E_WRONG_THREAD).</summary>
    public static bool IsApartmentError(Exception ex) =>
        ex is InvalidCastException
        || (ex is COMException com && (com.HResult == unchecked((int)0x80004002) || com.HResult == unchecked((int)0x8001010E)));
}

/// <summary>Kabuk P/Invoke ve COM tanımları.</summary>
internal static class ShellNative
{
    public const uint SigdnNormalDisplay = 0x00000000;
    public const uint SigdnParentRelativeParsing = 0x80018001;

    public static readonly Guid FolderIdAppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    public static readonly Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IidEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int AssocQueryString(uint flags, int str, string pszAssoc, string? pszExtra, StringBuilder pszOut, ref uint pcchOut);

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderItem(
        [In] ref Guid rfid, uint flags, IntPtr hToken, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out DibSection lpvObject);

    /// <summary>COM nesnesini hemen bırakır (tarama yüzlerce öğe üretir; sonlandırıcıyı beklemeyiz).</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    /// <summary><see cref="IShellItem.GetDisplayName"/> sonucu; alınamazsa boş metin.</summary>
    public static string DisplayName(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out var ptr) != 0 || ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DibSection
    {
        public BitmapInfo dsBm;
        public BitmapInfoHeader dsBmih;
        public uint dsBitfields0;
        public uint dsBitfields1;
        public uint dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    public interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);

        [PreserveSig]
        int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem? ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    public interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem? rgelt, out uint pceltFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumShellItems? ppenum);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }
}
