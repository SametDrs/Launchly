using Launchly.Plugins.Calculator;
using Launchly.Plugins.Programs;

namespace Launchly.Tests;

/// <summary>Gerçek kabuk çağrısı yapmayan <see cref="IProgramLauncher"/>; çağrıları kaydeder.</summary>
internal sealed class FakeProgramLauncher(Action<string>? launch = null) : IProgramLauncher
{
    public List<string> Launched { get; } = [];

    public List<string> AdminLaunched { get; } = [];

    public List<string> Revealed { get; } = [];

    /// <summary>Doluysa <see cref="LaunchAsAdmin"/> bunu fırlatır (ör. UAC iptali: Win32Exception 1223).</summary>
    public Exception? AdminError { get; set; }

    public void Launch(string launchPath)
    {
        launch?.Invoke(launchPath);
        Launched.Add(launchPath);
    }

    public void LaunchAsAdmin(string path)
    {
        if (AdminError is not null)
        {
            throw AdminError;
        }

        AdminLaunched.Add(path);
    }

    public void Reveal(string path) => Revealed.Add(path);
}

/// <summary>Panoya yazılanları kaydeder.</summary>
internal sealed class RecordingClipboard : IClipboardService
{
    public List<string> Texts { get; } = [];

    public void SetText(string text) => Texts.Add(text);
}
