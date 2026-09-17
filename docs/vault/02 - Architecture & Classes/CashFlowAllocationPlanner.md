---
title: CashFlowAllocationPlanner
type: class
layer: Domain
namespace: Mizan.Domain.Calculations
tags:
  - class
  - domain
  - allocation
  - cashflow
  - budget
---

# `CashFlowAllocationPlanner`

Kullanıcının ödeme tahsis stratejisine (`PreviousPeriod` vs `UpcomingPeriod`) göre zorunlu ödemelerin hangi projeksiyon döneminin bütçesine yazılacağını belirleyen ve mükerrer/kayıp atama kontrollerini gerçekleştiren Domain planlayıcısı.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Domain
- **Türü:** Saf Hesaplayıcı (Ödeme Tahsis & Dönemsel Bütçe Planlayıcı)
- **Sorumluluğu:**
  - Projeksiyon anchor tarihinden itibaren tüm zorunlu ödemeleri (`ObligationItem`) dönemsel bütçelerin (`CashFlowAllocationBudget`) kapsama tarihlerine (`CoverageStart` - `CoverageEnd`) atamak.
  - Strateji değişikliklerini tespit ederek geçiş dönemlerinde yakalama ödemelerini (`TransitionCatchUp`) ve önden fonlamaları (`IsForwardFunded`) işaretlemek.
  - İlk döneme ait özel yakalama durumlarını (`InitialSnapshotCatchUp` ve `PreFirstPeriodUpcoming`) hesaplamak.
  - Her ödemenin tam olarak 1 döneme atandığını doğrulamak; açıkta kalan (`UnassignedPaymentCount`) veya mükerrer atanan (`DuplicateAssignedCount`) ödemeleri raporlamak.

---

## 🔗 Bağımlılıklar (Depends On)
- [[PaymentAllocationStrategyResolver]] — Strateji geçmişini doğrular ve belirli bir dönem tarihi için aktif tahsis modunu çözer.
- [[CashFlowPeriodCalculator]] — Projeksiyon dönemlerini (`CashFlowPeriod`) kapsama tarihleri için temel alır.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- [[FinancialProjectionCalculator]] — Her bir dönemin yükümlülük bütçesini ve finansman açığını hesaplarken ödeme dağıtım planını (`CashFlowAllocationPlan`) almak için çağırır.
- [[MauiProgram]] — Dependency Injection container'ına Singleton olarak kaydeder.

---

## 📜 Bağlı İş Kuralları
- [[BR-RECON-01 - Donem Mutabakati ve Gozlem Noktasi|BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası]]
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
