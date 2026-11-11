# Notlar

## v1.1 turu — kullanıcı testinden çıkan maddeler (2026-11-11)

Kullanıcı kurulu uygulamayı test etti; aşağıdaki sekiz madde ve alınan kararlar. Kodlama henüz başlamadı.

### Kararlaştırılanlar

1. **Logo** — Uygulama logosu yok. Mavi tonlarında olacak; tarz yöneticiye bırakıldı. Görsel üretimle birkaç aday çıkarılır, kullanıcı seçer. Seçilenden çok boyutlu `.ico` üretilir; exe, tray, ayar penceresi ve installer aynı dosyayı kullanır.
2. **Yarı saydam tema** — Windows 11 Acrylic/Mica bulanıklığı. Windows 10'da mevcut koyu temaya düşer. Koyu tema kalır; tema ayarlardan seçilir.
3. **Görünmeyen uygulamalar** — Claude ve ChatGPT listede yok. Olası neden: paketlenmiş (Store/MSIX) uygulamalar `04-programs` kapsamı dışındaydı (kodda doğrulanmadı). Yön: taramayı `shell:AppsFolder` üzerine taşımak.
4. **Açılışta en çok kullanılanlar** — Boş sorguda rastgele sistem programları yerine en çok kullanılanlar. Başlatma sayısı + yakınlık (frecency) kaydı tutulur; aynı puan arama sıralamasına da katılır. Hiç kullanım kaydı yokken **sabit başlangıç listesi** gösterilir.
5. **Settings tasarımı** — Flow Launcher'ın ayar ekranı örnek alınır: sol menülü düzen (Genel, Görünüm, Arama kaynakları, Eklentiler, Hakkında), ana pencereyle aynı tema.
6. **Actions düğmesi** — Şu an çalışmıyor. Seçili sonuç için eylem menüsü (`Ctrl+K`): Aç, Yönetici olarak çalıştır, Dosya konumunu aç, Yolu kopyala. Eklenti SDK'sında `Result`'a eylem listesi eklenir.
7. **Özelleştirilebilir arama** — Kullanıcı anahtar kelime = URL tanımlar (örnek: `film=https://example.com/`). Anahtar kelime **siteyi doğrudan açar**. URL'de `{q}` varsa arama yapar (genel kural). Ayarlardaki "Arama kaynakları" sayfasından eklenir/silinir.
8. **Sonuç sayısı** — **Yalnızca boş açılışta** 8 yerine 6 sonuç. Yazarken sınır değişmez.

### Onaylanan ekler (2026-11-11)

- Sabit başlangıç listesi (6 öğe): Dosya Gezgini, varsayılan tarayıcı, Ayarlar, Terminal, Not Defteri, Hesap Makinesi.
- Uygulama gizleme (Actions menüsünden) — `09`'a dahil.
- Sabitleme (açılış listesinde sabit tut) — `09`'a dahil.
- `UrlPlugin`'in `notes.md` gibi dosya adlarını site sanması — `10`'a dahil.
- `FilesPlugin` her sorguda disk taraması (önbellek ya da Windows Search) — sonraki tura.
- Logo tarzı: koyu lacivert zemin, açık maviye geçişli soyut "L" / yukarı ok birleşimi; üç aday üretilir, kullanıcı seçer.

### Görev yazımı sonrası kararlar (2026-11-11)

- Boş açılışta 6'dan az kullanılmış uygulama varsa kalan yerler sabit başlangıç listesinden doldurulur.
- `UrlPlugin` düzeltmesi: `.md`, `.py`, `.zip` gibi dosya uzantısıyla biten çıplak girdiler site sayılmaz; açmak için `https://` ya da `www.` gerekir. Kabul edildi.
- Varsayılan saydam tema Mica'dan Acrylic'e geçer. Not: `Backdrop` ayarı kodda zaten var ama opak görünüyor; madde 2 eksik değil, hata.
- "Yönetici olarak çalıştır" Store (paketli) uygulamalarda sunulmaz. Kabul edildi.
- `Ctrl+Shift+Enter`: menü açmadan yönetici olarak çalıştır. `09`'a eklendi.
- Madde 3 kök nedeni doğrulandı: `ProgramScanner` yalnızca iki Başlat Menüsü klasöründeki `*.lnk` dosyalarını tarıyor.

### Görev sırası

1. `08` Program keşfi + kullanım kaydı (madde 3, 4, 8)
2. `09` Actions menüsü (madde 6)
3. `10` Özel arama (madde 7)
4. `11` Logo + yarı saydam tema + Settings tasarımı (madde 1, 2, 5)

İşlev önce, görünüm sonra: Settings sayfaları `09` ve `10`'da ekleneceği için tasarım en sona kalırsa iki kez yapılmaz.
