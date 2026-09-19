# Mimari Standartlar ve Katman Kuralları (Clean Architecture)

## Katman Bağımlılık Yönü
1. **Mizan.Domain**:
   - Saf `.NET 8`.
   - Hiçbir üst katmana (`Application`, `Infrastructure`, `App`, `UI`) bağımlı olamaz.
   - Durumsuz (stateless) saf domain hesaplayıcıları (`*Calculator`) burada yer alır.
2. **Mizan.Application**:
   - Use Cases, Interactors, DTOs, Repository Arayüzleri (`ISalaryRepository`, `ILoanRepository`, vb.).
   - Asla UI formatlaması (para formatı, tarih formatı, UI başlık string'leri) içermez.
   - God Service anti-pattern'i yasaktır. Büyük servisler küçük odaklı use case'lere bölünür.
3. **Mizan.Infrastructure**:
   - Yalnızca SQLite, PDF parse, dosya sistemi adaptörlerini barındırır.
   - İş mantığı barındıramaz.
4. **CoinFlow.UI / Presentation**:
   - Saf `.NET 8` ViewModel'ler ve ValueConverter'lar.
   - MAUI Controls bağımlılığı yoktur, doğrudan xUnit ile izole test edilebilir.
   - Navigasyon için `INavigationService`, dialoglar için `IDialogService` soyutlamaları kullanılır.
5. **Mizan.App**:
   - MAUI Shell, XAML sayfaları, stiller ve platform adaptörleri.

## MVVM Kuralları
- **Boyut Sınırı:** Hiçbir ViewModel 350 satırı aşamaz.
- **UI Bağımsızlığı:** `Page`, `NavigationPage`, `Shell.Current`, `Window` referansları yasaktır.
- **Service Locator Yasağı:** `IServiceProvider` enjekte edilemez.
- **Asenkron Fonksiyonlar:** `async void` yasaktır, her zaman `Task` veya `ValueTask` dönülür.
