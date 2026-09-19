# Kodlama Standartları ve Mimari Prensipler (code-style.md)

Bu kılavuz, Mizan projesinde kod üretirken veya düzenlerken uyulması gereken C# 12, Clean Architecture ve MVVM standartlarını tanımlar.

---

## 1. C# 12 & .NET 8 İDİOMLARI

- **Primary Constructors:** Servis ve ViewModel sınıflarında bağımlılık enjeksiyonu için primary constructor tercih edilir.
- **Collection Expressions:** `new List<T>()` veya `new Collection<T>()` yerine modern `[]` sözdizimi kullanılır.
- **Pattern Matching:** Null ve tip kontrollerinde `is { }` veya `is not null` yapıları kullanılır.

---

## 2. CLEAN ARCHITECTURE KATMAN SINIRLARI

```text
Mizan.App ──► CoinFlow.UI / Presentation ──► Mizan.Application ──► Mizan.Domain
                    ▲                              ▲
                    │                              │
                    └────────── Mizan.Infrastructure ┘
```

1. **Mizan.Domain:**
   - Saf `.NET 8`.
   - Kesinlikle hiçbir üst katmana (`Application`, `Infrastructure`, `App`) veya harici NuGet paketine bağımlı olamaz.
   - Durumsuz (stateless) saf domain hesaplayıcıları (`*Calculator`) ve değişmez (immutable) domain kayıtları/modelleri burada yer alır.
2. **Mizan.Application:**
   - Use Case'ler, orkestrasyon servisleri (`MizanService`), DTO'lar ve repository arayüzleri (`IMizanStore`, `ISalaryRepository`).
   - Asla UI formatlaması (para birimi string'leri, tarih formatları, renkler) içermez.
3. **Mizan.Infrastructure:**
   - Yalnızca dış dünya implementasyonları (SQLite veritabanı, PDF aktarımı, dosya sistemi).
   - İş mantığı barındıramaz.
4. **Mizan.App (veya UI/Presentation):**
   - MVVM ViewModel'leri, XAML sayfaları, kontroller ve ValueConverter'lar.

---

## 3. MVVM & VIEWMODEL STANDARTLARI

- **350 Satır Kuralı:** Hiçbir ViewModel tek bir fiziksel C# dosyasında 350 satırı aşamaz. Büyüyen ViewModel'ler `partial` sınıflara bölünür:
  - `FeatureViewModel.cs` (Ana durum ve constructor)
  - `FeatureViewModel.Operations.cs` (Komutlar ve operasyonlar)
  - `FeatureViewModel.Cards.cs` / `*.Statements.cs` (Spesifik alt akışlar)
- **UI Kontrol Bağımsızlığı:**
  - ViewModel içinde `Page`, `NavigationPage`, `Shell.Current`, `Window` veya MAUI UI kontrolleri (`Button`, `Label`) referans edilemez.
  - Sayfa geçişleri için `INavigationService`, kullanıcı diyalogları için `IDialogService` kullanılır.
- **Service Locator Yasağı:**
  - `IServiceProvider` enjeksiyonu KESİNLİKLE YASAKTIR (`services.GetRequiredService<T>()` kullanılamaz).
  - Yalnızca Constructor Injection kullanılır.
- **`async void` Yasağı:**
  - Tüm asenkron metotlar ve komutlar `Task` veya `ValueTask` dönmelidir.

---

## 4. İYİ VE KÖTÜ KOD ÖRNEKLERİ

### Örnek 1: Primary Constructor & MVVM Komutları

❌ **KÖTÜ (Anti-pattern):**
```csharp
public class BadViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    
    // YASAK: IServiceProvider enjeksiyonu (Service Locator)
    public BadViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // YASAK: async void
    public async void OnSaveClicked()
    {
        var service = _serviceProvider.GetRequiredService<MizanService>();
        await service.SaveAsync();
        
        // YASAK: ViewModel içinde doğrudan Shell/Page UI çağrısı
        await Shell.Current.DisplayAlert("Bilgi", "Kaydedildi", "Tamam");
    }
}
```

✅ **İYİ (Mizan Standardı):**
```csharp
// Primary constructor ile doğrudan bağımlılık enjeksiyonu
public partial class GoodViewModel(
    MizanService service,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase
{
    [ObservableProperty]
    private string name = string.Empty;

    // RelayCommand ile Task dönen asenkron metot
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            await service.SaveAsync(Name);
            await dialogs.AlertAsync("Bilgi", "Kaydedildi", "Tamam");
            await navigation.NavigateBackAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

### Örnek 2: 350 Satır Sınırı ve Partial ViewModel Bölünmesi

Mizan'da `CardControlViewModel` ve `CommitmentsViewModel` sınıfları şu şekilde modüler tutulmuştur:
- `CardControlViewModel.cs`: Durum değişkenleri (`[ObservableProperty]`), koleksiyonlar ve constructor.
- `CardControlViewModel.Operations.cs`: Limit güncelleme, ekstre ekleme vb. `[RelayCommand]` metotları.
- `CardControlViewModel.Statements.cs`: PDF ekstre aktarımı ve ekstre satırları işleme mantığı.
