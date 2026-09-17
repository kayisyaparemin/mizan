---
title: LoanAmortizationCalculator
type: class
layer: Domain
namespace: Mizan.Domain.Calculations
tags:
  - class
  - domain
  - loan
  - amortization
  - banking-interest
---

# `LoanAmortizationCalculator`

Kredilerin annüite faiz oranını girdi olarak almak yerine bisection (ikiye bölme) yöntemiyle türeten, kalan anapara, taksit tutarı, erken kapatma tutarı ve erken ödeme tazminatı hesaplamalarını yapan matematiksel Domain motoru.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Domain
- **Türü:** Saf Hesaplayıcı (Annüite, Erken Kapatma ve Faiz Amortisman Hesaplayıcı)
- **Sorumluluğu:**
  - Kullanıcının girdiği kalan anapara veya bankadan alınan tarihli kapatma teklifinden (`EarlyClosureAmount`) aylık efektif faiz oranını (`MonthlyRate`) annüite denklemi üzerinden bisection (0, 1] kök bulma algoritması ile hesaplamak.
  - Efektif faiz oranının makul sınırın üstünde (`MaxPlausibleMonthlyRate = %8`) olup olmadığını denetlemek (`ImplausibleRate`).
  - Ödenen taksit sayısına göre kalan anaparayı (`PrincipalAfter`), faiz payını ve tek taksit sonrası anapara değişimini (`PrincipalAfterPayment`) hesaplamak.
  - Belirli bir tarihteki erken kapatma bedelini, işleyen faizi ve 6502 sayılı Kanun'a göre konut/tüketici kredisi erken ödeme tazminatını (`PrepaymentFeeRate`) hesaplayarak `LoanPayoffQuote` üretmek.

---

## 🔗 Bağımlılıklar (Depends On)
- [[LoanScheduleCalculator]] — Kredinin taksit tarihlerini almak için kullanır.
- [[CalendarRules]] — Son ödeme tarihinden önceki vade tarihini (`PreviousDueDate`) bulmak için kullanır.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- [[SimulationCalculator]] — Kredi erken kapatma ve borç yapılandırma senaryolarının faiz avantajını hesaplamak için çağırır.
- [[LoanPaymentScheduleBuilder]] — Erken ve ara ödemeler sonrası güncellenmiş taksit planını yeniden inşa etmek için çağırır.
- [[LoanPayoffAdvisor]] — Kullanıcıya hangi krediyi kapatmasının daha avantajlı olduğunu öneren analiz servisi.
- [[LoanPayoffService]] — Erken kapatma tekliflerini ve anapara düşümlerini yöneten Application servisi.
- [[FinancialInstrumentReconciliationService]] — Dönem mutabakatında ödenen taksit sonrası kalan anaparayı güncellemek için çağırır.
- [[CommitmentsViewModel]] — Kullanıcının kredilerini ve erken kapatma simülasyonlarını görüntülediği UI katmanı.
- [[MauiProgram]] — Dependency Injection container'ına Singleton olarak kaydeder.

---

## 📜 Bağlı İş Kuralları
- [[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim|BR-SIM-01: Senaryo Koşul İzolasyonu ve Canlıya Aktarım]]
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
