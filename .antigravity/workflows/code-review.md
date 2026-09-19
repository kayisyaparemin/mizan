# Kod İnceleme İş Akışı (code-review.md)

Bu iş akışı, Mizan deposunda yapılan kod değişikliklerinin mimari standartlara, finansal invaryantlara ve kod kalitesine uygunluğunu doğrulamak için izlenecek adımları tanımlar.

---

## ADIM ADIM İNCELEME KONTROL LİSTESİ

### 1. Adım: Test Durumunu Doğrula (Pre-flight)
İncelemeye başlamadan önce mevcut test paketinin durumunu kontrol edin:
```powershell
dotnet test --no-build
```
> [!IMPORTANT]
> Testlerde tek bir hata bile varsa, incelemeyi durdurun veya öncelikle bu hatayı raporlayın.

### 2. Adım: Katman Bağımlılıklarını İncele
- [ ] `Mizan.Domain` katmanına herhangi bir dış paket veya üst katman (`Application`, `Infrastructure`, `App`) bağımlılığı eklenmiş mi?
- [ ] `Mizan.Application` katmanında UI formatlaması veya MAUI referansı var mı?
- [ ] `Mizan.Infrastructure` katmanına iş mantığı (business logic) yazılmış mı?

### 3. Adım: MVVM & ViewModel Kontrolleri
- [ ] Değiştirilen veya eklenen herhangi bir ViewModel dosyası **350 satırı** aşıyor mu?
- [ ] `partial class` mantığı doğru kullanılmış mı (`*.Operations.cs`, `*.Cards.cs` vb.)?
- [ ] ViewModel içinde `Page`, `NavigationPage`, `Shell.Current` veya MAUI UI kontrolleri var mı?
- [ ] `IServiceProvider` enjekte edilmiş mi (Service Locator anti-pattern)?
- [ ] Herhangi bir `async void` metot bulunuyor mu? (İstisnasız `Task` veya `ValueTask` olmalı)

### 4. Adım: Core Business & Finansal İnvaryant Kontrolleri
- [ ] **Invariant I16:** Dönem içi plansız harcamalar dondurulmuş planlanan bütçeyi (`PLANLANAN`) etkiliyor mu? (Asla etkilememeli!)
- [ ] **Kredi Kartı Sınırları:** Ödemeler ekstre borcundan fazla veya yasal asgariden az planlanabiliyor mu?
- [ ] **Para Korunumu:** Dönem kapanış bakiyesi ile sonraki dönem açılış bakiyesi arasında kuruş sapması var mı?

### 5. Adım: Test Kalitesi ve Sahte Test Kontrolü
- [ ] Eklenen testlerde `File.ReadAllText(...)` ile metin taraması yapan sahte testler (`*SourceTests.cs`) var mı?
- [ ] Testler AAA desenine ve `Method_Scenario_ExpectedBehavior` isimlendirmesine uyuyor mu?
- [ ] Finansal mantık değiştiyse `CoreBusinessRegressionTests.cs` çalıştırıldı mı?

### 6. Adım: Nihai Doğrulama (Post-flight)
```powershell
dotnet test
```
Tüm testler eksiksiz yeşil olduğunda inceleme onaylanır.
