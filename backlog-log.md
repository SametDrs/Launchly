# Backlog Log

Agent notları ve biten işlerin kaydı. Yeni kayıt en üste eklenir.

- 2026-11-11 | 02-pencere bitti | MainWindow (borderless, topmost, üst-orta, Mica/Acrylic/None, alt çubuk), Themes/Dark.xaml, MainViewModel (CommunityToolkit.Mvvm, sahte 8 sonuç), HotkeyManager (RegisterHotKey+HwndSource), TrayIcon (Exit), Settings.Backdrop + Core/HotkeyParser; build 0/0, 16 test geçti, Alt+Space/Esc/odak kaybı/ok tuşları/çakışma hatası elle doğrulandı.
- 2026-11-11 | karar: pencere | (1) Uygulama gizli başlar, Alt+Space açar; kısayol kaydedilemezse pencere açılır + MessageBox hata gösterir. (2) ShutdownMode=OnExplicitShutdown (tray Exit kapatır). (3) HotkeyParser Core'da (saf mantık, test edilebilir); RegisterHotKey App'te. (4) Mica: Win11 22H2+ (build 22621) `DWMWA_SYSTEMBACKDROP_TYPE`, altı düz koyu; AllowsTransparency kullanılmadı (köşeler DWM ile yuvarlanır, Win10'da köşe kare). (5) Tray için WinForms NotifyIcon: csproj'da UseWindowsForms + `Using Remove` System.Windows.Forms/System.Drawing (WPF ad çakışması). (6) Tray sol tık pencereyi açar (toggle değil: tık odak kaybıyla gizlerdi). (7) Settings/Actions düğmeleri şimdilik boş. Backdrop ayarı yalnızca settings.json'dan okunur.
- 2026-11-11 | 01-iskelet bitti | Launchly.sln + 5 proje, Directory.Build.props, Generic Host DI (App.xaml.cs), Settings + JsonStorage<T>, Serilog, .gitignore; build 0 uyarı/0 hata, 5/5 test geçti, uygulama açılıp kapanıyor ve settings.json oluşuyor.
- 2026-11-11 | karar: log | Serilog seçildi (Serilog.Extensions.Hosting 9.0.0 + Serilog.Sinks.File 6.0.0). Neden: MS.Extensions.Hosting ile tek satır entegrasyon (`UseSerilog`), yapısal log, NLog'daki XML config ihtiyacı yok. Dosya: `%AppData%\Launchly\Logs\launchly-YYYYMMDD.log`, günlük döner, 7 dosya tutulur. Core yalnızca `Microsoft.Extensions.Logging.Abstractions`'a bağlı (Serilog'a değil).
- 2026-11-11 | karar: JsonStorage | Kayıt `.tmp`'ye yazılıp `File.Replace` ile yerine konur, önceki sürüm `.bak` olur. Bozuk asıl dosya `.corrupt` olarak kenara alınır (yoksa sonraki kayıt bozuk içeriği `.bak`'a taşırdı), sonra `.bak`'tan yüklenip asıl dosya onarılır. İkisi de bozuksa varsayılan. `UnsafeRelaxedJsonEscaping`: `+` ve Türkçe karakter kaçışsız yazılır.
- 2026-11-11 | not: iskelet | `TreatWarningsAsErrors=true` Directory.Build.props'ta (uyarısız build şartı kalıcı). WPF projesinde ImplicitUsings `System.IO`'yu içermez; `using System.IO;` gerekir. App `AssemblyName=Launchly` (exe: `Launchly.exe`). Geçici boş `MainWindow` var; 02-pencere değiştirecek. `OnExit` senkron (async void'de süreç beklemeden kapanıyor). Tests şimdilik `net9.0` ve yalnızca Core'a referanslı; App/Plugins test edilecekse TFM `net9.0-windows10.0.19041.0` olmalı.


<!-- Biçim:
- YYYY-MM-DD | başlık | ne yapıldı / neden yarıda kaldı / ne karar verildi
-->




