---
title: CardControlViewModel
type: class
layer: App
namespace: Mizan.App.ViewModels
tags:
  - class
  - viewmodel
  - app
  - credit-card
---

# `CardControlViewModel`

Kredi kartlarının ekstre durumlarını, asgari/tam ödeme tercihlerini, gelecek dönem harcamalarını ve PDF ekstre içeri aktarma süreçlerini yöneten kontrol paneli ViewModel'i.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** App
- **Türü:** ViewModel / UI Presenter
- **Sorumluluğu:** 
  - Kesilmiş ekstrenin tutar, asgari ödeme ve son ödeme tarihlerini göstermek ve elle/taslak üzerinden düzenlemek.
  - PDF formatındaki banka ekstresini otomatik okuyarak ayrıştırmak (`CreditCardStatementImportWorkflow`).
  - Aktif ve gelecek ekstreler için ödeme stratejisini (Asgari, Tamamı, Özel Tutar) belirlemek ve veritabanına kaydetmek.
  - Kredi kartına ait gelecek taksitli veya tek çekim harcamaları (`FutureCharges`) yönetmek.
  - Önümüzdeki ekstre dönemlerine ait tahmini ekstre bakiyelerini ve ödeme tercihlerini (`UpcomingStatements`) listelemek.

---

## 🔗 Bağımlılıklar (Depends On)
- `MizanService` — Kart, ekstre ve ödeme planlarının kaydedilmesi ve sorgulanması.
- [[CreditCardStatementCalculator]] — Ekstre devreden bakiye, asgari tutar ve carry faiz projeksiyon hesapları.
- `CreditCardStatementImportWorkflow` — PDF ekstre belgesi otomatik okuma ve taslak oluşturma iş akışı.
- [[CreditCardObligationService]] — Kart borçlarının dönem nakit akışına yansıtılması (MizanService aracılığıyla).
- `IUserFeedbackService` — Bildirimler ve kullanıcı diyalogları.
- `INavigationService` — Kart detaylarına ve ilgili modüllere yönlendirme.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `CardControlPage` — Kredi kartı kontrol ve ekstre yönetimi XAML ekranı.
- `MauiProgram` — Dependency Injection konteyner kaydı.
- `CommitmentsPage` — Kart listesinden ilgili kartın kontrol ekranına geçiş.
- [[SimulationViewModel]] — Simülasyon senaryosundan kart kontrol detaylarına geçiş.

---

## 📜 Bağlı İş Kuralları
- [[BR-CARD-01 - Kredi Karti Carry Faizi ve Asgari Tutar Mantigi|BR-CARD-01: Kredi Kartı Carry Faizi ve Asgari Tutar Mantığı]]
