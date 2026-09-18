---
title: SimulationWorkflowService
type: use-case
layer: Application
namespace: Mizan.Application.Services
tags:
  - class
  - use-case
  - application
  - simulation
---

# `SimulationWorkflowService`

Kullanıcının "What-If" senaryo simülasyonlarını çalıştırmasını, geçici taslak olarak kaydetmesini (SimulationDraft) ve onaylanan senaryoları canlı finansal plana aktarmasını sağlayan Use Case orkestratörü.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Application
- **Türü:** Application Service / Workflow Interactor
- **Sorumluluğu:** 
  - Canlı bütçeyi bozmadan birden fazla simülasyon senaryosunu (SimulationRequest) test etmek (SimulateAsync).
  - Oluşturulan senaryo koşullarını isimli taslaklar (SimulationDraft) halinde veritabanında saklamak, listelemek ve silmek.
  - Açık kullanıcı onayı alındığında senaryoyu canlı finansal plana dönüştürüp uygulayarak veritabanına kaydetmek (ApplySimulationAsync).
  - Finansal Yapı ekranından doğrudan borç/harcama ekleme taleplerini doğrulamak ve işlemek (AddRecordFromScenarioAsync).

---

## 🔗 Bağımlılıklar (Depends On)
- [[SimulationCalculator]] — Senaryo planlarını dondurulmuş plana uygulayan ve farkları hesaplayan saf domain motoru.
- `SimulationPersistenceBatchBuilder` — Senaryoların canlı veritabanı değişiklik toplu kaydına (batch) dönüştürülmesi.
- `FinancialPlanQueryService` — Mevcut aktif finansal planın getirilmesi ve değişiklik kaydı.
- `IMizanStore` — Taslakların ve uygulanan batch'lerin kalıcı saklanması.
- `IClock` — Taslak ve işlem zaman damgaları.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `SimulationViewModel` — What-If senaryo simülasyon ekranı.
- `MizanService` — Ana uygulama servis cephesi.

---

## 📜 Bağlı İş Kuralları
- [[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim|BR-SIM-01: Senaryo Koşul İzolasyonu ve Canlıya Aktarım]]
