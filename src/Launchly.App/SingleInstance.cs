using System.Threading;

namespace Launchly.App;

/// <summary>
/// Tek örnek: ilk süreç adlı mutex'i alır ve adlı olayı dinler; sonraki süreç olayı tetikleyip kapanır
/// (ilk süreç pencereyi açar).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _listener;

    /// <summary>Bu süreç ilk örnek mi.</summary>
    public bool IsFirst { get; }

    public SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.Mutex", out var created);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");
        IsFirst = created;
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
    public void SignalFirst() => _signal.Set();

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Join(1000);
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }

        _mutex.Dispose();
        _signal.Dispose();
        _stop.Dispose();
    }
}
