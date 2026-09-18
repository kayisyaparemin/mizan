---
title: SimulationViewModel
type: class
layer: App
namespace: Mizan.App.ViewModels
tags:
  - class
  - viewmodel
  - app
  - simulation
---

# `SimulationViewModel`

Kullanıcının canlı finansal planını bozmadan What-If senaryolarını (harcama, taksit, borç, erken kapatma, düzen değişikliği) denemesini, sonuçları kıyaslamasını ve onaylanan senaryoları canlı plana aktarmasını sağlayan ViewModel.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** App
- **Türü:** ViewModel / UI Presenter
- **Sorumluluğu:** 
  - Senaryo koşul formunu (`ScenarioConditionForm`) yönetmek ve yeni simülasyon talepleri (`SimulationRequest`) oluşturmak.
  - Taslak simülasyon senaryolarını (`SimulationDraft`) veritabanında kaydetmek, listelemek, yüklemek ve silmek.
  - Canlı projeksiyon ile senaryo projeksiyonunu karşılaştırarak net faiz kazançları ve kümülatif bakiye farklarını hesaplatmak.
  - Kredi erken kapama etkilerini (`LoanPrepaymentImpact`) ve hedef tutara ulaşma dönemini (`FindTargetReachability`) görselleştirmek.
  - Açık kullanıcı onayı alındığında simülasyon senaryolarını canlı finansal plana uygulamak (`ApplySimulationAsync`).

---

## 🔗 Bağımlılıklar (Depends On)
- `MizanService` — Simülasyon çalıştırma, taslak saklama ve canlıya aktarım işlemleri.
- `SimulatorInsightService` — Simülasyon çıktılarının metinsel özetlere ve faiz kıyaslamalarına dönüştürülmesi.
- `IUserFeedbackService` — Onay diyalogları ve bildirimler.
- [[SimulationWorkflowService]] — Simülasyon senaryolarının orkestrasyonu (MizanService aracılığıyla).
- [[SimulationCalculator]] — Saf domain senaryo simülasyon ve fark hesaplama motoru.
- [[LoanPayoffAdvisor]] — Kredi erken kapatma analizleri ve anapara/faiz hesabı.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `SimulationPage` — What-If senaryo simülasyon XAML görünüm ekranı.
- `MauiProgram` — Dependency Injection konteyner kaydı.
- [[FutureMonthsViewModel]] — Kredi erken kapama tavsiyesinden doğrudan simülasyon parametreleriyle yönlendirme.
- [[DashboardViewModel]] — Ana ekrandan simülasyon modülüne geçiş.

---

## 📜 Bağlı İş Kuralları
- [[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim|BR-SIM-01: Senaryo Koşul İzolasyonu ve Canlıya Aktarım]]
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
