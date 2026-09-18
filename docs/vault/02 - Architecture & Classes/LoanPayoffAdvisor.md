---
title: LoanPayoffAdvisor
type: class
layer: Application
namespace: Mizan.Application.Services
tags:
  - class
  - application
  - advisor
  - loan
---

# `LoanPayoffAdvisor`

Aktif kredilerin erken kapatılması durumunda nakit akışında açık oluşturup oluşturmadığını ve net faiz kazancını simüle ederek akıllı erken kapama tavsiyeleri (LoanPayoffAdvice) üreten danışman servis.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Application
- **Türü:** Application Service / Decision Advisor
- **Sorumluluğu:** 
  - Her bir aktif kredi için gelecekteki taksit günlerinde erken kapama senaryosu (LoanEarlyClosure) simülasyonu çalıştırmak.
  - Krediyi kapatmak için kullanılan paranın nakit akışında ek bir açık faizi maliyeti yaratıp yaratmadığını denetlemek (noNewDeficit).
  - Erken kapama sonucunda 12. dönem sonundaki bakiye farkı ile ufuk sonrası ödenmeyecek taksit kazancını hesaplamak (NetGain).
  - Kullanıcıya Recommended, NoSafeMonth, NotWorthIt, NeedsPrincipal veya AlreadyClosing durumlarında finansal tavsiye üretmek.

---

## 🔗 Bağımlılıklar (Depends On)
- [[FinancialProjectionCalculator]] — Erken kapama öncesi ve sonrası 12 aylık likidite baseline karşılaştırması.
- [[SimulationCalculator]] — Erken kapama senaryo planının oluşturulması.
- [[LoanAmortizationCalculator]] — Kredi anapara ve faiz amortisman analizinin kontrolü.
- [[LoanScheduleCalculator]] — Kredi ödeme planının yeniden oynatılması (Replay).

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `FinancialPlanQueryService` — Plan analizi ve danışman özetlerinin getirilmesi.
- `SimulationViewModel` — Akıllı senaryo ve erken kapama tavsiye panelleri.
- `MizanService` — Ana uygulama servis cephesi.

---

## 📜 Bağlı İş Kuralları
- [[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim|BR-SIM-01: Senaryo Koşul İzolasyonu ve Canlıya Aktarım]]
