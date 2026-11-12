using System.Diagnostics;
using System.Runtime.InteropServices;
using Launchly.Core.Querying;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.SystemCommands;

/// <summary>Sistem komutları; testlerde sahtesi kullanılır (gerçeği kilitler/kapatır).</summary>
public interface ISystemCommands
{
    void Lock();

    void Sleep();

    void Shutdown();

    void Restart();

    void EmptyRecycleBin();
}

/// <summary>Kullanıcıdan evet/hayır onayı alır; testlerde sahtesi kullanılır.</summary>
public interface IConfirmation
{
    bool Confirm(string title, string message);
}

public sealed class WindowsSystemCommands : ISystemCommands
{
    public void Lock()
    {
        if (!NativeMethods.LockWorkStation())
        {
            throw new InvalidOperationException("Oturum kilitlenemedi.");
        }
    }

    public void Sleep()
    {
        if (!NativeMethods.SetSuspendState(false, false, false))
        {
            throw new InvalidOperationException("Uyku moduna geçilemedi (Win32 hata " + Marshal.GetLastWin32Error() + ").");
        }
    }

    public void Shutdown() => RunShutdown("/s /t 0");

    public void Restart() => RunShutdown("/r /t 0");

    // NOCONFIRMATION | NOPROGRESSUI | NOSOUND. S_OK, S_FALSE ve E_UNEXPECTED (çöp kutusu zaten boş) başarı sayılır.
    public void EmptyRecycleBin()
    {
        var hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);
        if (hr is not (0 or 1 or unchecked((int)0x8000FFFF)))
        {
            throw new InvalidOperationException("Geri dönüşüm kutusu boşaltılamadı (HRESULT 0x" + hr.ToString("X8") + ").");
        }
    }

    private static void RunShutdown(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false })?.Dispose();

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LockWorkStation();

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
    }
}

/// <summary>Windows MessageBox ile onay (varsayılan düğme: Hayır).</summary>
public sealed class MessageBoxConfirmation : IConfirmation
{
    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
}

/// <summary>lock, sleep, shutdown, restart, empty recycle bin. Anahtar kelime yok; ada göre eşleşir.</summary>
public sealed class SystemPlugin : IPlugin
{
    public const string PluginId = "launchly.system";

    private sealed record Command(string Title, string SubTitle, string Icon, string? ConfirmMessage, Action<ISystemCommands> Run);

    private static readonly Command[] Commands =
    [
        new("Lock", "Lock the computer", "", null, c => c.Lock()),
        new("Sleep", "Put the computer to sleep", "", null, c => c.Sleep()),
        new("Shutdown", "Shut down the computer", "", "Bilgisayar kapatılsın mı?", c => c.Shutdown()),
        new("Restart", "Restart the computer", "", "Bilgisayar yeniden başlatılsın mı?", c => c.Restart()),
        new("Empty Recycle Bin", "Permanently delete items in the Recycle Bin", "", "Geri dönüşüm kutusu kalıcı olarak boşaltılsın mı?", c => c.EmptyRecycleBin()),
    ];

    private readonly ISystemCommands _system;
    private readonly IConfirmation _confirmation;
    private readonly ILogger _logger;

    public SystemPlugin(ISystemCommands system, IConfirmation confirmation, ILogger<SystemPlugin> logger)
        : this(system, confirmation, (ILogger)logger)
    {
    }

    internal SystemPlugin(ISystemCommands system, IConfirmation confirmation, ILogger? logger)
    {
        _system = system;
        _confirmation = confirmation;
        _logger = logger ?? NullLogger.Instance;
    }

    public PluginMetadata Metadata { get; } = new() { Id = PluginId, Name = "System", Description = "Kilitle, uyku, kapat, yeniden başlat ve geri dönüşüm kutusunu boşalt gibi sistem komutlarını çalıştırır." };

    public Task InitAsync(PluginContext context) => Task.CompletedTask;

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        var search = query.Raw.Trim();
        if (search.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var results = new List<Result>();
        foreach (var command in Commands)
        {
            var match = StringMatcher.Match(search, command.Title);
            if (!match.IsMatch)
            {
                continue;
            }

            var c = command;
            results.Add(new Result
            {
                Title = c.Title,
                SubTitle = c.SubTitle,
                Icon = c.Icon,
                Score = match.Score,
                TypeLabel = "System",
                AutoComplete = c.Title.ToLowerInvariant(),
                Action = () => Task.FromResult(Run(c)),
            });
        }

        return Task.FromResult<IReadOnlyList<Result>>(results);
    }

    private bool Run(Command command)
    {
        try
        {
            if (command.ConfirmMessage is not null && !_confirmation.Confirm(command.Title, command.ConfirmMessage))
            {
                return false; // vazgeçildi; pencere açık kalır
            }

            command.Run(_system);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sistem komutu başarısız: {Command}", command.Title);
            return false;
        }
    }
}
