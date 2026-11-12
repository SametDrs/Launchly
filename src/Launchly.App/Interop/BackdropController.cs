using System.Windows;
using System.Windows.Threading;
using Launchly.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Launchly.App.Interop;

/// <summary>
/// Açık pencerelere ayardaki temayı uygular ve Windows saydamlık ayarı çalışırken değişince yeniden uygular.
/// UI iş parçacığında kullanılır; <see cref="SystemEvents"/> olayları dispatcher'a taşınır.
/// </summary>
public sealed class BackdropController : IDisposable
{
    private readonly Settings _settings;
    private readonly ILogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Window, BackdropType> _windows = [];
    private bool _transparency;
    private bool _disposed;

    public BackdropController(Settings settings, ILogger logger, Dispatcher dispatcher)
    {
        _settings = settings;
        _logger = logger;
        _dispatcher = dispatcher;
        _transparency = WindowBackdrop.IsTransparencyEnabled();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Saydamlık ayarı değişti (ayar penceresindeki seçenekler yeniden değerlendirilsin).</summary>
    public event Action? AvailabilityChanged;

    /// <summary>Pencereyi izlemeye alır ve temayı hemen uygular.</summary>
    public BackdropType Attach(Window window)
    {
        _windows.TryAdd(window, (BackdropType)(-1));
        return Apply(window);
    }

    public void Detach(Window window) => _windows.Remove(window);

    /// <summary>Tüm izlenen pencerelere güncel ayarı uygular (Uygula sonrası).</summary>
    public void ApplyAll()
    {
        foreach (var window in _windows.Keys.ToList())
        {
            Apply(window);
        }
    }

    private BackdropType Apply(Window window)
    {
        var requested = _settings.Backdrop;
        var palette = BackdropPalette.For(requested, WindowBackdrop.IsSupported, _transparency);
        var applied = WindowBackdrop.Apply(window, palette);
        if (_windows.TryGetValue(window, out var previous) && previous != applied)
        {
            _windows[window] = applied;
            if (applied == BackdropType.None && requested != BackdropType.None)
            {
                var reason = !WindowBackdrop.IsSupported
                    ? WindowBackdrop.UnsupportedReason
                    : !_transparency ? WindowBackdrop.TransparencyOffReason : "DWM özniteliği reddedildi";
                _logger.LogInformation(
                    "Arka plan ({Window}): istenen {Requested}, uygulanan {Applied} ({Reason})",
                    window.GetType().Name, requested, applied, reason);
            }
            else
            {
                _logger.LogInformation(
                    "Arka plan ({Window}): istenen {Requested}, uygulanan {Applied}",
                    window.GetType().Name, requested, applied);
            }
        }

        return applied;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Olay SystemEvents'in kendi iş parçacığından gelebilir; pencereler UI iş parçacığında güncellenir.
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            var transparency = WindowBackdrop.IsTransparencyEnabled();
            if (transparency == _transparency)
            {
                return;
            }

            _transparency = transparency;
            _logger.LogInformation("Windows saydamlık efektleri {State}", transparency ? "açıldı" : "kapandı");
            ApplyAll();
            AvailabilityChanged?.Invoke();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _windows.Clear();
    }
}
