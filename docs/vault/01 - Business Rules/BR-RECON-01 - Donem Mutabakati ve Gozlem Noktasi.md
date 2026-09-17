---
id: BR-RECON-01
title: Dönem Mutabakatı ve Gözlem Noktası
domain: Mutabakat ve Dönem Döngüsü
status: Active
pillar: 1. Dönem Döngüsü ve Mutabakat
tags:
  - business-rule
  - reconciliation
  - checkpoint
---

# BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası

## 🎯 Kuralın Amacı ve Özü
Mizan, kullanıcıyı mikro harcama fişi giren bir muhasebe memuruna dönüştürmez. Finansal yönetim; **Planı Dondurma**, **Dönem İçi Gözlem Noktası** ve **Dönem Sonu Mutabakatı** olmak üzere 3 adımdan oluşur.

---

## 📐 Matematiksel Model ve İşleyiş

```
+-------------------------------------------------------------+
|                     AKTİF DÖNEM AKIŞI                       |
|                                                             |
|  [Dönem Başı] --------> [Gözlem Noktası] --------> [Mutabakat]
|   Plan Dondurulur        Mevcut Bakiye              Plan vs. Gerçek
|   Serbest Havuz          Sapma Ölçülür              Kapanış Onayı
+-------------------------------------------------------------+
```

### 1. Plan Dondurma (Frozen Plan)
- Dönem başladığı an, o döneme ait zorunlu giderler (krediler, kart ekstreleri, kira vs.) dondurulur (`PeriodPlanSnapshot`).
- Dönem toplam gelirinden zorunlu giderler düşülerek **Serbest Yaşam Bütçesi Havuzu** belirlenir:
  $$\text{Serbest Yaşam Havuzu} = \text{Toplam Gelir} - \text{Zorunlu Giderler}$$

### 2. Gözlem Noktası (Observation Checkpoint)
- Kullanıcı dönem içinde ana ekranda istediği zaman güncel banka bakiyesini girebilir.
- **Kritik Kural:** Bu bakiye projeksiyonu kirletmez veya planı bozmaz! Sadece gidişatın hedeften sapıp sapmadığını gösteren bir "gözlem"dir.
  $$\text{Dönem İçi Sapma} = \text{Beklenen Kümülatif Bakiye} - \text{Gözlenen Bakiye}$$

### 3. Dönem Kapanış Mutabakatı (Period Reconciliation)
- Dönem bittiğinde kullanıcı:
  1. Hangi zorunlu ödemeleri yaptığını teyit eder ("Ödendi", "Ertelendi", "Farklı Tutar").
  2. Dönem sonu fiili serbest yaşam giderini tek kalemde mutabakat düzeltmesi (`Reconciliation Adjustment`) olarak kaydeder.
  3. Yeni döneme fiili devreden bakiye ile başlanır.

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[CashFlowPeriodCalculator]]
- Use Case: [[PeriodWorkflowService]]
- UI: `PeriodReviewWizardViewModel`

---

## 💬 Karar Gerekçesi (Rationale)
> Geleneksel bütçe uygulamaları 3 hafta sonra terk edilir çünkü kahve fişi girmek sürdürülemez. Mizan'da harcamalar makro havuz olarak yönetilir; dönem sonu mutabakatı ile deterministik denge korunur.
