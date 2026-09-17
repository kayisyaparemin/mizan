---
title: IncomeProjectionCalculator
type: class
layer: Domain
namespace: Mizan.Domain.Calculations
tags:
  - class
  - domain
  - income
  - projection
---

# `IncomeProjectionCalculator`

Maaş ve tek seferlik diğer gelir akışlarını `CashFlowPeriod` bazında eşleştirerek dönemsel gelir özetini deterministik olarak hesaplayan saf Domain hizmeti.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Domain
- **Türü:** Saf Hesaplayıcı (Stateless Domain Calculator)
- **Sorumluluğu:**
  - `IncomeResolver` aracılığıyla döneme denk gelen maaş tutarını (`SalaryScheduleEntry`) tespit etmek.
  - Dönem tarih aralığına (`period.Contains`) giren tek seferlik gelirleri (`OneTimeIncome`) filtrelemek ve tarihe göre sıralamak.
  - Projeksiyon ufku başlangıcı ile ilk maaş tarihi arasındaki boşlukta (`[prePeriodIncomeStart, period.Start)`) kalan tek seferlik gelirlerin kaybolmasını engellemek için ön-dönem pencere kontrolü (`IsWithinPrePeriodWindow`) yapmak.
  - Maaş, diğer gelirler ve toplam geliri `IncomeProjectionSummary` kaydı olarak sunmak.

---

## 🔗 Bağımlılıklar (Depends On)
- [[CashFlowPeriodCalculator]] — Projeksiyon dönem sınırlarını (`CashFlowPeriod`) tanımlar.
- [[IncomeResolver]] — Belirli bir tarih için geçerli olan maaş tanımını çözer.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- [[FinancialProjectionCalculator]] — Her dönemin toplam gelirini hesaplamak ve açılış bakiyesine eklemek için çağırır.
- [[MauiProgram]] — Dependency Injection container'ına Singleton olarak kaydeder.

---

## 📜 Bağlı İş Kuralları
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
