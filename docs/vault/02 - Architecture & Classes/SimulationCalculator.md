---
title: SimulationCalculator
type: class
layer: Domain
namespace: Mizan.Domain.Calculations
tags:
  - class
  - domain
  - simulation
  - what-if
---

# `SimulationCalculator`

Kullanıcının canlı bütçesine dokunmadan, "What-If" senaryoları kurgulamasını sağlayan simülasyon hesaplayıcısı.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** `Domain`
- **Türü:** Saf Hesaplayıcı (Partial Class: Core, Instruments, PlanBuilder, Validation)
- **Sorumluluğu:** 
  - Mevcut finansal planı (`currentPlan`) referans (baseline) olarak almak.
  - Kullanıcının simülasyon isteklerini (`SimulationRequest`) — örneğin yeni kredi çekme, erken borç kapatma, ek gelir/harcama ekleme — izole bir geçici plana (`scenarioPlan`) dönüştürmek.
  - Her iki planı da [[FinancialProjectionCalculator]] üzerinden 12 ay boyunca koşturarak dönem dönem farkları (`Delta`) ve kümülatif likidite etkisini üretmek.

---

## 🔗 Bağımlılıklar (Depends On)
- [[FinancialProjectionCalculator]] — Hem baseline hem scenario projeksiyonunu hesaplamak için.
- `InstallmentScheduleCalculator` — Simüle edilen yeni taksitli harcamalar için.
- `LoanPaymentScheduleBuilder` & `LoanAmortizationCalculator` — Simüle edilen yeni krediler ve erken kapatma faiz tasarrufu için.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `SimulationWorkflowService` — Senaryo durumunu yöneten ve "Planı Uygula" (Apply Plan) aksiyonunu yürüten servis.
- `SimulatorInsightService` — Simülasyonun toplam maliyet/tasarruf içgörülerini çıkaran servis.
- `SimulationViewModel` — Simülasyon ekranı.

---

## 📜 Bağlı İş Kuralları
- [[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim|BR-SIM-01: Senaryo Koşul İzolasyonu ve Canlıya Aktarım]]

---

## 🔬 Karşılaştırma Modeli (Zip & Delta)

```csharp
var baseline = projectionCalculator.Calculate(currentPlan, asOf, periodCount);
var scenarioPlan = BuildScenarioPlan(currentPlan, requests);
var scenario = projectionCalculator.Calculate(scenarioPlan, asOf, periodCount);

// Dönem dönem delta hesaplanır:
// Net Fark = Senaryo Serbest Bütçe - Baz Serbest Bütçe
```
