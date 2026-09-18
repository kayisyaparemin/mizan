---
title: DashboardViewModel
type: class
layer: App
namespace: Mizan.App.ViewModels
tags:
  - class
  - viewmodel
  - app
  - dashboard
---

# `DashboardViewModel`

Kullanıcının anlık finansal durumunu, aktif dönem ilerlemesini, gözlem noktası kayıtlarını ve ödeme hatırlatıcılarını sunan ana ekran ViewModel'i.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** App
- **Türü:** ViewModel / UI Presenter
- **Sorumluluğu:** 
  - Aktif dönemin kalan gün ve değişken bütçe ilerleme oranını (`PeriodProgress`) sunmak.
  - Anlık bakiye gözlem kaydını (`ObserveCurrentBalanceAsync`) kullanıcıdan almak ve finansal plan durumunu tazelemek.
  - Geçen dönem kapandığında dönem kapatma mutabakatının uygunluğunu (`PeriodReviewAvailability`) denetlemek ve uyarı vermek.
  - Bekleyen ödeme hatırlatıcı bileşenini (`PaymentReminderCardViewModel`) barındırmak ve yanıt güncellemelerini dinlemek.
  - Kredi kartı ödeme tercihlerinin eksikliğini veya planlanan düzen değişikliklerini kullanıcıya alert kartı olarak sunmak.
  - Sayfalar arası yönlendirme komutlarını (`OpenSimulationAsync`, `OpenPeriodReviewAsync`, `OpenFutureMonthsAsync` vb.) yönetmek.

---

## 🔗 Bağımlılıklar (Depends On)
- `MizanService` — Ana uygulama servis cephesi (dönem ilerlemesi, gözlem ve dashboard özet sorguları).
- `INavigationService` — MAUI sayfaları arası ekran yönlendirme servisi.
- `PaymentReminderCardViewModel` — Dönem içi anlık ödeme hatırlatıcılarının kart bileşeni.
- [[FinancialProjectionService]] — Dönem sonu devreden bakiye ve likidite tahminlerinin getirilmesi (MizanService aracılığıyla).
- [[PeriodWorkflowService]] — Dönem mutabakatı ve gözlem noktası yönetimi (MizanService aracılığıyla).

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `MainPage` — Dashboard ana ekranının XAML görünüm bileşeni.
- `MauiProgram` — Dependency Injection konteyner kaydı.

---

## 📜 Bağlı İş Kuralları
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
- [[BR-RECON-01 - Donem Mutabakati ve Gozlem Noktasi|BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası]]
- [[BR-REMIND-01 - Operasyonel Hatirlatici ve Vade Erteleme|BR-REMIND-01: Operasyonel Hatırlatıcı ve Vade Erteleme]]
