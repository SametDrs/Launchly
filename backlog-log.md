# Backlog Log

Agent notları ve biten işlerin kaydı. Yeni kayıt en üste eklenir.

- 2026-11-11 | 01-iskelet bitti | Launchly.sln + 5 proje, Directory.Build.props, Generic Host DI (App.xaml.cs), Settings + JsonStorage<T>, Serilog, .gitignore; build 0 uyarı/0 hata, 5/5 test geçti, uygulama açılıp kapanıyor ve settings.json oluşuyor.
- 2026-11-11 | karar: log | Serilog seçildi (Serilog.Extensions.Hosting 9.0.0 + Serilog.Sinks.File 6.0.0). Neden: MS.Extensions.Hosting ile tek satır entegrasyon (`UseSerilog`), yapısal log, NLog'daki XML config ihtiyacı yok. Dosya: `%AppData%\Launchly\Logs\launchly-YYYYMMDD.log`, günlük döner, 7 dosya tutulur. Core yalnızca `Microsoft.Extensions.Logging.Abstractions`'a bağlı (Serilog'a değil).
- 2026-11-11 | karar: JsonStorage | Kayıt `.tmp`'ye yazılıp `File.Replace` ile yerine konur, önceki sürüm `.bak` olur. Bozuk asıl dosya `.corrupt` olarak kenara alınır (yoksa sonraki kayıt bozuk içeriği `.bak`'a taşırdı), sonra `.bak`'tan yüklenip asıl dosya onarılır. İkisi de bozuksa varsayılan. `UnsafeRelaxedJsonEscaping`: `+` ve Türkçe karakter kaçışsız yazılır.
- 2026-11-11 | not: iskelet | `TreatWarningsAsErrors=true` Directory.Build.props'ta (uyarısız build şartı kalıcı). WPF projesinde ImplicitUsings `System.IO`'yu içermez; `using System.IO;` gerekir. App `AssemblyName=Launchly` (exe: `Launchly.exe`). Geçici boş `MainWindow` var; 02-pencere değiştirecek. `OnExit` senkron (async void'de süreç beklemeden kapanıyor). Tests şimdilik `net9.0` ve yalnızca Core'a referanslı; App/Plugins test edilecekse TFM `net9.0-windows10.0.19041.0` olmalı.


<!-- Biçim:
- YYYY-MM-DD | başlık | ne yapıldı / neden yarıda kaldı / ne karar verildi
-->




