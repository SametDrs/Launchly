using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Launchly.App.Hotkey;
using Launchly.App.Interop;
using Launchly.App.Tray;
using Launchly.App.ViewModels;
using Launchly.Core;
using Launchly.Core.Plugins;
using Launchly.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Launchly.App;

public partial class App : Application
{
    private IHost? _host;
    private HotkeyManager? _hotkey;
    private TrayIcon? _tray;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host henüz başlatılmadı.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(DataLocation.LogsDirectory, "launchly-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog()
            .ConfigureServices(ConfigureServices)
            .Build();

        await _host.StartAsync();

        // Ayarlar ilk çözümlemede yüklenir; dosya yoksa oluşturulur.
        var settings = Services.GetRequiredService<Settings>();
        Log.Information("Launchly başladı. Ayar dosyası: {Path}, kısayol: {Hotkey}", DataLocation.SettingsPath, settings.Hotkey);

        // Eklentiler pencere açılmadan başlatılır (şimdilik kayıtlı eklenti yok; 04-06 ekler).
        await Services.GetRequiredService<PluginManager>().InitializeAsync();

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;

        // Tutamak oluşturulur ama pencere gösterilmez: Launchly gizli başlar, kısayolla açılır.
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var applied = WindowBackdrop.Apply(window, settings.Backdrop);
        Log.Information("Arka plan: istenen {Requested}, uygulanan {Applied}", settings.Backdrop, applied);

        _tray = new TrayIcon(window.ShowLauncher, Shutdown);

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += window.ToggleLauncher;
        if (!_hotkey.TryRegister(hwnd, settings.Hotkey, out var error))
        {
            Log.Warning("Kısayol kaydedilemedi: {Error}", error);
            // Kısayol yoksa kullanıcı yine de uygulamaya ulaşabilsin: pencereyi aç ve nedenini göster.
            window.ShowLauncher();
            System.Windows.MessageBox.Show(error, "Launchly", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();

        if (_host is not null)
        {
            Services.GetRequiredService<JsonStorage<Settings>>().Save(Services.GetRequiredService<Settings>());
            // OnExit senkron olmalı (async void'de süreç await'i beklemeden kapanabilir).
            // Task.Run, UI senkronizasyon bağlamında kilitlenmeyi önler.
            Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.Information("Launchly kapandı.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Konsol olmayan WPF uygulamasında "Press Ctrl+C" mesajları gereksiz.
        services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
        services.AddSingleton(sp => new JsonStorage<Settings>(
            DataLocation.SettingsPath,
            sp.GetRequiredService<ILogger<JsonStorage<Settings>>>()));
        services.AddSingleton(sp => sp.GetRequiredService<JsonStorage<Settings>>().Load());
        // Dahili eklentiler (IPlugin) 04-06 görevlerinde buraya AddSingleton<IPlugin, ...>() ile eklenir.
        services.AddSingleton<PluginManager>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Yakalanmamış hata");
    }
}
