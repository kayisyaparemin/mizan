---
id: ADR-002
title: Deterministik Offline-First Projeksiyon Motoru
status: Accepted
date: 2026-09-18
tags:
  - adr
  - architecture
  - offline-first
  - deterministic
---

# ADR-002: Deterministik Offline-First Projeksiyon Motoru

## 📌 Bağlam (Context)
Finansal veriler kullanıcının en hassas ve gizli verileridir. Ayrıca simülatör ve projeksiyon hesaplamalarının her an, internet olmasa bile anında (milisaniyeler içinde) tepki vermesi gerekmektedir.

## 🎯 Alınan Karar (Decision)
1. **Offline-First Mimari:** Tüm veritabanı SQLite üzerinde, yerel cihazda saklanır. Hiçbir harici bulut sunucu bağımlılığı yoktur.
2. **Deterministik Saf Domain Hesaplayıcıları:** `Domain` katmanındaki tüm hesaplayıcılar (`FinancialProjectionCalculator`, `SimulationCalculator`, `CreditCardStatementCalculator`) saf (stateless, pure) fonksiyonlar olarak tasarlanmıştır. Veritabanına doğrudan gitmezler; planı parametre alıp sonucu dönerler.

## ⚖️ Sonuçlar (Consequences)
- **Pozitif:** Tam veri mahremiyeti ve GDPR/KVKK uyumu.
- **Pozitif:** Sıfır gecikme (zero latency); simülasyon kaydırıcıları oynatıldığında anında 12 aylık hesaplama yenilenir.
- **Pozitif:** Yüksek test edilebilirlik; tüm hesaplayıcılar saf birim testleri (unit tests) ile %100 kapsanabilir.
