---
id: BR-INCOME-01
title: Düzensiz Gelirler ve Çapa Öncesi Gelir Penceresi
domain: Gelir Yönetimi ve Projeksiyon
status: Active
pillar: 2. İleriye Dönük 12 Aylık Projeksiyon
tags:
  - business-rule
  - income
  - projection
  - salary
  - pre-period-window
---

# BR-INCOME-01: Düzensiz Gelirler ve Çapa Öncesi Gelir Penceresi

## 🎯 Kuralın Amacı ve Özü
Projeksiyon motorunun gelecekteki nakit akışını tahmin ederken hem maaş artışlarını doğru dönemde devreye almasını sağlamak, hem de finansal durum tespiti (çapa tarihi) ile ilk maaş günü arasındaki boşlukta gerçekleşen tek seferlik gelirlerin (ikramiye, prim, tahsilat) sisteme dahil edilerek sessizce kaybolmasını (silently dropped) engellemektir.

---

## 📐 Matematiksel Formül ve Algoritma

### 1. Maaş Artışı ve Efektif Tarih Çözümleme ([[IncomeResolver]])
Maaş kayıtları zaman serisi olarak `SalaryScheduleEntry` listesinde tutulur. Her dönemin başlangıç tarihi ($Period.Start$) için geçerli maaş şu kurala göre çözülür:

$$\text{EffectiveSalary} = \operatorname{arg\,max}_{entry \in \text{Salaries}} \{ entry.EffectiveDate \mid entry.EffectiveDate \le Period.Start \}$$

- **Dönem Önceliği İlkesi:** Maaş artışı ancak dönemin başlangıç gününde veya öncesinde yürürlüğe girmişse ($EffectiveDate \le Period.Start$) o döneme yansır.
- **Dönem İçi Değişiklik Korunumu:** Ayın ortasında (örn. 15 Ocak) yürürlüğe giren bir zam, 10 Ocak'ta başlayan dönemin maaşını etkilemez. Zamlı maaş ilk kez 10 Şubat döneminde uygulanır.

---

### 2. Standart Dönem İçi Düzensiz Gelirler (`OneTimeIncome`)
Her tek seferlik gelirin belirli bir tarihi ($ExactDate$) ve tutarı ($Amount$) vardır. Standart bir dönem için gelir atanma kuralı:

$$\text{Gelir Dahil} \iff Period.Start \le ExactDate < Period.End$$

---

### 3. Çapa Öncesi Gelir Penceresi (Pre-Period Income Window)
Mizan'da 12 aylık projeksiyon ufku, ilk dönemin maaş gününde ($Period_0.Start$) başlar. Ancak kullanıcının mevcut nakdini girdiği finansal durum tespiti (çapa tarihi / snapshot - $Anchor$) ilk maaş gününden haftalar önce olabilir.

```
                    Çapa Tarihi (Anchor)           İlk Maaş (Period_0.Start)          Dönem Sonu
─────────────────────────┬──────────────────────────────────┬─────────────────────────────┬───> Zaman
                         │                                  │                             │
   [Açılış Bakiyesi]     │    [Çapa Öncesi Gelir Penceresi]  │    [Standart İlk Dönem]     │
   ExactDate < Anchor    │    Anchor <= ExactDate < P0.Start│    P0.Start <= ExactDate    │
   (Zaten Nakitte)       │    (İlk Döneme Eklenir!)         │    (Standart Dönem Geliri)  │
```

#### Problem ve Matematiksel Boşluk:
Eğer kullanıcı 20 Ağustos'ta çapa atıp ilk maaşını 10 Eylül'de alacaksa; 1 Eylül'de hesabına yatacak 30.000 TL prim:
1. 20 Ağustos'taki açılış bakiyesinde (`ProjectionOpeningBalance`) **yoktur** (para henüz hesaba geçmemiştir).
2. Standart $[10 \text{ Eylül}, 10 \text{ Ekim})$ dönemine de **dahil olamaz** (tarih 1 Eylül olduğu için dönem dışındadır).
3. Bu durumda özel bir kural işletilmezse 30.000 TL havada kalır ve sessizce kaybolur.

#### Çözüm Algoritması:
Yalnızca projeksiyonun ilk dönemi için bir `prePeriodIncomeStart = Anchor` penceresi tanımlanır:

$$\text{IsWithinPrePeriodWindow} = (Anchor \le ExactDate < Period_0.Start)$$

Bu pencereye düşen tüm `OneTimeIncome` kalemleri ilk dönemin gelir tablosuna (`IncomeProjectionSummary.OtherIncome`) dahil edilir:

$$\text{Diğer Gelirler}_0 = \sum_{ExactDate \in [Period_0.Start, Period_0.End)} \text{Amount} + \sum_{ExactDate \in [Anchor, Period_0.Start)} \text{Amount}$$

#### Çapa Öncesi Sınır Kuralı:
$ExactDate < Anchor$ olan gelirler kesinlikle projeksiyona **eklenmez**; çünkü bu tutarlar çapa anında zaten tahsil edilmiş olup `ProjectionOpeningBalance` (açılış bakiyesi) içinde mevcuttur. Aksi halde çift sayım (double-counting) hatası oluşur.

---

### 4. Dönem Toplam Gelir Hesabı
Her $i$. dönem için:

$$\text{PrimaryIncome}_i = \text{Maaş Tutarı}$$
$$\text{OtherIncome}_i = \sum \text{Döneme Atanan Düzensiz Gelirler}$$
$$\text{TotalIncome}_i = \text{PrimaryIncome}_i + \text{OtherIncome}_i$$

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[IncomeProjectionCalculator]]
- Sınıf: [[IncomeResolver]]
- Sınıf: [[FinancialProjectionCalculator]]
- Model: [[SalaryScheduleEntry]], [[OneTimeIncome]], [[IncomeProjectionItem]], [[IncomeProjectionSummary]], [[CashFlowPeriod]]

---

## 💬 Karar Gerekçesi (Rationale)
> 1. **Sessiz Kayıpları Önleme:** Kullanıcı ay ortasında bütçe kurduğunda, ay sonuna kadar alacağı avans, prim veya kira gelirlerinin ilk döneme akması gerekir. Bu paraların projeksiyondan düşmesi yapay likidite açığı (false deficit) alarmları üretir.
> 2. **Açılış Bakiyesi Aritmetiği ile Tutarlılık:** Çapa öncesi geliri doğrudan açılış bakiyesine katmak da matematiksel olarak aynı sonucu verebilirdi; ancak kullanıcının gelir kalemlerini şeffafça `IncomeItems` dökümünde görebilmesi ve mutabakat yapabilmesi için kalemin ilk döneme açıkça kaydedilmesi tercih edilmiştir.
> 3. **Deterministik Maaş Değişimi:** Maaş zamlarının effective-date mantığıyla geriye dönük ve ileriye dönük kesin tarihlere bağlanması, geçmiş veriyi bozmadan geleceği simüle edebilmeyi sağlar.
