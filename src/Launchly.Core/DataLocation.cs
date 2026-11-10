namespace Launchly.Core;

/// <summary>Uygulamanın kullanıcı verisi yolları (%AppData%\Launchly).</summary>
public static class DataLocation
{
    public const string AppName = "Launchly";
    public const string SettingsFileName = "settings.json";

    public static string RoamingDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    public static string SettingsPath => Path.Combine(RoamingDirectory, SettingsFileName);

    public static string LogsDirectory => Path.Combine(RoamingDirectory, "Logs");
}
