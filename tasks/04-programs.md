# 04 – Programs eklentisi

## Amaç
Kurulu uygulamaları bulup ikonlarıyla listelemek ve açmak.

## Bağımlılık
`03-sorgu-cekirdegi.md`

## Yapılacaklar
- **Tarama:** Start Menu klasörleri (kullanıcı + ortak) altındaki `.lnk` dosyaları; hedef yolu ve görünen adı çıkar. Yinelenenleri ele.
- **Önbellek:** JSON dosyası (`%AppData%\Launchly\cache\programs.json`); açılışta önbellekten yükle, arka planda yeniden tara.
- **İkon:** shell ikonu -> `BitmapSource`, bellek içi önbellekli, UI iş parçacığına uygun dondurulmuş (`Freeze`).
- **Sorgu:** global eklenti (`*`); `StringMatcher` ile puanla; `TypeLabel = "Application"`.
- **Çalıştırma:** `Process.Start` (`UseShellExecute=true`); başarısızlıkta hatayı logla.
- Boş sorguda uygulamaları alfabetik göster (1. görseldeki gibi).

## Kapsam dışı
UWP/AppX uygulamaları (sonraya), PATH taraması, "yönetici olarak çalıştır" (Actions).

## İlgili yollar
`src/Launchly.Plugins/Programs/`, `tests/Launchly.Tests/`

## Bitti ölçütü
- `visual` yazınca Visual Studio Code listede üst sıralarda, ikonuyla görünür.
- `vsc` kısaltması çalışır.
- Enter uygulamayı açar, pencere gizlenir.
- Tarama UI'ı dondurmaz; ikinci açılış önbellekten hızlıdır.
