# Mizan — Antigravity / Gemini Agent Guidelines (GEMINI.md)

Bu depoda çalışan Antigravity ve Gemini ajanları için bağlayıcı talimatlardır.
Ayrıntılı rehber için [AGENTS.md](AGENTS.md) ve [docs/ARCHITECTURAL_RULES.md](docs/ARCHITECTURAL_RULES.md) dosyalarına bakınız.

## MUTLAK ZORUNLULUKLAR
1. **İşe Başlamadan Önce:** Her zaman `dotnet test` koşturun. Kırık bir durum varsa önce tespit edin.
2. **Kör Tahmin Yapmayın:** Domain kurallarını ve finansal hesaplamaları ezberden değil, `Mizan.Domain/Calculations` altındaki sınıflardan inceleyerek anlayın.
3. **Core Business Regresyon Testleri:** Finansal hesaplama, dönem akışı veya bakiye mantığını değiştiren her görevde `tests/Mizan.Tests/Regression/CoreBusinessRegressionTests.cs` testlerini çalıştırın.
4. **Emülatör Üzerinde E2E Regresyon Testi:** Projede her geliştirme yapıldığında tüm uygulama akışlarını emülatör üzerinde test eden `powershell ./scripts/run-emulator-regression-tests.ps1` scripti çalıştırılmalı ve tüm senaryolar yeşil olmalıdır.
5. **Asla Kırık Kod Bırakmayın:** Tüm testler (%100 birim/mimari testler ve emülatör E2E testleri) yeşil olmadan görevi teslim etmeyin.

## KESİNLİKLE YASAKLAR
- **`*SourceTests.cs` Yazılamaz:** `File.ReadAllText` ile C#/XAML metnini tarayan sahte testler yasaktır. Gerçek xUnit davranış testleri yazın.
- **ViewModel > 350 Satır Olamaz:** 350 satırı aşan sınıflar parçalanmalıdır.
- **ViewModel İçinde UI Olamaz:** `Page`, `Shell`, `NavigationPage`, MAUI Controls referansı kesinlikle yasaktır.
- **`async void` Yasaktır:** Sadece `Task` veya `ValueTask` dönün.
- **Service Locator Yasaktır:** `IServiceProvider` enjeksiyonu yasaktır, constructor injection kullanın.
- **Invariant I16:** Dönem içi plansız harcamalar dondurulmuş planlanan bütçeyi (`PLANLANAN`) ASLA değiştiremez. Sadece `MEVCUT` ve `GİDİŞAT`'a yansır.
