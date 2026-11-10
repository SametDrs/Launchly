using System.Diagnostics;

namespace Launchly.Plugins.WebSearch;

/// <summary>Adresi varsayılan tarayıcıda açar; testlerde sahtesi kullanılır.</summary>
public interface IUrlOpener
{
    void Open(string url);
}

/// <summary>Windows kabuğu ile (varsayılan tarayıcı) açar.</summary>
public sealed class ShellUrlOpener : IUrlOpener
{
    public void Open(string url)
    {
        if (!SearchUrl.IsSafe(url))
        {
            throw new InvalidOperationException("Yalnızca http/https adresleri açılabilir.");
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
