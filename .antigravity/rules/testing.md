# Test Standartları ve Kalite Protokolü (testing.md)

Bu kılavuz, Mizan projesinde birim, entegrasyon ve regresyon testlerinin nasıl yazılması ve doğrulanması gerektiğini tanımlar.

---

## 1. SAHTE TEST YASAĞI (*SourceTests.cs) — MUTLAK YASAK

- ❌ **`File.ReadAllText(...)` YASAKTIR:** C# veya XAML dosyalarında metin/regex taraması yaparak mimari veya kod kuralı doğrulamaya çalışan testler yazılamaz.
- ✅ **Çalışma Zamanı Davranışı:** Testler kaynak kod metnini değil; derlenmiş sınıfların, metotların, hesaplayıcıların ve ViewModel'lerin çalışma zamanı davranışını (runtime behavior) doğrulamalıdır.

---

## 2. xUNIT STANDARTLARI & İSİMLENDİRME

- **İsimlendirme Şablonu:** `MethodAdı_Senaryo_BeklenenDavranış`
  - Örnek: `Calculate_WhenNegativeBalance_AccruesDeficitInterest`
  - Örnek: `ObserveCurrentBalanceAsync_WithValidAmount_UpdatesObservation`
- **AAA Deseni (Arrange-Act-Assert):**
  - **Arrange:** Girdileri ve test durumunu hazırla (`TestFactory` kullan).
  - **Act:** Test edilecek metodu çağır.
  - **Assert:** Sonuçları doğrula (FluentAssertions veya xUnit `Assert`).

---

## 3. CORE BUSINESS REGRESYON TEST PAKETİ

Finansal hesaplama, dönem akışı veya bakiye mantığını etkileyen her değişiklikte aşağıdaki testler mutlaka çalıştırılmalıdır:
- **Dosya:** `tests/Mizan.Tests/Regression/CoreBusinessRegressionTests.cs`
- **Komut:**
  ```powershell
  dotnet test --filter "FullyQualifiedName~CoreBusinessRegressionTests"
  ```
- **Kapsanan Kritik Alanlar:**
  1. Nakit akışı ve dönem geçişleri (Cash Flow & Period Transitions)
  2. Dönem dondurma ve harcama izolasyonu (Period Lock & Mid-Period Charges - Invariant I16)
  3. Kredi kartı ekstre, asgari ve ödeme hesaplamaları (Credit Card Calculations)
  4. Kredi itfa, taksit ve erken kapama (Loan Amortization & Early Payoff)
  5. 12 aylık projeksiyon sürekliliği (12-Month Trajectory Continuity)
  6. KMH eksi bakiye faiz hesaplamaları (Deficit Interest Dynamics)

---

## 4. İYİ VE KÖTÜ TEST ÖRNEKLERİ

### Örnek 1: Davranış Testi vs. Sahte Metin Testi

❌ **KÖTÜ (Anti-pattern - Sahte Test):**
```csharp
[Fact]
public void ViewModel_ShouldNotUseShellCurrent()
{
    // YASAK: Kaynak kodu metin olarak tarayan sahte test
    var sourceCode = File.ReadAllText("../../../src/Mizan.App/ViewModels/DashboardViewModel.cs");
    Assert.DoesNotContain("Shell.Current", sourceCode);
}
```

✅ **İYİ (Davranışsal xUnit Testi):**
```csharp
[Fact]
public async Task SaveCurrentBalanceAsync_WhenAmountIsValid_InvokesServiceAndReloads()
{
    // Arrange
    var store = new InMemoryMizanStore();
    var service = TestFactory.CreateService(store);
    var navigation = new FakeNavigationService();
    var reminders = new PaymentReminderCardViewModel(service);
    var viewModel = new DashboardViewModel(service, navigation, reminders);

    viewModel.CurrentBalanceInput = "15.000";

    // Act
    await viewModel.SaveCurrentBalanceCommand.ExecuteAsync(null);

    // Assert
    var progress = await service.GetPeriodProgressAsync();
    Assert.NotNull(progress?.Observation);
    Assert.Equal(15000m, progress.Observation.ObservedBalance);
}
```

---

## 5. SIK KULLANILAN TEST KOMUTLARI

```powershell
# Tüm testleri çalıştırma (Derlemesiz hızlı)
dotnet test --no-build

# Mimari testleri çalıştırma
dotnet test --filter "FullyQualifiedName~ArchitecturalInvariantTests"

# Başarısız testleri listeleme
dotnet test --verbosity normal
```
