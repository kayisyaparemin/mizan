# Ajan Çalışma Akışı Protokolü

Her AI ajanı göreve başladığında bu protokole uymak zorundadır.

## Adım 1: Başlangıç Doğrulaması (Pre-flight Check)
- Koda dokunmadan önce `dotnet test` koşturun.
- Mevcut test durumunu bilmeden kod geliştirmesi yapmayın.

## Adım 2: Mimari ve İş Mantığı İncelemesi
- Geliştirilecek özelliğin hangi katmana ait olduğunu belirleyin (Domain, Application, Presentation).
- Katman sınırlarını ihlal etmeyin (örn. Domain'e Presentation bağımlılığı sokmayın).
- Finansal hesaplama kurallarını `Mizan.Domain/Calculations` altından inceleyin.

## Adım 3: Geliştirme ve Regresyon Testi
- Kod değişikliğini yapın.
- ViewModel ekliyorsanız 350 satır sınırına dikkat edin.
- `async void` kesinlikle kullanmayın.
- İlgili birim testlerini ve `CoreBusinessRegressionTests` paketini çalıştırın.

## Adım 4: Teslim Öncesi %100 Yeşil Test Doğrulaması
- `dotnet test` komutunun 0 hata ile sonuçlandığını teyit edin.
- Asla kırık test veya başarısız derleme bırakarak görevi tamamlandı olarak bildirmeyin.
