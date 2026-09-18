---
id: BR-CALENDAR-01
title: Ay Sonu ve Artık Yıl Tarih Sabitleme
domain: Takvim ve Tarih Aritmetiği
status: Active
pillar: 4. Gerçekçi Maliyet ve Faiz Modellemesi
tags:
  - business-rule
  - calendar
  - leap-year
  - month-end
  - date-arithmetic
  - invariant
---

# BR-CALENDAR-01: Ay Sonu ve Artık Yıl Tarih Sabitleme

## 🎯 Kuralın Amacı ve Özü
Takvim aylarının farklı gün sayısına sahip olması (28, 29, 30, 31 gün) ve artık yıllar nedeniyle ortaya çıkan tarih kaymalarını (drift) önlemektir. Ayın 31'inde maaş alan veya kredi kartı kesilen bir kullanıcının döngüsünün Şubat ayında 28/29'a kırpıldıktan sonra takip eden aylarda sonsuza kadar 28'de takılı kalmasını engelleyerek tercih edilen günü hafızada tutmaktır.

---

## 📐 Matematiksel Model ve Algoritma

### 1. Gün Doğrulama (ValidateDay)
Tüm sistem genelinde kullanıcıdan veya banka ayarlarından alınan ödeme/maaş/hesap kesim günleri katı bir aralık denetiminden geçer:

$$1 \le preferredDay \le 31$$

- Eğer $preferredDay < 1$ veya $preferredDay > 31$ ise sistem anında `ArgumentOutOfRangeException` fırlatır.

---

### 2. Ay İçinde Güvenli Gün Çözümleme (ResolveDay)
Verilen bir yıl ($Y$) ve ay ($M$) için, o ayın çeken gün sayısı $DaysInMonth(Y, M)$ olmak üzere:

$$ResolveDay(Y, M, preferredDay) = \operatorname{DateOnly}(Y, M, \min(preferredDay, DaysInMonth(Y, M)))$$

Örnek Çözümlemeler ($preferredDay = 31$ için):
- $ResolveDay(2027, 1, 31) \implies \text{31 Ocak 2027}$
- $ResolveDay(2027, 2, 31) \implies \text{28 Şubat 2027}$ (Artık olmayan yıl)
- $ResolveDay(2028, 2, 31) \implies \text{29 Şubat 2028}$ (Artık yıl)
- $ResolveDay(2027, 4, 31) \implies \text{30 Nisan 2027}$

---

### 3. Tercih Edilen Günü Koruyarak Ay Ekleme (AddMonthsKeepingDay)

#### Klasik Tarih Kütüphanelerinin "Kelepçeleme" (Drift) Hatası:
Standart `.NET` `DateTime.AddMonths` veya benzer kütüphanelerde bir tarihe ardışık olarak ay eklendiğinde:
- $\text{31 Ocak} \xrightarrow{+1 \text{ ay}} \text{28 Şubat}$
- $\text{28 Şubat} \xrightarrow{+1 \text{ ay}} \mathbf{28 \text{ Mart!}}$ *(Hata: 31 Mart olması gerekirken gün 28'e kilitlenir)*
- $\text{28 Mart} \xrightarrow{+1 \text{ ay}} \mathbf{28 \text{ Nisan!}}$

Bu kalıcı kayma (drift), 12 aylık finansal projeksiyonda tüm vadelerin yanlış güne oturmasına ve faizlerin hatalı gün esasıyla çarpılmasına neden olur.

#### Mizan Çözüm Formülü:
Mizan, referans tarih üzerine $m$ ay eklerken kullanıcının orijinal `preferredDay` tercihini parametre olarak korur ve her adımda hedef ayın tavanına göre çözer:

$$AddMonthsKeepingDay(date, m, preferredDay) = ResolveDay\Big(\big(date.AddMonths(m)\big).Year, \big(date.AddMonths(m)\big).Month, preferredDay\Big)$$

#### 31 Gün Zincirleme Örneği:
Tercih edilen gün $preferredDay = 31$ olduğunda 12 aylık periyot serisi:

```
  Ocak 31  ──>  Şubat 28  ──>  Mart 31  ──>  Nisan 30  ──>  Mayıs 31
  (Tam gün)     (Kırpıldı)    (Geri Yükseldi) (Kırpıldı)   (Geri Yükseldi)
```

1. $m = 0 \implies \text{31 Ocak}$
2. $m = 1 \implies \text{28 Şubat}$ (Kısa ay kelepçesi)
3. $m = 2 \implies \mathbf{31 \text{ Mart}}$ (Orijinal gün geri kazanıldı)
4. $m = 3 \implies \text{30 Nisan}$ (30 gün kelepçesi)
5. $m = 4 \implies \mathbf{31 \text{ Mayıs}}$ (Orijinal gün geri kazanıldı)

---

### 4. Artık Yıl (Leap Year) Yönetimi
Artık yıl hesabı asla sabit 28 gün varsayımı ile yapılmaz. `DateTime.DaysInMonth` mekanizması ile 4'e bölünen (ve 100/400 kuralına uyan) yıllarda Şubat ayı 29 gün olarak değerlendirilir:
- 2027 (Normal Yıl): 31 Ocak $\to$ **28 Şubat 2027** $\to$ 31 Mart 2027
- 2028 (Artık Yıl): 31 Ocak $\to$ **29 Şubat 2028** $\to$ 31 Mart 2028
- 2029 (Normal Yıl): 31 Ocak $\to$ **28 Şubat 2029** $\to$ 31 Mart 2029

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[CalendarRules]]
- Sınıf: [[CashFlowPeriodCalculator]] (Maaş döngüleri ve dönem sınırları)
- Sınıf: [[CreditCardStatementCalculator]] (Hesap kesim ve son ödeme tarihleri)
- Sınıf: [[LoanScheduleCalculator]] (Kredi taksit vadeleri)
- Sınıf: [[InstallmentScheduleCalculator]] (Taksitli harcama planı)
- Sınıf: [[LoanAmortizationCalculator]] (Önceki vade tarihi `PreviousDueDate` türetimi)

---

## 💬 Karar Gerekçesi (Rationale)
> 1. **Takvim Determinizmi:** Bankalar kredi kartı kesim gününü kullanıcının sözleşmesinde "her ayın 31'i" olarak belirler. Şubat ayında ekstre 28'inde kesilse bile, Mart ayında banka tekrar 31'inde kesim yapar. Yazılımın banka ile birebir mutabık kalması için bu esneklik zorunludur.
> 2. **Faiz Hesap Hassasiyeti:** Kredi erken kapamasında gün işleyen faiz ($\Delta t \times r / 30$) gün farkına doğrudan bağlıdır. Vade gününün 2-3 gün kayması faiz ve anapara amortisman tablosunu bozar.
> 3. **Merkezi Tekillik (DRY):** Tarih aritmetiği sistemdeki düzinelerce sınıfta dağınık if-else bloklarıyla yapılmaz; tüm domain `CalendarRules` üzerindeki saf (pure) matematiksel fonksiyonları kullanır.
