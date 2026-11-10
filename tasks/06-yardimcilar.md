# 06 – Yardımcı eklentiler

## Amaç
Hesap makinesi, URL açma, sistem komutları ve dosya aramayı eklemek.

## Bağımlılık
`03-sorgu-cekirdegi.md` (tamamlanması şart); `05-websearch.md` ile bağımsız, paralel yapılabilir.

## Yapılacaklar
Her eklenti kendi klasöründe, `IPlugin` uygular.

- **Calculator** (global): ifade matematiksel görünüyorsa sonuç göster (`2+2*3`); Enter sonucu panoya kopyalar. Kütüphane seçimi (NCalc veya `DataTable.Compute`) `backlog-log.md`'ye yazılır. Sıfıra bölme gibi hatalarda sonuç üretme.
- **Url** (global): `http(s)://...` veya `alan.adi` biçimi girilince "Open URL" sonucu; yalnızca `http`/`https`; şema yoksa `https://` ekle.
- **System** (keyword yok, ada göre eşleşir): `lock`, `sleep`, `shutdown`, `restart`, `empty recycle bin`. Geri dönüşsüz komutlarda (shutdown, restart) çalıştırmadan önce onay adımı.
- **Files** (keyword `f `): önce basit dizin taraması (kullanıcı klasörleri); Windows Search API'ye geçiş kararı `backlog-log.md`'ye yazılır. Enter dosyayı açar; Actions'ta "klasörde göster" ileride.

## Kapsam dışı
Dosya içeriği arama, çeviri, pano geçmişi, dış eklenti yükleme.

## İlgili yollar
`src/Launchly.Plugins/{Calculator,Url,System,Files}/`, `tests/Launchly.Tests/`

## Bitti ölçütü
- Birim testler: Calculator (geçerli/geçersiz ifade), Url (algılama ve şema ekleme).
- Elle: `2+2*3` -> `8`; `github.com` -> URL sonucu; `lock` -> kilitler; `shutdown` onay ister; `f rapor` dosya listeler.
- `dotnet build` ve `dotnet test` temiz.
