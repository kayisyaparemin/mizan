---
title: FutureMonthsViewModel
type: class
layer: App
namespace: Mizan.App.ViewModels
tags:
  - class
  - viewmodel
  - app
  - projection
---

# `FutureMonthsViewModel`

Önümüzdeki 12 döneme ait kümülatif likidite projeksiyonunu, toplam faiz yüklerini ve akıllı kredi erken kapama tavsiyelerini sunan ViewModel.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** App
- **Türü:** ViewModel / UI Presenter
- **Sorumluluğu:** 
  - 12 dönemlik kümülatif nakit akışı ve devreden bakiye projeksiyonlarını (`ProjectionLine`) listelemek.
  - Toplam kredi kartı carry faizi ve finansman açığı faiz yükünü özetlemek (`ProjectionInterestSummary`).
  - Aktif dönem içi bakiye sapmalarını tespit ederek kullanıcıyı bilgilendirmek (`RefreshDeviationNoticeAsync`).
  - Akıllı kredi kapatma tavsiyelerini (`GetLoanPayoffAdviceAsync`) arka planda hesaplayıp kullanıcıya sunmak.
  - Belirli bir hedef birikim tutarına hangi dönemde ulaşılabileceğini sorgulamak (`FindTargetReachabilityAsync`).

---

## 🔗 Bağımlılıklar (Depends On)
- `MizanService` — Gelecek dönem projeksiyonlarının ve kredi erken kapama tavsiyelerinin sorgulanması.
- `INavigationService` — Dönem detaylarına, taahhütlere ve simülasyon modülüne yönlendirme.
- [[FinancialProjectionService]] — 12 dönemlik kümülatif nakit akışı ve faiz motoru (MizanService aracılığıyla).
- [[FinancialProjectionCalculator]] — Saf domain projeksiyon ve likidite motoru.
- [[LoanPayoffAdvisor]] — Kredi erken kapatma kârlılık ve dönem analizleri.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `FutureMonthsPage` — Gelecek 12 ay kümülatif görünüm XAML ekranı.
- `MauiProgram` — Dependency Injection konteyner kaydı.
- [[DashboardViewModel]] — Ana ekrandan gelecek aylar görünümüne geçiş.

---

## 📜 Bağlı İş Kuralları
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
