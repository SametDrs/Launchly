# 03 – Sorgu çekirdeği

## Amaç
Eklenti arayüzünü, sorgu ayrıştırmayı, eklenti yöneticisini, sonuç hattını ve bulanık eşleştirmeyi yazmak; pencereye bağlamak.

## Bağımlılık
`01-iskelet.md`, `02-pencere.md`

## Yapılacaklar
- **`Launchly.Plugin`:**
  - `IPlugin`: `InitAsync(context)`, `QueryAsync(Query, CancellationToken) -> IReadOnlyList<Result>`.
  - `PluginMetadata`: `Id`, `Name`, `ActionKeywords` (`*` = global), `SearchDelayMs`.
  - `Query`: `Raw`, `ActionKeyword`, `Search`, `Terms[]`.
  - `Result`: `Title`, `SubTitle`, `Icon`, `Score`, `TypeLabel`, `AutoComplete`, `Action` (`Func<Task<bool>>`).
- **`Launchly.Core`:**
  - `QueryBuilder`: ilk kelime etkin bir eklentinin keyword'üyse `ActionKeyword`, kalanı `Search`; değilse yalnızca global eklentiler çalışır.
  - `PluginManager`: keyword -> eklenti sözlüğü, paralel çalıştırma, yeni sorguda öncekini iptal (`CancellationToken`), eklenti başına gecikme.
  - Sonuç hattı: `Channel<Result[]>` ile ~20 ms toplu birleştirme, `Score`'a göre sıralama, UI'a tek seferde yazma.
  - `StringMatcher`: kısaltma (`vsc` -> Visual Studio Code) + sıralı bulanık eşleştirme, sade puanlama.
- **`Launchly.App`:** `MainViewModel`'i `PluginManager`'a bağla; Enter seçili sonucun `Action`'ını çalıştırır, ardından pencere gizlenir; Tab `AutoComplete` yazar.

## Kapsam dışı
Gerçek eklentiler (04–06). Test için sahte eklenti yeterli.

## İlgili yollar
`src/Launchly.Plugin/`, `src/Launchly.Core/`, `src/Launchly.App/ViewModels/`, `tests/Launchly.Tests/`

## Bitti ölçütü
- Birim testler geçer: `QueryBuilder` (keyword ayırma, global, bilinmeyen keyword), `StringMatcher` (kısaltma, sıralı eşleşme, eşleşmeme), `PluginManager` (iptal, paralellik).
- Sahte eklentiyle yazdıkça sonuçlar listelenir, hızlı yazımda eski sonuçlar ekrana düşmez.
- `dotnet build` ve `dotnet test` temiz.
