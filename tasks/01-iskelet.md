# 01 – İskelet

## Amaç
Çözüm ve proje yapısını kurmak; DI, ayarlar ve log altyapısını hazırlamak. Henüz pencere ya da eklenti mantığı yok.

## Bağımlılık
Yok (ilk görev).

## Yapılacaklar
- `Launchly.sln` oluştur; projeleri ekle:
  - `src/Launchly.Plugin` (classlib, bağımlılık yok)
  - `src/Launchly.Core` (classlib, `Launchly.Plugin`'e referans)
  - `src/Launchly.App` (WPF, `Core`'a referans)
  - `src/Launchly.Plugins` (classlib, `Plugin` + `Core`'a referans)
  - `tests/Launchly.Tests` (xUnit)
- Hedef: `net9.0-windows10.0.19041.0` (App ve Plugins), `net9.0` (Plugin, Core, Tests uygunsa).
- `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`.
- `Microsoft.Extensions.Hosting` ile DI iskeleti (`App.xaml.cs`).
- `Launchly.Core`: `Settings` sınıfı + `JsonStorage<T>` (`%AppData%\Launchly\settings.json`, `.bak` yedekli, bozuksa yedekten yükle).
- Log altyapısı (Serilog veya NLog; seçimi `backlog-log.md`'ye yaz).
- `.gitignore` (bin/obj/.vs, kullanıcı dosyaları).
- Klasörlerdeki `.gitkeep` dosyalarını, klasöre gerçek dosya girince sil.

## Kapsam dışı
Pencere, hotkey, eklenti arayüzleri, UI tasarımı.

## İlgili yollar
`Launchly.sln`, `Directory.Build.props`, `src/*`, `tests/Launchly.Tests`

## Bitti ölçütü
- `dotnet build` uyarısız ve hatasız.
- `dotnet test` çalışır (en az bir `JsonStorage` testi: yaz/oku, bozuk dosyada yedekten yükleme).
- Uygulama boş pencereyle ya da pencerisiz açılıp kapanır, ayar dosyası oluşur.
