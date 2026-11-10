using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace Launchly.App;

/// <summary>
/// Tek örnek: ilk süreç adlı mutex'i alır ve adlı olayı dinler; sonraki süreç olayı tetikleyip kapanır
/// (ilk süreç pencereyi açar).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly string _pidName;
    private MemoryMappedFile? _pidFile;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _listener;

    /// <summary>Bu süreç ilk örnek mi.</summary>
    public bool IsFirst { get; }

    public SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.Mutex", out var created);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");
        IsFirst = created;
        _pidName = $@"Local\{name}.Pid";

        if (created)
        {
            // İlk örnek PID'ini paylaşır; ikinci örnek yalnızca ona ön plan hakkı verir.
            try
            {
                _pidFile = MemoryMappedFile.CreateOrOpen(_pidName, sizeof(int));   // eski/artık bir eşleme varsa bile açılır, PID yine yazılır
                using var view = _pidFile.CreateViewAccessor();
                view.Write(0, Environment.ProcessId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _pidFile?.Dispose();
                _pidFile = null;
            }
        }
    }

    private int? ReadFirstPid()
    {
        try
        {
            using var file = MemoryMappedFile.OpenExisting(_pidName, MemoryMappedFileRights.Read);
            using var view = file.CreateViewAccessor(0, sizeof(int), MemoryMappedFileAccess.Read);
            var pid = view.ReadInt32(0);
            return pid > 0 ? pid : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>İkinci bir örnek başlatılınca çağrılır (arka plan iş parçacığında).</summary>
    public void Listen(Action onSecondLaunch)
    {
        _listener = new Thread(() =>
        {
            var handles = new[] { _signal, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                onSecondLaunch();
            }
        })
        {
            IsBackground = true,
            Name = "Launchly.SingleInstance",
        };
        _listener.Start();
    }

    /// <summary>Çalışan ilk örneği uyarır.</summary>
    public void SignalFirst()
    {
        // Ön plan hakkı bu (kullanıcının az önce başlattığı) süreçte; ilk örnek pencereyi gerçekten öne alabilsin diye devret.
        // PID alınamazsa çağrılmaz (herkese hak verilmez).
        if (ReadFirstPid() is { } pid)
        {
            AllowSetForegroundWindow(pid);
        }

        _signal.Set();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Join(1000);
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }

        _pidFile?.Dispose();
        _mutex.Dispose();
        _signal.Dispose();
        _stop.Dispose();
    }
}
