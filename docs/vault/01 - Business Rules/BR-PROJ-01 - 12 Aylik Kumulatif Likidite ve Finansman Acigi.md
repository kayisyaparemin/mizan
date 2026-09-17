---
id: BR-PROJ-01
title: 12 Aylık Kümülatif Likidite ve Finansman Açığı
domain: Projeksiyon ve Likidite
status: Active
pillar: 2. İleriye Dönük 12 Aylık Projeksiyon
tags:
  - business-rule
  - projection
  - liquidity-deficit
---

# BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı

## 🎯 Kuralın Amacı ve Özü
Kullanıcının bugünkü nakit durumundan (`Anchor Snapshot`) başlayarak, önümüzdeki tam 12 ay (365 gün) boyunca kümülatif likidite eğrisini çizmek ve olası **Finansman Açıklarını (Liquidity Deficit / Eksi Bakiye)** aylar öncesinden tespit etmektir.

---

## 📐 Matematiksel Formül ve Akış

Her $i$. dönem için ($i = 1 \dots 12$):

$$\text{Açılış Bakiyesi}_i = \text{Kapanış Bakiyesi}_{i-1}$$

$$\text{Toplam Gelir}_i = \text{Düzenli Gelirler}_i + \text{Düzensiz/Beklenen Gelirler}_i$$

$$\text{Zorunlu Ödemeler}_i = \sum \text{Krediler} + \sum \text{Ekstreler} + \sum \text{Taksitler} + \sum \text{Planlı Harcamalar}$$

$$\text{Dönem Neti}_i = \text{Toplam Gelir}_i - \text{Zorunlu Ödemeler}_i$$

$$\text{Kapanış Bakiyesi}_i = \text{Açılış Bakiyesi}_i + \text{Dönem Neti}_i - \text{Hedef Yaşam Bütçesi}_i$$

### ⚠️ Finansman Açığı Kuralı (Liquidity Deficit Invariant)
Eğer herhangi bir dönemde:
$$\text{Kapanış Bakiyesi}_i < 0$$
ise sistem bunu **Finansman Açığı Riski** olarak işaretler. Bu açık:
1. Sonraki döneme devredilen faiz maliyeti yükler.
2. Dashboard'da ve Gelecek Aylar ekranında uyarı tetikler.
3. Kullanıcıya açık kapatma veya harcama öteleme önerisi sunar.

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[FinancialProjectionCalculator]]
- Sınıf: [[CashFlowPeriodCalculator]]
- Application: `FinancialProjectionService`
- UI: `FutureMonthsViewModel`, `DashboardViewModel`
