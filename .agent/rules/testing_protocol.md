# Test Protokolü ve Kalite Standartları

## 1. Sahte Testler (*SourceTests.cs) KESİNLİKLE YASAKTIR
- `File.ReadAllText(...)` kullanarak C# veya XAML dosyalarında metin/string arayan testler yazmak KESİNLİKLE YASAKTIR.
- Testler diskteki dosyaların metnini değil, derlenmiş sistemin nesne ve metotlarını çağırarak çalışma zamanı davranışını test etmelidir.

## 2. Core Business Regresyon Test Paketi
- Projenin ana omurgası `tests/Mizan.Tests/Regression/CoreBusinessRegressionTests.cs` altındadır.
- Aşağıdaki alanlarda yapılan her değişiklikte bu regresyon paketi çalıştırılmalıdır:
  1. Nakit akışı ve dönem geçişleri (Cash Flow & Period Transitions)
  2. Dönem dondurma ve harcama izolasyonu (Period Lock & Mid-Period Charges - I16)
  3. Kredi kartı ekstre, asgari ve ödeme hesaplamaları (Credit Card Calculations)
  4. Kredi itfa, taksit ve erken kapama (Loan Amortization & Early Payoff)
  5. 12 aylık projeksiyon sürekliliği (12-Month Trajectory Continuity)
  6. KMH eksi bakiye faiz hesaplamaları (Deficit Interest Dynamics)

## 3. Test Çalıştırma Komutları
- Regresyon testleri: `dotnet test --filter "FullyQualifiedName~CoreBusinessRegressionTests"`
- Mimari invariant testleri: `dotnet test --filter "FullyQualifiedName~ArchitecturalInvariantTests"`
- Tüm testler: `dotnet test`
