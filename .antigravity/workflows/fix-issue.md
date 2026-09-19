# Hata Ayıklama ve Doğrulama Akışı (fix-issue.md)

Bu iş akışı, Mizan projesinde bildirilen veya tespit edilen bir hatayı sistematik, güvenli ve regresyona yol açmadan çözmek için izlenecek adımları tanımlar.

---

## ADIM ADIM HATA ÇÖZÜM AKIŞI

### 1. Adım: Hatayı Bir xUnit Testiyle Yeniden Üret (TDD Yaklaşımı)
- Kodu doğrudan değiştirmeden önce, bildirilen hatalı davranışı sergileyen ve şu anda **kırmızı (başarısız)** olan bir xUnit davranış testi yazın.
- ❌ **Dikkat:** `File.ReadAllText` ile sahte test yazmayın. Test nesnelerin çalışma zamanı davranışı üzerine olmalıdır.
- Testi çalıştırıp beklenen şekilde başarısız olduğunu teyit edin:
  ```powershell
  dotnet test --filter "FullyQualifiedName~YeniYazilanTestAdi"
  ```

### 2. Adım: Kök Neden Analizi Yap
- Hatayı tahmin etmek yerine ilgili domain hesaplayıcısını (`Mizan.Domain/Calculations`) veya application servisini (`Mizan.Application/Services`) inceleyin.
- Finansal bir tutarsızlık varsa Invariant I16, kredi kartı ödeme sınırları veya KMH faizi hesap kurallarını kontrol edin.

### 3. Adım: En Yalın Düzeltmeyi Uygula
- Hatayı mimari katman sınırlarını ihlal etmeden çözün.
- Domain mantığı ise `Mizan.Domain` içinde saf hesaplayıcıyı düzeltin.
- ViewModel ise 350 satır sınırına, `async Task` kuralına ve UI kontrol bağımsızlığına dikkat edin.

### 4. Adım: Hedef Testi Doğrula
- 1. Adımda yazdığınız testin artık **yeşil (başarılı)** olduğunu teyit edin:
  ```powershell
  dotnet test --filter "FullyQualifiedName~YeniYazilanTestAdi"
  ```

### 5. Adım: Regresyon ve Tam Test Doğrulaması
- Finansal veya dönem akışı ile ilgili bir alana dokunduysanız çekirdek regresyon paketini koşturun:
  ```powershell
  dotnet test --filter "FullyQualifiedName~CoreBusinessRegressionTests"
  ```
- Tüm test paketini çalıştırın:
  ```powershell
  dotnet test --no-build
  ```
- Sıfır hata ile sonuçlandığından emin olun.
