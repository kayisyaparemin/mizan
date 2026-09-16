# Mizan — Mimari Kurallar ve Geliştirme Standartları (Architectural Invariants)

Bu dosya, projenin mimari bütünlüğünü korumak için belirlenen temel kuralları tanımlar.
Ayrıntılı rehber için [docs/ARCHITECTURAL_RULES.md](docs/ARCHITECTURAL_RULES.md) dosyasına bakınız.

---

## Özet Kural Tablosu

| Alan | Kesinlikle Yasak (Anti-Pattern) | Olması Gereken Standart (Target) |
|---|---|---|
| **ViewModel Boyutu** | >350 satır dev sınıflar (`CommitmentsViewModel`, `OnboardingViewModel`) | SRP'ye uygun odaklı ViewModel'ler + Child ViewModel bileşenleri |
| **ViewModel UI Bağımlılığı** | `Page`, `NavigationPage`, `Shell.Current`, `Window` | `INavigationService` ve `IDialogService` soyutlamaları |
| **Service Locator** | `IServiceProvider.GetRequiredService<Page>()` | Constructor Dependency Injection |
| **Code-Behind Mantığı** | Click eventleri ile parametre aktarımı ve iş mantığı | XAML `Command` + `CommandParameter` veri bağlama |
| **Metin Biçimlendirme** | ViewModel içinde Türkçe para/tarih formatı üretmek | XAML `IValueConverter` veya `StringFormat` |
| **Asenkron Fonksiyonlar** | `async void` kullanımı | `Task` veya `ValueTask` (Event handler'larda `try/catch`) |
| **Application Katmanı** | 2.100 satırlık God Service (`CoinFlowService`) | Odaklı Use Case / Interactor sınıfları |
| **Veri Katmanı Arayüzü** | 40+ metotlu God Interface (`ICoinFlowStore`) | Segregated Repositories (`ISalaryRepository`, `ILoanRepository`...) |
| **Profil / Multi-Tenancy** | Manuel pass-through delegasyon wrapper'ları | .NET DI Container `IServiceScope` yaşam döngüsü |
| **Test Stratejisi** | `File.ReadAllText` ile kod metni arayan testler (`*SourceTests.cs`) | Mock'lu gerçek xUnit ViewModel ve Use Case birim testleri |
| **Platform Taşınabilirliği**| `MauiProgram.cs` içine hardcoded Android sınıfları | Saf `net8.0` çekirdek + `#if ANDROID` / platform DI modülleri |
