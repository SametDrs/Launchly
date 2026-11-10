using System.Runtime.InteropServices;
using System.Text;

namespace Launchly.Plugins.Programs;

/// <summary>.lnk dosyasının hedef yolunu IShellLink ile çözer.</summary>
internal static class ShortcutResolver
{
    /// <summary>Hedef yol; çözülemezse ya da hedef yoksa boş metin.</summary>
    public static string ResolveTarget(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var sb = new StringBuilder(520);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return Environment.ExpandEnvironmentVariables(sb.ToString());
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            return string.Empty;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    }

    // GetPath sözleşmedeki ilk yöntemdir; diğerlerine gerek yok.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    }
}
