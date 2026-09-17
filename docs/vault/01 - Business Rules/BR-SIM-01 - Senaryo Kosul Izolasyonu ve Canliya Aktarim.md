---
id: BR-SIM-01
title: Senaryo Koşul İzolasyonu ve Canlıya Aktarım
domain: Simülatör ve Senaryo Yönetimi
status: Active
pillar: 3. Akıllı Senaryo Simülatörü
tags:
  - business-rule
  - simulation
  - sandbox
---

# BR-SIM-01: Senaryo Koşul İzolasyonu ve Canlıya Aktarım

## 🎯 Kuralın Amacı ve Özü
Kullanıcının canlı finansal verilerini ve dondurulmuş planını riske atmadan, bir sandbox ortamında "What-If" denemeleri yapmasını; senaryodaki koşulları (yeni kredi, erken kapama, ek gelir) tek tek açıp kapatarak test etmesini ve beğenirse tek tıkla canlıya aktarmasını ("Planı Uygula") sağlamak.

---

## 📐 Simülasyon Mekanizması

```
Canlı Plan (Baseline) ──────┐
                             ├──> [Zip & Delta Karşılaştırma] ──> Dönem Dönem Net Fark
Senaryo Planı (Sandbox) ────┘
  ├─ Koşul 1 (Açık)
  ├─ Koşul 2 (Kapalı)
  └─ Koşul 3 (Açık)
```

### 1. İzole Kopya İlkesi (Pure Sandbox)
- Canlı finansal plan kopyalanır (`BuildScenarioPlan`).
- Her simülasyon koşulu (`SimulationRequest`), yalnızca bu kopyanın üzerine bindirilir.
- Canlı SQLite veritabanına kullanıcı "Planı Uygula" diyene kadar hiçbir yazma yapılmaz.

### 2. Koşul Toggle Özgürlüğü
- Kullanıcı oluşturduğu taslaktaki bir koşulu (örn. 50.000 TL ihtiyaç kredisi) pasife aldığında, tüm 12 aylık projeksiyon anında yeniden hesaplanır.

### 3. "Planı Uygula" (Apply Plan) Atomikliği
- Kullanıcı simülasyonu onayladığında:
  - Eklenen yeni krediler veya planlı harcamalar gerçek veri tablolarına yazılır.
  - Aktif projeksiyon yenilenir.
  - Taslak simülasyon arşive kaldırılır.

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[SimulationCalculator]]
- Application: `SimulationWorkflowService`, `SimulatorInsightService`
- UI: `SimulationViewModel`, `ScenarioConditionFormView`
