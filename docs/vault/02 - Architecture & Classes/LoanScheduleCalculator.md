---
title: LoanScheduleCalculator
type: class
layer: Domain
namespace: Mizan.Domain.Calculations
tags:
  - class
  - domain
  - loan
  - schedule
---

# `LoanScheduleCalculator`

Bir kredinin kalan taksit sayısına ve ödeme gününe göre gelecekteki tüm ödeme tarihlerini takvim kurallarına uygun olarak türeten saf ödeme takvimi hesaplayıcısı.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Domain
- **Türü:** Saf Hesaplayıcı (Ödeme Takvimi Tarih Hesaplayıcısı)
- **Sorumluluğu:**
  - Kredinin ödeme gününün (`PaymentDay`) geçerliliğini doğrulayarak (`CalendarRules.ValidateDay`), `NextPaymentDate` tarihinden itibaren kalan taksit sayısı (`RemainingInstallmentCount`) kadar tüm ödeme tarihlerini dizilim olarak üretmek.
  - Ay sonu gün taşmalarını (örneğin 31 Ocak -> 28/29 Şubat) `CalendarRules.AddMonthsKeepingDay` mantığıyla yönetmek.

---

## 🔗 Bağımlılıklar (Depends On)
- [[CalendarRules]] — Vade günü koruyarak ay ekleme ve gün doğrulama kuralları.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- [[LoanAmortizationCalculator]] — Erken kapatma gününe kadar kaç taksit ödendiğini saptamak için taksit tarihlerini alır.
- [[LoanPaymentScheduleBuilder]] — Taksit planını rekonstrüksiyon mantığıyla tekrar oluşturmak için çağırır.
- [[SimulationCalculator]] — Senaryolarda türetilen yeni kredilerin takvimini oluşturmak için kullanır.
- [[MauiProgram]] — Dependency Injection container'ına Singleton olarak kaydeder.

---

## 📜 Bağlı İş Kuralları
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
