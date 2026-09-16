# Mizan — Mimari Kurallar ve Geliştirme Standartları (Architectural Invariants)

> **Amaç:** Bu doküman, Mizan projesinde Clean Architecture ve Modern MVVM prensiplerini kesin olarak bağlamak, "vibecoding" (kontrolsüz/plansız kod üretimi) kaynaklı anti-pattern'lerin tekrarını engellemek ve kod kalitesini garanti altına almak amacıyla oluşturulmuştur.  
> Projede kod yazan tüm geliştiriciler ve AI asistanları bu kurallara uymakla yükümlüdür.

---

## 1. Katman Mimarisi ve Bağımlılık Yönü (Clean Architecture)

Proje bağımlılıkları kesin olarak tek yönlüdür ve tersine bağımlılık yasaktır:

```text
┌─────────────────────────────────────────────────────────────┐
│                    CoinFlow.App                             │
│   (Yalnızca XAML Views, DataTemplates, Shell & Platform)   │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                    CoinFlow.UI / Presentation                │
│   (Saf .NET 8: ViewModels, ValueConverters, Navigation/     │
│    Dialog Abstractions — MAUI Controls bağımlılığı YOKTUR)   │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                    CoinFlow.Application                      │
│   (Use Cases / Interactors, DTOs, Repository Interfaces)    │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                    CoinFlow.Domain                           │
│   (Entities, Value Objects, Pure Calculators, Invariants)   │
└─────────────────────────────────────────────────────────────┘
                               ▲
┌──────────────────────────────┴──────────────────────────────┐
│                    CoinFlow.Infrastructure                   │
│   (SQLite Store, PDF Importers, Device Storage Adaptors)    │
└─────────────────────────────────────────────────────────────┘
```

### Katman Sınır Kuralları:
1. **`CoinFlow.Domain`**:
   - Saf C# ve `.NET 8` olmalıdır.
   - UI, MAUI, veritabanı veya harici kütüphane bağımlılığı barındıramaz.
   - Entity'ler kendi geçerlilik kurallarını (invariants) korumalıdır.
   - Hesaplama motorları (`*Calculator`) saf domain servisleridir; durum (state) tutamazlar.
2. **`CoinFlow.Application`**:
   - Yalnızca kullanım senaryolarını (Use Cases / Interactors), DTO'ları ve repository sözleşmelerini (interfaces) barındırır.
   - **ASLA** UI formatlaması (örn: Türkçe para formatı, tarih formatı, UI başlık metinleri) barındıramaz.
   - `SalaryPeriodDetailPresenter` gibi sınıflar Application katmanında değil, UI/Presentation katmanında yer almalıdır.
3. **`CoinFlow.Infrastructure`**:
   - Yalnızca veritabanı, dosya sistemi, PDF okuyucu gibi dış dünya implementasyonlarını içerir.
   - İş mantığı barındıramaz.
4. **`CoinFlow.UI` (Saf Presentation)**:
   - Tüm ViewModel'ler, `IValueConverter`'lar, `INavigationService`, `IDialogService` burada yer alır.
   - `net8.0` hedeflemeli ve platformdan bağımsız olmalıdır (MAUI Controls bağımlılığı olmamalıdır). Böylece doğrudan xUnit testleri ile izole test edilebilir.
5. **`CoinFlow.App` (MAUI Shell & Platform Host)**:
   - Yalnızca XAML sayfaları, stiller, custom kontroller ve `Platforms/` altındaki platform adaptörlerini içerir.

---

## 2. MVVM ve Sunum Katmanı Kuralları (Presentation Invariants)

### A. Sınıf Boyutu ve Sorumluluk Sınırı (No God ViewModels)
- **350 Satır Sınırı:** Hiçbir ViewModel sınıfı **350 satırı** geçemez.
- **Tek Sorumluluk:** Bir ViewModel birden fazla bağımsız özelliği aynı anda yönetemez (`CommitmentsViewModel` gibi 1.300 satırlık dev sınıflar yasaktır).
- **Alt Bileşenler (Child ViewModels):** Sayfa içindeki karmaşık bölümler alt component view model'lerine bölünmelidir.
- **Sihirbaz / Adım Akışları (Wizard Flows):** `Onboarding` gibi çok adımlı akışlar, tek bir ViewModel içinde onlarca boolean bayrak (`isIntro`, `isCard`, `isLoan`) ile değil; `IOnboardingStepViewModel` gibi adım arayüzleri ve her adıma özel bağımsız ViewModel'ler ile kurgulanmalıdır.

### B. UI Bağımlılıkları ve Navigasyon Kuralları
- ViewModel'lerin içinde `Page`, `NavigationPage`, `Shell.Current`, `Window` veya herhangi bir MAUI UI sınıfı **KESİNLİKLE KULLANILAMAZ**.
- ViewModel'ler sayfa `Task`'larını (`await page.Completion`) bekleyemez.
- **Service Locator Yasaktır:** ViewModel veya Page yapıcılarına `IServiceProvider` enjekte edilemez (`services.GetRequiredService<Page>()` çağrıları yasaktır).
- Navigasyon işlemleri yalnızca `INavigationService` üzerinden strongly-typed parametrelerle yapılmalıdır.
- Kullanıcıya gösterilen uyarı ve onay pencereleri yalnızca `IDialogService` üzerinden yürütülmelidir (`CurrentPage().DisplayAlert` çağrısı yasaktır).

### C. XAML Code-Behind Kuralları
- Sayfa code-behind dosyaları (`*.xaml.cs`) yalnızca UI yaşam döngüsü (animasyon, layout) ile sınırlı kalmalıdır.
- Buton tıklamaları, liste seçimleri ve form aksiyonları code-behind `Click` eventleri ile değil; XAML üzerinden `Command` ve `CommandParameter` ile ViewModel'e bağlanmalıdır.
- `IQueryAttributable` veya sayfa navigasyon parametreleri Page code-behind'ında değil, doğrudan ilgili ViewModel'de karşılanmalıdır.

### D. Veri Bağlama ve Formatlama
- ViewModel'ler önceden biçimlendirilmiş string listeleri (`PlannedLivingText`, `CurrentPeriodText` vb.) üretmemelidir.
- ViewModel ham veriyi (`decimal`, `DateOnly`) sunmalı; para ve tarih formatlaması XAML tarafında **ValueConverter**'lar (`CurrencyConverter`, `DateOnlyConverter`) veya `StringFormat` ile yapılmalıdır.
- `ViewModelBase` içine hardcoded kültür (`tr-TR`) veya genel `Money()` formatlayıcıları gömülmemelidir.

### E. Asenkron Metot ve Hata Yönetimi (`async void` Yasağı)
- ViewModel ve servislerde `async void` kullanımı **KESİNLİKLE YASAKTIR**. Tüm asenkron metotlar `Task` veya `ValueTask` dönmelidir.
- Yalnızca XAML event handler'ları mecburi durumlarda `async void` olabilir; ancak tüm metot gövdesi mutlaka `try { ... } catch (Exception ex)` içine alınarak Android runtime crash'i engellenmelidir.

---

## 3. Application ve Veri Erişim Kuralları (Backend & Storage Invariants)

### A. No God Service
- `CoinFlowService.cs` gibi 2.000 satırlık mega servisler yasaktır.
- Her iş akışı odaklı bir **Use Case / Interactor** (veya CQRS Command/Query Handler) olarak tanımlanmalıdır:
  - `GetDashboardProgressQueryHandler`
  - `ApplySimulationScenarioCommandHandler`
  - `ImportCreditCardStatementCommandHandler`
  - vb.

### B. Interface Segregation (Arayüz Ayrışımı - ISP)
- `ICoinFlowStore` gibi 40'tan fazla metot içeren monolitik arayüzler yasaktır.
- Depolama arayüzleri varlık veya modül bazlı küçük sözleşmelere ayrılmalıdır:
  - `ISettingsRepository`
  - `ISalaryRepository`
  - `ICreditCardRepository`
  - `ILoanRepository`
  - `ISimulationDraftRepository`

### C. Profil / Multi-Tenancy Yaşam Döngüsü (DI Scoping)
- Profil değişimlerinde, tüm store çağrılarını `_current` değişkenine delege eden yapay wrapper sınıflar (`ProfileScopedCoinFlowStore`) yerine, .NET DI konteynerinin yerel **`IServiceScope`** mekanizması kullanılmalıdır.
- Profil seçildiğinde bir child scope açılmalı, profil kapatıldığında o scope dispose edilmelidir. Store ve profile bağımlı servisler `Scoped` yaşam döngüsüne sahip olmalıdır.

---

## 4. Test Stratejisi ve Kalite Standartları (Testing Invariants)

### A. "Kaynak Kodu Metin Olarak Okuyan Testler" (`*SourceTests.cs`) KESİNLİKLE YASAKTIR
- `File.ReadAllText(...)` kullanarak diske gidip C# veya XAML dosyalarında regex/string arayarak yapılan sahte testler projeye eklenemez, mevcut olanlar gerçek davranış testlerine dönüştürülmelidir.
- Testler **kod metnini değil, sistemin çalışma zamanı davranışını (behavior)** test etmelidir.

### B. Gerçek Birim Testleri (Real Unit Testing)
- ViewModel'ler ve Use Case'ler, bağımlılıkları mock'lanarak (`NSubstitute` veya `Moq`) gerçek xUnit testleri ile doğrulanmalıdır:
  ```csharp
  [Fact]
  public async Task AddLoan_WithValidData_NavigatesBackAndRefreshes()
  {
      // Arrange
      var mockNav = Substitute.For<INavigationService>();
      var mockUseCase = Substitute.For<ICreateLoanUseCase>();
      var vm = new LoanEditorViewModel(mockUseCase, mockNav);
      
      // Act
      vm.Amount = 50000m;
      await vm.SaveCommand.ExecuteAsync(null);
      
      // Assert
      await mockUseCase.Received(1).ExecuteAsync(Arg.Any<CreateLoanRequest>());
      await mockNav.Received(1).GoBackAsync();
  }
  ```

---

## 5. Platform ve Taşınabilirlik Standartları (Portability)

- Tüm iş mantığı ve ViewModel katmanları saf `.NET 8` (`net8.0`) olmalıdır.
- Platforma özel (Android) sınıflar (`AndroidStorageAccess`, `PaymentReminders` vb.) sadece `CoinFlow.App/Platforms/Android` altında kalmalı ve platform arayüzlerini (`IStorageAccess`, `IReminderScheduler`) implemente etmelidir.
- `MauiProgram.cs` içinde platform sınıfları koşulsuz olarak kaydedilmemeli, platform DI modülleri üzerinden (`#if ANDROID` veya partial class `ConfigurePlatformServices`) bağlanmalıdır.

---

## 6. Sürüm Çıkma ve CI/CD Yaşam Döngüsü Standardı (Release & CI/CD Lifecycle Invariant)

- **Release Talebinin Sonu Başarılı Pipeline'dır:** Kullanıcı bir sürüm çıkılmasını (release) istediğinde, süreç yalnızca `git tag` oluşturulup push edilerek sonlandırılamaz.
- **Aktif İzleme (Active CI/CD Monitoring):** Agent veya sorumlu mühendis, GitHub Actions pipeline'ını (build, test, release-apk) API veya CLI üzerinden aktif olarak polling/webhook ile takip etmelidir (`status: completed`, `conclusion: success`).
- **Uçtan Uca Sorumluluk:** Pipeline'da herhangi bir hata (derleme, test, paketleme, imzalama) oluşursa; hata logları derhal analiz edilmeli, kod/yapılandırma düzeltilmeli, commit & tag güncellenip süreç tekrar başlatılmalı ve pipeline başarıyla sonuçlanana kadar takip sürdürülmelidir.
- **Doğrulama ve Raporlama:** Süreç, ancak ve ancak GitHub Releases üzerinde ilgili sürüme ait APK / release asset'i başarıyla yayınlandığında tamamlanmış sayılır. Geliştirici kullanıcıya release bağlantısını, APK detaylarını ve özet raporunu sunarak görevi noktalar.

