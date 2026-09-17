---
title: MandatoryPaymentCalculator
type: class
layer: Domain
namespace: Mizan.Domain.Calculations
tags:
  - class
  - domain
  - mandatory-payment
  - obligations
---

# `MandatoryPaymentCalculator`

Krediler (ve erken ödeme olayları), geçici ödeme planları ve kredi kartı ekstre ödemelerini tek bir zorunlu yükümlülük listesinde (`ObligationItem`) birleştiren ve türe göre özetleyen Domain orkestratörü.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Domain
- **Türü:** Saf Hesaplayıcı (Zorunlu Yükümlülük Toplayıcı & Özetleyici)
- **Sorumluluğu:**
  - Aktif kredileri ve bunlara ait erken/ara ödemeleri (`LoanPrepayment`) `LoanPaymentScheduleBuilder.Replay` ile işleyerek `ObligationItem` listesine dönüştürmek.
  - Geçici ödeme planlarını (`TemporaryPaymentPlan`) ve kredi kartı ödemelerini ekleyerek tüm zorunlu ödemeleri vade tarihine (`DueDate`) göre sıralamak.
  - Atanmış ödemeleri türlerine göre (Kredi, Kredi Kartı, Taksit, Geçici Ödeme vb.) gruplayıp `MandatoryPaymentSummary` özetini hesaplamak.

---

## 🔗 Bağımlılıklar (Depends On)
- [[LoanPaymentScheduleBuilder]] — Kredi ödeme takvimini ve erken ödeme hareketlerini simüle eder.
- [[ScheduledPaymentCalculator]] — Planlı geçici ödemeleri `ObligationItem` formatına çevirir.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- [[FinancialProjectionCalculator]] — 12 aylık projeksiyonda tüm zorunlu ödemeleri toplamak ve dönemlere atamak için kullanır.
- [[MauiProgram]] — Dependency Injection container'ına Singleton olarak kaydeder.

---

## 📜 Bağlı İş Kuralları
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
- [[BR-REMIND-01 - Operasyonel Hatirlatici ve Vade Erteleme|BR-REMIND-01: Operasyonel Hatırlatıcı ve Vade Erteleme]]
- [[BR-RECON-01 - Donem Mutabakati ve Gozlem Noktasi|BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası]]
