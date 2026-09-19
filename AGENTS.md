# Mizan — Evrensel Ajan Kuralları ve Geliştirme Protokolü (AGENTS.md)

Bu dosya, bu depoda (repository) çalışan **tüm yapay zeka ajanları (Antigravity, Cursor, Codex, Claude vb.) için bağlayıcı ve kesin** kuralları tanımlar.
Bu depoda kod üreten, düzenleyen veya test yazan her ajan bu kurallara **harfiyen uymak zorundadır**.

---

## 1. ZORUNLU AJAN İŞ AKIŞI PROTOKOLÜ (MANDATORY AGENT WORKFLOW)

Bir ajan herhangi bir göreve başladığında ve görevi bitirdiğinde şu adımları izlemekle yükümlüdür:
1. **İşe Başlamadan Önce Doğrulama:**
   - Koda dokunmadan önce mutlaka derleme ve test durumunu kontrol edin (`dotnet test`).
   - Mevcut bir hata varsa, görevinize bu hatayı bilerek veya çözerek başlayın.
2. **Kör Tahmin ve Varsayım Yasaktır:**
   - Bir servisin, modelin veya hesaplayıcının nasıl çalıştığını tahmin etmeyin; ilgili domain hesaplayıcısını (`Mizan.Domain/Calculations`) ve servis sözleşmesini (`Mizan.Application`) inceleyin.
3. **Core Business Regresyon Testi Zorunluluğu:**
   - Nakit akışı, dönem ilerlemesi (PeriodProgress), kredi kartı ekstre/ödeme, kredi itfa (amortization) veya projeksiyon kodlarına dokunduysanız; mutlaka `tests/Mizan.Tests/Regression/CoreBusinessRegressionTests.cs` testlerini çalıştırın.
4. **Asla Kırık Kod / Kırık Test Bırakılamaz:**
   - Görevi tamamlamadan önce tüm test paketinin (`dotnet test`) eksiksiz yeşil (0 hata) olduğunu doğrulamadan kullanıcıya "tamamlandı" yanıtı verilemez.

---

## 2. KESİNLİKLE YASAKLI PRATİKLER (FORBIDDEN PATTERNS)

Aşağıdaki anti-pattern'ler derleme veya mimari testler tarafından anında reddedilir:
- ❌ **`*SourceTests.cs` (Sahte Testler):** `File.ReadAllText(...)` kullanarak C# veya XAML dosyalarında metin/regex arayan sahte testler yazmak **KESİNLİKLE YASAKTIR**. Testler kod metnini değil, sistemin çalışma zamanı davranışını doğrulamalıdır.
- ❌ **350 Satırı Aşan ViewModel'ler:** Hiçbir ViewModel sınıfı 350 satırı aşamaz. Büyüyen ViewModel'ler Child ViewModel veya bileşen sınıflarına bölünmelidir.
- ❌ **ViewModel İçinde UI Bağımlılığı:** ViewModel'lerde `Page`, `NavigationPage`, `Shell.Current`, `Window` veya MAUI UI kontrolleri kullanılamaz. Navigasyon için `INavigationService`, uyarılar için `IDialogService` kullanılır.
- ❌ **Service Locator:** `IServiceProvider` sınıflara veya ViewModel'lere enjekte edilemez (`services.GetRequiredService<T>()` yasaktır). Yalnızca constructor injection kullanılır.
- ❌ **`async void`:** ViewModel ve servislerde `async void` kullanımı **KESİNLİKLE YASAKTIR**. İstisnasız `Task` veya `ValueTask` dönülmelidir. (Sadece XAML UI event handler'larında mecburi ise ve tüm gövde `try-catch` içindeyse kullanılabilir).
- ❌ **Hardcoded Kültür ve Format:** ViewModel içinde string para/tarih formatlama (`.ToString("C")`, `"tr-TR"`) yasaktır. ViewModel ham `decimal` veya `DateOnly` döner; formatlama XAML ValueConverter'larda yapılır.

---

## 3. KATMAN MİMARİSİ VE BAĞIMLILIK KURALLARI (CLEAN ARCHITECTURE)

```text
Mizan.App (MAUI Host & XAML Views)
   │
   ▼
CoinFlow.UI / Presentation (Saf .NET 8 ViewModels, Converters - MAUI kontrol bağımlılığı YOKTUR)
   │
   ▼
Mizan.Application (Use Cases, Interactors, DTOs, Repository Arayüzleri)
   │
   ▼
Mizan.Domain (Entities, Pure Calculators, Invariants - Sıfır Dış Bağımlılık)
   ▲
   │
Mizan.Infrastructure (SQLite Store, PDF Importer, Platform Adaptörleri)
```
- `Mizan.Domain` kesinlikle hiçbir üst katmana veya harici kütüphaneye bağımlı olamaz.
- `Mizan.Application` asla UI formatlaması barındıramaz.
- `Mizan.Infrastructure` yalnızca dış dünya implementasyonlarını içerir, iş mantığı barındıramaz.

---

## 4. CORE BUSINESS & FİNANSAL İNVARYANTLAR

1. **Invariant I16 (Dondurulmuş Plan Dokunulmazlığı):**
   - Açık dönem başladığında dondurulan plan (`openPlan`), dönem içinde yapılan plansız kredi kartı harcamaları veya nakit harcamalarla **asla kirletilemez**.
   - `PLANLANAN` kolonları kilitli kalır.
   - Harcama yalnızca `MEVCUT` (Current) ve `GİDİŞAT` (Projected Ending Balance / KMH Faizi) alanlarını etkiler.
2. **Kredi Kartı Ödeme Kuralları:**
   - Bir kredi kartına ekstre borcundan fazla ödeme planlanamaz (`Math.Min(requested, statementBalance)`).
   - Yasal asgari ödeme tutarının altında ödeme yapılamaz (`Math.Max(fixedAmount, minimumPayment)`).
   - Sabit tutarlı ödeme planı (`FixedAmount`) seçilmişse, dönem ortasında yapılan ek harcamalar o dönemin ödeme tutarını artırmaz; artan borç bir sonraki ekstreye devreder.
3. **Para Korunumu (Conservation of Balance):**
   - 12 aylık projeksiyonda veya çok dönemli nakit akışında bir dönemin kapanış bakiyesi (`EndingProjectedBalance`), bir sonraki dönemin açılış bakiyesine (`OpeningBalance`) kuruşu kuruşuna eşit olmak zorundadır. Drift (sapma) kabul edilemez.
4. **KMH / Eksi Bakiye Faiz Mantığı:**
   - Dönem sonu bakiye projeksiyonu negatife düştüğünde, eksi bakiye üzerinden günlük KMH faizi dinamik olarak hesaplanarak dönem sonu tahminine dahil edilir.

---

## 5. İLGİLİ DOKÜMANTASYON

- Detaylı mimari kurallar: [docs/ARCHITECTURAL_RULES.md](docs/ARCHITECTURAL_RULES.md)
- Modüler kurallar: `.agent/rules/`
- Core business regresyon testleri: `tests/Mizan.Tests/Regression/CoreBusinessRegressionTests.cs`
- Mimari invariant testleri: `tests/Mizan.Tests/Architecture/ArchitecturalInvariantTests.cs`
