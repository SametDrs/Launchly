using System.ComponentModel;
using System.Diagnostics;

namespace Launchly.Plugins.Programs;

/// <summary>Programs eklentisinin sistem çağrıları (başlat, yönetici olarak başlat, Gezgin'de göster); testlerde sahtesi kullanılır.</summary>
public interface IProgramLauncher
{
    /// <summary>.lnk yolu, <c>shell:AppsFolder\&lt;AUMID&gt;</c> ya da komut; kabuk (ShellExecute) açar.</summary>
    void Launch(string launchPath);

    /// <summary>
    /// Yönetici olarak başlatır (<c>runas</c>). UAC iptalinde <see cref="Win32Exception"/>
    /// (<see cref="Win32Exception.NativeErrorCode"/> = <see cref="ShellProgramLauncher.ErrorCancelled"/>) fırlatır.
    /// </summary>
    void LaunchAsAdmin(string path);

    /// <summary>Gezgin'i açar ve dosyayı seçili gösterir.</summary>
    void Reveal(string path);
}

/// <summary>Gerçek kabuk çağrıları.</summary>
public sealed class ShellProgramLauncher : IProgramLauncher
{
    /// <summary>ERROR_CANCELLED: kullanıcı UAC istemini reddetti.</summary>
    public const int ErrorCancelled = 1223;

    public void Launch(string launchPath) =>
        Process.Start(new ProcessStartInfo(launchPath) { UseShellExecute = true })?.Dispose();

    public void LaunchAsAdmin(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" })?.Dispose();

    public void Reveal(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
}
