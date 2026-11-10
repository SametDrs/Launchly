# Const

Projede değişmeyen gerçekler (sabit kararlar, kısıtlar, tanımlar). Değişen bilgi buraya yazılmaz.

- Hedef çerçeve: App ve Plugins `net9.0-windows10.0.19041.0`; Plugin, Core, Tests `net9.0`.
- Ortak derleme ayarları `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=true`.
- Kullanıcı verisi: `%AppData%\Launchly\` (`DataLocation`). Ayarlar `settings.json` (+ `.bak`, bozuksa `.corrupt`), loglar `Logs\launchly-YYYYMMDD.log`.
- Log: Serilog (App'te). Core/Plugin Serilog'a bağlanmaz; `Microsoft.Extensions.Logging.Abstractions` (`ILogger`) kullanır.
- DI: `Microsoft.Extensions.Hosting` Generic Host, `App.xaml.cs` içinde kurulur.
- .NET 9 SDK PATH'te değil: `C:\Users\samet\AppData\Local\Microsoft\dotnet\dotnet.exe` (gerekirse `DOTNET_ROOT` aynı klasör).

