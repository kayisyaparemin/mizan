---
id: BR-ALLOC-01
title: Nakit Akışı Tahsis Stratejisi ve Geçiş Yönetimi
domain: Nakit Akışı ve Bütçe Tahsisi
status: Active
pillar: 1. Dönem Döngüsü ve Mutabakat
tags:
  - business-rule
  - cash-flow
  - allocation-strategy
  - transition
  - invariant
---

# BR-ALLOC-01: Nakit Akışı Tahsis Stratejisi ve Geçiş Yönetimi

## 🎯 Kuralın Amacı ve Özü
Her kullanıcının maaşı ile ödemelerini eşleştirme zihniyeti aynı değildir. Kimisi maaşıyla *önündeki* ayın masraflarını finanse ederken, kimisi maaşıyla *geçen aydan beri birikmiş* borçları kapatır. Bu kural; `PreviousPeriod` ve `UpcomingPeriod` tahsis stratejilerini yönetir ve kullanıcı strateji değiştirdiğinde hiçbir borcun çift ödenmemesini veya arada atlanmamasını (`TransitionCatchUp` ve `TransitionForward`) garanti eder.

---

## 📐 Matematiksel Mantık ve Algoritma

### 1. İki Temel Tahsis Modu ([[CashFlowAllocationMode]])
Maaş günü $T_i$ ve bir sonraki maaş günü $T_{i+1}$ olsun. Her dönem $[T_i, T_{i+1})$ yarı açık aralığıdır.

```
                    Maaş Günü (T_i)           Sonraki Maaş (T_{i+1})
─────────────────────────┬──────────────────────────────┬──────────────────> Zaman
                         │                              │
◄─── [PreviousPeriod] ───┤                              │
   (T_{i-1}, T_i]        │                              │
                         ├─── [UpcomingPeriod] ────────►│
                         │    [T_i, T_{i+1} - 1]        │
```

- **UpcomingPeriod (Gelecek Dönem Modu - Standart/Varsayılan):**
  - Alınan maaş, bir sonraki maaşa kadar olan dönemin yükümlülüklerini karşılar.
  - Hedef Kapsam: $[T_i, T_{i+1} - 1]$
  - Gerekçe Kodu: `PaymentAllocationReason.NormalUpcoming`
  - İlk Dönem İstisnası ($T_0$): Çapa tarihi ile ilk maaş arasına düşen ödemeler ($[Anchor, T_0)$) ilk döneme girmez; `PreFirstPeriodUpcoming` olarak ayrı tutulur (`preFirst`).
- **PreviousPeriod (Geçmiş Dönem Modu):**
  - Alınan maaş, önceki maaştan o güne kadar oluşan borçları kapatır.
  - Hedef Kapsam: $(T_{i-1}, T_i]$
  - Gerekçe Kodu: `PaymentAllocationReason.NormalPrevious`
  - İlk Dönem İstisnası ($T_0$): Çapa tarihi ile ilk maaş arasındaki tüm ödemeler ($[Anchor, T_0]$) ilk dönemin bütçesine bağlanır; gerekçesi `InitialSnapshotCatchUp` olur.

---

### 2. Strateji Değişimi ve Geçiş Yönetimi (Transitions)
Strateji geçmişi effective-dated kayıtlar olarak saklanır ([[CashFlowAllocationStrategy]]). Kullanıcı geçmiş veya gelecek bir dönemde modu değiştirdiğinde sistem deterministik geçiş kurallarını uygular:

#### A. PreviousPeriod $\to$ UpcomingPeriod Geçişi (Boşluk Kapatma ve İleri Fonlama)
Önceki dönem ($T_{i-1}$) `PreviousPeriod` olduğundan yalnızca $T_{i-1}$ gününe kadar olan borçları karşılamıştı. Yeni dönem ($T_i$) ise `UpcomingPeriod` olarak $T_{i+1}-1$ gününe kadar olanları üstlenir. Bu durumda aradaki tüm ödemeler $T_i$ bütçesinde toplanır:

$$\text{CoverageStart} = \text{lastCoveredDate} + 1 = T_{i-1} + 1$$
$$\text{TargetCoveredDate} = T_{i+1} - 1$$

Bu dönem bütçesindeki ödemeler ikiye ayrılır:
1. **TransitionCatchUp:** Vadesi $T_i$ tarihinden önce olan ($DueDate < T_i$) borçlar. Geçmiş modun kapatmadığı boşluğu kapatır:
   $$\text{TransitionCatchUpAmount} = \sum_{DueDate < T_i} \text{Amount}$$
2. **TransitionForward:** Vadesi $T_i$ ve sonrası olan ($DueDate \ge T_i$) borçlar. Gelecek dönemi önden fonlar:
   $$\text{ForwardFundedAmount} = \sum_{DueDate \ge T_i} \text{Amount}$$

#### B. UpcomingPeriod $\to$ PreviousPeriod Geçişi (Mükerrer Ödemeyi Engelleme)
Önceki dönem ($T_{i-1}$) `UpcomingPeriod` iken $T_i - 1$ tarihine kadar olan tüm borçları zaten fonlamıştı. Yeni dönem ($T_i$) `PreviousPeriod` olduğunda $T_i$'ye kadar olanları fonlamak ister.
- `lastCoveredDate` değeri $T_i - 1$ olduğundan:
  $$\text{CoverageStart} = \text{lastCoveredDate} + 1 = T_i$$
  $$\text{TargetCoveredDate} = T_i$$
- Sonuç olarak $T_{i-1}$ ile $T_i$ arasında daha önce Upcoming tarafından fonlanmış hiçbir ödeme **ikinci kez bütçeye dahil edilmez**. Sadece tam $T_i$ günündeki ödemeler eklenir; sonraki ödemeler ise $T_{i+1}$ dönemine bırakılır.

---

### 3. Değişmezlik Güvencesi (Allocation Invariant)
Projeksiyon ufkundaki her bir uygun yükümlülük ($DueDate \le \text{lastCoveredDate}$) için:

$$\text{AssignedExactlyOnceCount} = \text{EligiblePaymentCount}$$
$$\text{UnassignedPaymentCount} = 0$$
$$\text{DuplicateAssignedCount} = 0$$

Hiçbir ödeme kaybolamaz (unassigned = 0) ve hiçbir ödeme iki farklı döneme yazılamaz (duplicate = 0).

---

### 4. Tarihsel Bütünlük Kuralları ([[PaymentAllocationStrategyResolver]])
1. **Dönem Başı Kuralı:** `EffectiveFromPeriodDate` tarihi kesinlikle bir maaş gününe ($IncomeDay$) denk gelmelidir. Ay ortasında strateji değiştirilemez (`IsPeriodStartDate`).
2. **Tekillik:** Aynı dönem tarihi için birden fazla kullanım stratejisi kaydedilemez.
3. **İlk Dönem Kapsamı:** En az bir strateji kaydı ilk projeksiyon dönemini ($firstPeriodStart$) kapsamalıdır.

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[CashFlowAllocationPlanner]]
- Sınıf: [[PaymentAllocationStrategyResolver]]
- Sınıf: [[CashFlowPeriodCalculator]]
- Sınıf: [[MandatoryPaymentCalculator]]
- Model: [[CashFlowAllocationPlan]], [[CashFlowAllocationBudget]], [[CashFlowAllocationStrategy]], [[ObligationItem]]

---

## 💬 Karar Gerekçesi (Rationale)
> 1. **Farklı Finansal Alışkanlıklar:** Bazı kullanıcılar "maaşımı aldım, 15 gün sonraki kiramı ve kartımı ödeyeceğim" der (`UpcomingPeriod`). Bazıları ise "maaşımı aldım, ayın başından beri kartta biriken borcu kapatıyorum" der (`PreviousPeriod`). Yazılım bu iki farklı insan modeline eşit derecede kusursuz uyum sağlamalıdır.
> 2. **Geçiş Güvenliği:** Bir kullanıcı arayüzden bütçeleme modunu değiştirdiğinde, sistem matematiksel olarak ödemeleri yeniden dağıtır. Eğer `TransitionCatchUp` mekanizması olmasaydı, mod değişiminde 1 aylık faturalar ve krediler bütçesiz kalarak gözden kaçabilirdi.
