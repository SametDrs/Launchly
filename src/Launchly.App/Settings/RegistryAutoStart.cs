using Launchly.Core;
using Microsoft.Win32;

namespace Launchly.App.SettingsUi;

/// <summary><c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> altına Launchly girdisini yazar/siler.</summary>
public sealed class RegistryAutoStart : IAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = DataLocation.AppName;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Çalıştırılabilir dosya yolu bulunamadı.");
            key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
