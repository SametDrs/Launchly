using System.Runtime.InteropServices;

namespace Launchly.Plugins.Programs;

/// <summary>
/// <c>shell:AppsFolder</c> öğelerinin kaynağı (gerçek: <see cref="AppsFolderScanner"/>; testler sahtesini verir).
/// </summary>
internal interface IAppsFolderSource
{
    /// <summary>
    /// Tüm öğeler: <c>ParsingName</c> (paketli uygulamada AUMID, ör. <c>Claude_pzs8sxrjxfjjc!Claude</c>; diğerlerinde
    /// kabuk ayrıştırma adı) ve Windows dilindeki görünen ad. İptal edilirse <see cref="OperationCanceledException"/>.
    /// </summary>
    IReadOnlyList<(string ParsingName, string DisplayName)> Enumerate(CancellationToken cancellationToken);
}

/// <summary>Kaynak yok (testlerin varsayılanı).</summary>
internal sealed class EmptyAppsFolderSource : IAppsFolderSource
{
    public static readonly EmptyAppsFolderSource Instance = new();

    public IReadOnlyList<(string ParsingName, string DisplayName)> Enumerate(CancellationToken cancellationToken) => [];
}

/// <summary>
/// <c>SHGetKnownFolderItem(FOLDERID_AppsFolder)</c> + <c>BHID_EnumItems</c> ile AppsFolder'ı gezer.
/// Önce çağıran iş parçacığında (eklenti taraması: iş parçacığı havuzu, MTA) dener; kabuk daire hatası verirse
/// (<c>E_NOINTERFACE</c>, <c>RPC_E_WRONG_THREAD</c>) ayrılmış bir STA iş parçacığında yeniden dener.
/// </summary>
internal sealed class AppsFolderScanner : IAppsFolderSource
{
    public IReadOnlyList<(string ParsingName, string DisplayName)> Enumerate(CancellationToken cancellationToken)
    {
        try
        {
            return EnumerateCore(cancellationToken);
        }
        catch (Exception ex) when (StaRunner.IsApartmentError(ex) && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return StaRunner.Run(() => EnumerateCore(cancellationToken)).GetAwaiter().GetResult();
        }
    }

    internal static List<(string ParsingName, string DisplayName)> EnumerateCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = new List<(string, string)>();
        object? folderObj = null;
        object? enumObj = null;
        try
        {
            var folderId = ShellNative.FolderIdAppsFolder;
            var iidItem = ShellNative.IidShellItem;
            Marshal.ThrowExceptionForHR(ShellNative.SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref iidItem, out folderObj));
            var folder = (ShellNative.IShellItem)folderObj!;

            var bhid = ShellNative.BhidEnumItems;
            var iidEnum = ShellNative.IidEnumShellItems;
            Marshal.ThrowExceptionForHR(folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out enumObj));
            var items = (ShellNative.IEnumShellItems)enumObj!;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hr = items.Next(1, out var item, out var fetched);
                if (hr != 0 || fetched == 0 || item is null)
                {
                    ShellNative.Release(item);
                    if (hr < 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }

                    break;   // S_FALSE: son
                }

                try
                {
                    var parsing = ShellNative.DisplayName(item, ShellNative.SigdnParentRelativeParsing);
                    var display = ShellNative.DisplayName(item, ShellNative.SigdnNormalDisplay);
                    if (parsing.Length > 0 && display.Length > 0)
                    {
                        list.Add((parsing, display));
                    }
                }
                finally
                {
                    ShellNative.Release(item);
                }
            }
        }
        finally
        {
            ShellNative.Release(enumObj);
            ShellNative.Release(folderObj);
        }

        return list;
    }
}
