# Const

Projede değişmeyen gerçekler (sabit kararlar, kısıtlar, tanımlar). Değişen bilgi buraya yazılmaz.

- Hedef çerçeve: App ve Plugins `net9.0-windows10.0.19041.0`; Plugin, Core, Tests `net9.0`.
- Ortak derleme ayarları `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=true`.
- Kullanıcı verisi: `%AppData%\Launchly\` (`DataLocation`). Ayarlar `settings.json` (+ `.bak`, bozuksa `.corrupt`), loglar `Logs\launchly-YYYYMMDD.log`.
- Program verisi: önbellek `%AppData%\Launchly\cache\programs.json` (v2: Kind/AppUserModelId), kullanım `%AppData%\Launchly\usage.json` (anahtar `ProgramEntry.Id`: `lnk:`/`app:`/`cmd:` önekli, büyük/küçük harf duyarsız).
- `usage.json` (Version 1): `Items` (sayaçlar), `Hidden` (`Id`+`Name`), `Pinned` (sıralı Id). Alanlar yoksa ya da null ise boş liste; gizleme/sabitleme bu dosyada, `settings.json`'da değil.
- Sonuç eylemleri: `ResultAction.RunAsAdminId = "launchly.runas"`; UAC iptali `Win32Exception.NativeErrorCode == 1223`. Paketli (AppsFolder) uygulamada yönetici ve dosya konumu eylemi yoktur.
- `JsonStorage.Load` dosya yoksa varsayılan içeriği diske yazar.
- Kabuk ikonları (`IShellItemImageFactory.GetImage`) düz (ön çarpılmamış) alfalı 32 bit DIB döner: `PixelFormats.Bgra32`.
- Log: Serilog (App'te). Core/Plugin Serilog'a bağlanmaz; `Microsoft.Extensions.Logging.Abstractions` (`ILogger`) kullanır.
- DI: `Microsoft.Extensions.Hosting` Generic Host, `App.xaml.cs` içinde kurulur.
- .NET 9 SDK PATH'te değil: `C:\Users\samet\AppData\Local\Microsoft\dotnet\dotnet.exe` (gerekirse `DOTNET_ROOT` aynı klasör).

