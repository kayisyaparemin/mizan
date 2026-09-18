---
title: PeriodWorkflowService
type: use-case
layer: Application
namespace: Mizan.Application.Services
tags:
  - use-case
  - application
  - workflow
  - reconciliation
---

# `PeriodWorkflowService`

Dönem döngüsü, dönem içi gözlem noktası (checkpoint) kaydı ve dönem sonu mutabakat (Period Review) süreçlerini yöneten ana orkestratör Use Case servisi.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** `Application`
- **Türü:** Application Service / Workflow Interactor
- **Sorumluluğu:** 
  - Dönem mutabakatının uygunluğunu (`PeriodReviewAvailability`) kontrol etmek.
  - Dönem içi kaydedilmiş olan gözlem noktasını (`ObservedReviewDraft`) getirmek.
  - Dönem tamamlandığında dondurulmuş plan ile fiili durumu karşılaştırıp dönem kapatma işlemini onaylamak (`CommitPeriodReviewAsync`).
  - Yeni dönemin açılış bütçesini ve devreden bakiyesini belirlemek.

---

## 🔗 Bağımlılıklar (Depends On)
- `IMizanStore` — Veri erişim soyutlaması.
- `IClock` — Sistem saati soyutlaması.
- [[PeriodReviewService]] — Mutabakat hesaplamaları ve doğrulama.
- `PeriodProgressService` — Dönem içi gün ve harcama ilerleme yüzdesi.
- `IFinancialPlanQueryService` — Aktif finansal plan sorguları.
- [[CashFlowPeriodCalculator]] — Dönem sınırları.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `PeriodReviewWizardViewModel` — Dönem kapatma sihirbazı.
- `MainPage` & `DashboardViewModel` — Dönem durumunu göstermek için.

---

## 📜 Bağlı İş Kuralları
- [[BR-RECON-01 - Donem Mutabakati ve Gozlem Noktasi|BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası]]
