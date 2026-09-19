# API & Servis Standartları ve Finansal İnvaryantlar (api-conventions.md)

Bu kılavuz, Mizan'ın Domain hesaplayıcıları, Application servisleri ve finansal invaryantlarının tasarım kurallarını tanımlar.

---

## 1. DOMAIN HESAPLAYICILARI (PURE CALCULATORS)

- **Durumsuzluk (Stateless):** `Mizan.Domain/Calculations` altındaki tüm sınıflar saf iş mantığı barındırır. İç durum (state) saklamaz.
- **Değişmezlik (Immutability):** Parametre olarak record veya snapshot alır, yeni bir hesaplama sonucu/projeksiyon nesnesi döner.
- **Dış Bağımlılık Sıfır:** `IClock`, veritabanı veya IO bağımlılıkları Domain içine giremez; tarihler ve veriler parametre olarak aktarılır.

---

## 2. APPLICATION SERVİSLERİ VE SÖZLEŞMELER

- **Use Case Odaklı Tasarım:** Servisler belirli bir amaca odaklanmalıdır (`PeriodWorkflowService`, `LoanPayoffAdvisor`, `ObligationManagementService`). Dev "God Service" yapıları engellenmelidir.
- **Repository Abstractions:** Servisler somut veritabanı sınıflarına değil, arayüzlere bağımlıdır (`IMizanStore`, `ISalaryRepository`, `ILoanRepository`).
- **Hata Yönetimi ve Kullanıcı Mesajları:**
  - Servis ve UI sınırlarında hatalar yakalanarak `UserFacingMessages.FromException(...)` aracılığıyla kullanıcıya anlamlı Türkçe mesajlar sunulur.
  - Asla ham istisna veya stack trace kullanıcı arayüzüne sızdırılmaz.

---

## 3. CORE BUSINESS FİNANSAL İNVARYANTLARI

1. **Invariant I16 (Dondurulmuş Plan Dokunulmazlığı):**
   - Açık dönem başladığında dönemin planı dondurulur (`openPlan`).
   - Dönem ortasında yapılan plansız harcamalar veya kredi kartı işlemleri, planlanan bütçeyi (`PLANLANAN`) ASLA değiştiremez.
   - Bu harcamalar sadece `MEVCUT` (gerçekleşen) ve `GİDİŞAT` (dönem sonu tahmini / KMH faizi) alanlarına etki eder.

2. **Kredi Kartı Ödeme Kuralları:**
   - **Maksimum Sınır:** Ödeme tutarı ekstre borcundan fazla planlanamaz: `Math.Min(requested, statementBalance)`.
   - **Minimum Sınır:** Ödeme tutarı yasal asgari tutarın altında planlanamaz: `Math.Max(fixedAmount, minimumPayment)`.
   - **Sabit Ödeme (FixedAmount):** Dönem içi yeni harcamalar mevcut dönemin ödeme tutarını artırmaz; kalan borç bir sonraki ekstreye devreder.

3. **Para Korunumu (Zero Drift Invariant):**
   - Dönem $N$ kapanış bakiyesi (`EndingProjectedBalance`), Dönem $N+1$ açılış bakiyesine (`OpeningBalance`) kuruşu kuruşuna eşit olmak zorundadır.

4. **KMH / Eksi Bakiye Faiz Mantığı:**
   - Dönem sonu bakiye tahmini negatife düştüğünde, açık tutar üzerinden günlük KMH faizi hesaplanarak projeksiyona dahil edilir.

---

## 4. İYİ VE KÖTÜ ÖRNEKLER

### Örnek 1: Dondurulmuş Plan İnvaryantı (I16)

❌ **KÖTÜ (İnvaryant İhlali):**
```csharp
// YASAK: Dönem içi harcama planlanan bütçeyi güncelliyor
public void RecordMidPeriodExpense(decimal amount)
{
    _openPlan.PlannedLivingExpenses += amount; // I16 İhlali!
}
```

✅ **İYİ (I16 Uyumlu):**
```csharp
// İYİ: Planlanan kilitli kalır, harcama yalnızca gerçekleşen ve dönem sonu projeksiyonuna yansır
public void RecordMidPeriodExpense(decimal amount)
{
    // PlannedLivingExpense DEĞİŞMEZ
    _currentObservation.ObservedLivingSpend += amount;
    _projectedEndingBalance = CalculateProjectedEnding(_openPlan, _currentObservation);
}
```

### Örnek 2: Kart Ödeme Sınırları

✅ **İYİ (Sınır Kontrolleri):**
```csharp
public static decimal BoundPayment(decimal requested, decimal statementBalance, decimal minimumPayment)
{
    var capped = Math.Min(requested, statementBalance);
    return Math.Max(capped, minimumPayment);
}
```
