---
title: PeriodReviewWizardViewModel
type: class
layer: App
namespace: Mizan.App.ViewModels
tags:
  - class
  - viewmodel
  - app
  - reconciliation
---

# `PeriodReviewWizardViewModel`

Dönem sonu mutabakat (Period Review) sürecini adım adım (Plan → Gerçekleşen → Sonuç) yöneten sihirbaz (wizard) ViewModel'i.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** App
- **Türü:** ViewModel / Multi-step Wizard Controller
- **Sorumluluğu:** 
  - Dondurulmuş plan ile dönem sonundaki gerçekleşen ödeme ve gelir durumlarını karşılaştırarak kullanıcıya adım adım sunmak.
  - Planlanan ödemelerin ödenmiş/ödenmemiş durumlarını ve gerçekleşen tutar/tarihlerini (`ActualPaymentInputItem`) almak.
  - Plansız gelişen ek ödeme ve gelir akışlarını (`ActualFlowInputItem`) ile değişken yaşam gideri kırılımlarını toplamak.
  - Mutabakat taslağının dönem sonu bakiyesini ve fark analizini canlı olarak önizlemek (`PreviewPeriodReviewAsync`).
  - Mutabakatı onaylayıp kesinleştirerek yeni dönemin açılış bütçesini belirlemek (`FinalizePeriodReviewAsync`).

---

## 🔗 Bağımlılıklar (Depends On)
- `MizanService` — Dönem mutabakat taslağı hazırlığı, önizleme ve kesinleştirme servisleri.
- `IUserFeedbackService` — Bilgilendirme ve hata diyalogları.
- [[PeriodWorkflowService]] — Dönem döngüsü ve mutabakat orkestratör servisi (MizanService aracılığıyla).
- [[PeriodReviewService]] — Dönem kapatma doğrulama ve devreden bakiye hesaplayıcısı.
- [[PaymentReminderPlanner]] — Ertelenmiş veya yanıtlanmış hatırlatıcı kontrolleri.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `PeriodReviewPage` — Dönem kapatma sihirbazı XAML modal penceresi.
- `MauiProgram` — Dependency Injection konteyner kaydı.
- [[DashboardViewModel]] — Geçen dönem kapandığında gösterilen uyarı üzerinden mutabakat sihirbazını tetikleme.

---

## 📜 Bağlı İş Kuralları
- [[BR-RECON-01 - Donem Mutabakati ve Gozlem Noktasi|BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası]]
