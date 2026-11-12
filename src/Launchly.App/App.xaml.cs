using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Launchly.App.Hotkey;
using Launchly.App.Interop;
using Launchly.App.SettingsUi;
using Launchly.App.Tray;
using Launchly.App.ViewModels;
using Launchly.Core;
using Launchly.Core.Localization;
using Launchly.Core.Plugins;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Launchly.Plugins.Calculator;
using Launchly.Plugins.Files;
using Launchly.Plugins.Programs;
using Launchly.Plugins.SystemCommands;
using Launchly.Plugins.Url;
using Launchly.Plugins.WebSearch;
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
    private SingleInstance? _singleInstance;
    private SettingsWindow? _settingsWindow;
    private BackdropController? _backdrop;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host henüz başlatılmadı.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tek örnek: ikinci başlatma çalışan pencereyi açar ve kapanır.
        _singleInstance = new SingleInstance("Launchly");
        if (!_singleInstance.IsFirst)
        {
            _singleInstance.SignalFirst();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

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

        // Dil: kurulumun yazdığı işaret yalnızca dil ayarı Auto iken uygulanır (bir kez, ardından silinir); sonra ayardan çözülür.
        if (InstallerLanguage.Consume(settings, InstallerLanguage.MarkerPath, Services.GetRequiredService<ILogger<Settings>>()))
        {
            Services.GetRequiredService<JsonStorage<Settings>>().Save(settings);
        }

        Loc.SetLanguage(settings.Language);
        ApplyUiCulture();
        Loc.LanguageChanged += ApplyUiCulture;
        Log.Information("Arayüz dili: {Language} (ayar: {Setting})", Loc.Language, settings.Language);

        // Eklentiler pencere açılmadan başlatılır (şimdilik kayıtlı eklenti yok; 04-06 ekler).
        await Services.GetRequiredService<PluginManager>().InitializeAsync();

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;

        // Tutamak oluşturulur ama pencere gösterilmez: Launchly gizli başlar, kısayolla açılır.
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _backdrop = new BackdropController(
            settings,
            Services.GetRequiredService<ILogger<BackdropController>>(),
            Dispatcher);
        _backdrop.Attach(window);   // istenen/uygulanan tür ve düşüş nedeni controller'da loglanır
        var backdrop = _backdrop;

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += window.ToggleLauncher;

        var settingsService = new SettingsService(
            settings,
            Services.GetRequiredService<JsonStorage<Settings>>(),
            new WindowHotkeyRegistrar(_hotkey, hwnd),
            new RegistryAutoStart(),
            Services.GetRequiredService<PluginManager>(),
            Services.GetRequiredService<ILogger<SettingsService>>());
        // Uygula: tema açık tüm pencerelere (ana pencere + ayar penceresi) hemen uygulanır.
        settingsService.Applied += _ => backdrop.ApplyAll();

        var pluginManager = Services.GetRequiredService<PluginManager>();

        void OpenSettings()
        {
            if (_settingsWindow is null)
            {
                var viewModel = new SettingsViewModel(
                    settingsService,
                    Services.GetRequiredService<ProgramUsageStore>(),
                    () => pluginManager.Plugins,
                    WindowBackdrop.Availability,
                    Services.GetRequiredService<IUrlOpener>());
                backdrop.AvailabilityChanged += viewModel.RefreshBackdropAvailability;
                _settingsWindow = new SettingsWindow(viewModel, backdrop);
            }

            _settingsWindow.ShowSettings();
        }

        window.SettingsRequested += OpenSettings;
        _tray = new TrayIcon(window.ShowLauncher, OpenSettings, Shutdown);
        _singleInstance.Listen(() => Dispatcher.BeginInvoke(window.ShowLauncher));

        if (settings.StartOnSystemStartup)
        {
            // Exe yolu değişmiş olabilir: kaydı güncel yolla yenile.
            try
            {
                new RegistryAutoStart().SetEnabled(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Otomatik başlatma kaydı yenilenemedi");
            }
        }

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
        _settingsWindow?.CloseForExit();
        _backdrop?.Dispose();
        _singleInstance?.Dispose();

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
        services.AddSingleton(sp => sp.GetRequiredService<JsonStorage<Settings>>().Load()
            .Normalize(sp.GetRequiredService<ILogger<Settings>>()));
        // Dahili eklentiler (IPlugin); 05-06 görevleri buraya AddSingleton<IPlugin, ...>() ile ekler.
        // Kullanım kaydı tekil: eklentiden önce oluşur, DI kapanışta eklentiden sonra Dispose eder (son yazım).
        services.AddSingleton<ProgramUsageStore>();
        services.AddSingleton<IPlugin, ProgramsPlugin>();
        services.AddSingleton<IUrlOpener, ShellUrlOpener>();
        services.AddSingleton<IPlugin, WebSearchPlugin>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<IPlugin, CalculatorPlugin>();
        services.AddSingleton<IPlugin, UrlPlugin>();
        services.AddSingleton<ISystemCommands, WindowsSystemCommands>();
        services.AddSingleton<IConfirmation, MessageBoxConfirmation>();
        services.AddSingleton<IPlugin, SystemPlugin>();
        services.AddSingleton<IFileOpener, ShellFileOpener>();
        services.AddSingleton<IPlugin, FilesPlugin>();
        services.AddSingleton<PluginManager>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    // Çerçeve kaynakları (ör. metin kutusu bağlam menüsü) da seçilen dilde gelsin; "Auto" çözümlemesi bundan etkilenmez.
    private static void ApplyUiCulture()
    {
        CultureInfo.DefaultThreadCurrentUICulture = Loc.Culture;
        CultureInfo.CurrentUICulture = Loc.Culture;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Yakalanmamış hata");
    }
}
