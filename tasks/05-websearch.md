# 05 – WebSearch eklentisi

## Amaç
`youtube beatles` gibi sorgularda ilgili sitede arama yapmak; `google claude` yazınca Google'da `claude` aratmak.

## Bağımlılık
`03-sorgu-cekirdegi.md`

## Yapılacaklar
- **Kaynak modeli:** `SearchSource { Title, ActionKeyword, UrlTemplate, Icon, Enabled }`; şablonda `{q}`, değer `Uri.EscapeDataString(search)`.
- **Varsayılan kaynaklar** (`Settings` içinde, kullanıcı düzenleyebilir):
  - `google` -> `https://www.google.com/search?q={q}`
  - `youtube` -> `https://www.youtube.com/results?search_query={q}`
  - `bing`, `duckduckgo`, `github`, `wikipedia`
- **Sorgu:** `ActionKeyword` eşleşirse tek sonuç: başlık = aranan kelime, alt başlık `Search Youtube` biçimi, sağda `Alt+1` ipucu. Kelime boşsa alt başlık "Aramak için yazın".
- **Çalıştırma:** URL'yi varsayılan tarayıcıda aç; yalnızca `http`/`https` şemasına izin ver.
- **Settings:** kaynaklar `settings.json`'da saklanır; eksikse varsayılanlar yazılır.
- Tek kelimelik sorgu (`youtube`) yalnızca kaynağı gösterir, aramayı çalıştırmaz.

## Kapsam dışı
Arama önerileri (autocomplete), özel ikon yükleme arayüzü, ayar penceresi (07).

## İlgili yollar
`src/Launchly.Plugins/WebSearch/`, `src/Launchly.Core/Settings`, `tests/Launchly.Tests/`

## Bitti ölçütü
- Birim test: `youtube beatles` -> `https://www.youtube.com/results?search_query=beatles`; boşluk ve Türkçe/özel karakterler (`ç ğ ş & ?`) doğru kodlanır.
- Elle: `google claude` + Enter tarayıcıda Google'da `claude` aramasını açar; `youtube beatles` YouTube'da açar.
- Devre dışı kaynak sonuç üretmez.
