---
title: PeriodReviewService
type: use-case
layer: Application
namespace: Mizan.Application.Services
tags:
  - class
  - use-case
  - application
  - reconciliation
---

# `PeriodReviewService`

Dönem sonu mutabakatı (Period Review) süreçlerinde dondurulmuş plan ile fiili durum gerçekleşmelerini karşılaştıran, mutabakat farkını hesaplayan ve yeni dönem açılış snapshot'ını üreten temel Use Case servisi.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Application
- **Türü:** Application Service / Workflow Interactor
- **Sorumluluğu:** 
  - Dönemin kapatılmaya hazır olup olmadığını ve en güncel dondurulmuş planı sorgulamak (GetAvailabilityAsync).
  - Planlanan ödemeler ile gerçekleşen ödemeler/gelirler arasındaki sapmaları hesaplamak (GetContextAsync, PreviewAsync).
  - Taslak mutabakat girdilerini (PeriodReviewDraft) işleyerek gerçekleşen dönem kaydını (PeriodActual) oluşturmak (BuildActual).
  - Dönem kapatıldığında borç, kart ve taksit enstrümanlarının bakiyelerini güncellemek (FinancialInstrumentReconciliationService) ve yeni FinancialSnapshot oluşturmak (FinalizeAsync).

---

## 🔗 Bağımlılıklar (Depends On)
- `FinancialSnapshotService` — Anlık finansal durum bundle'ı inşası ve en güncel snapshot sorgusu.
- `FinancialStateReconciliationService` — Fiili duruma göre önerilen kapanış bakiyesi hesaplaması.
- `FinancialInstrumentReconciliationService` — Dönem sonu fiili ödemelerine göre kredi, kart ve harcama enstrüman durumlarının güncellenmesi.
- `PlanActualComparisonCalculator` — Planlanan vs gerçekleşen kalem bazlı sapma analizleri.
- `IMizanStore` — Mutabakat sonucunun ve yeni finansal durumun veritabanına kaydı.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- [[PeriodWorkflowService]] — Dönem akışları ve gözlem noktaları orkestrasyonu.
- `PeriodReviewWizardViewModel` — Dönem kapatma ve mutabakat sihirbazı ekranı.
- `MizanService` — Ana uygulama servis cephesi.

---

## 📜 Bağlı İş Kuralları
- [[BR-RECON-01 - Donem Mutabakati ve Gozlem Noktasi|BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası]]
