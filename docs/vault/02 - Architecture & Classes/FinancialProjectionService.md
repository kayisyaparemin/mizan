---
title: FinancialProjectionService
type: use-case
layer: Application
namespace: Mizan.Application.Services
tags:
  - class
  - use-case
  - application
  - projection
---

# `FinancialProjectionService`

FinancialProjectionCalculator motorunu çağırarak 12 aylık kümülatif nakit akışı projeksiyonunu ve Dashboard anlık görüntüsünü (DashboardSnapshot) hazırlayan Application Use Case servisi.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Application
- **Türü:** Application Service / Use Case
- **Sorumluluğu:** 
  - Aktif finansal plan (FinancialPlan) ve baz tarih (asOf) üzerinden dashboard özet durumunu (DashboardSnapshot) inşa etmek.
  - Vadesi yakın zorunlu ödemeleri (upcoming), maaş öncesi yükümlülükleri (preFirst) ve en dar boğazdaki dönemi (tightest) saptamak.
  - İleriye dönük istenen dönem sayısı kadar (periodCount) projeksiyon listesini (CashFlowPeriodProjection) üretmek.
  - Kart ve bütçe faiz maliyet toplamlarını dashboard anlık durumuna taşımak.

---

## 🔗 Bağımlılıklar (Depends On)
- [[FinancialProjectionCalculator]] — 12 aylık kümülatif projeksiyon ve dönem hesaplamalarını gerçekleştirir.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `FinancialPlanQueryService` — Aktif plan sorguları ve projeksiyon verilerini sunmak için.
- `MizanService` — Ana uygulama servis cephesi (Facade).
- `DashboardViewModel` — Ana ekran dashboard görünümü.
- `FutureMonthsViewModel` — Gelecek aylar kümülatif projeksiyon görünümü.

---

## 📜 Bağlı İş Kuralları
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]]
