---
id: ADR-004
title: Çift Katmanlı Regresyon Test Stratejisi ve CI-CD Kalite Kapısı
status: Accepted
date: 2026-09-18
tags:
  - adr
  - architecture
  - testing
  - regression
  - ci-cd
  - quality-gate
---

# ADR-004: Çift Katmanlı Regresyon Test Stratejisi ve CI-CD Kalite Kapısı

## 📌 Bağlam (Context)
Mizan; kredi amortismanı, carry faizi, 12 aylık kümülatif likidite projeksiyonu ve kriz simülasyonları gibi yüksek hassasiyet gerektiren finansal matematik motorları üzerinde çalışır. Mevcut durumda birim testler (`tests/Mizan.Tests`) bulunmakla birlikte:
1. Geliştiricinin yerel ortamında geliştirme biter bitmez otomatik çalıştırabileceği bir regresyon standardı ve scripti eksiktir.
2. Kod tabanına yapılan katkılarda veya Pull Request'lerde, regresyona neden olan bir mantık hatasını engelleyen otomatik bir uzaktan kalite kapısı (GitHub Actions CI) bulunmamaktadır.
3. Finansal matematik motorlarında oluşacak 1 kuruşluk veya 1 günlük sapma (off-by-one/rounding) dahi kullanıcının güvenini ve uygulamanın güvenilirliğini doğrudan zedeler.

## 🎯 Alınan Karar (Decision)
**Çift Katmanlı (Local + GitHub Actions) Otomasyon Kalite Kapısı benimsenecektir:**

1. **Katman 1 (Geliştirici Yerel Seviyesi - Fast Local Gate):**
   - Test projesinde `[Trait("Category", "Regression")]` ve `[Trait("Category", "DomainMath")]` etiketleme standardı kurulur.
   - Geliştiricinin commit öncesi tek komutla (`./scripts/run-regression-tests.ps1` veya `dotnet test --filter "Category=Regression"`) tüm kritik regresyon testlerini birkaç saniyede koşabilmesi sağlanır.

2. **Katman 2 (GitHub Actions CI - Pull Request Blocker):**
   - `.github/workflows/regression-tests.yml` iş akışı devreye alınır.
   - `main` ve `develop` dallarına açılan her Pull Request ve doğrudan push işleminde otomatik tetiklenir.
   - İş akışı; mimari bağımlılık kurallarını, SQLite göç/şema geriye uyumluluk testlerini ve saf domain hesaplayıcılarını baştan sona çalıştırır.
   - **Kritik Kural:** Herhangi bir regresyon testi kırmızıya düşerse (fail ederse), GitHub PR birleştirmesini (merge) kesin olarak engeller (Branch Protection Rule).

3. **Regresyon Test Kapsamının Odak Alanları:**
   - [[FinancialProjectionCalculator]] — 12 aylık nakit açığı ve kümülatif likidite değişmezleri ([[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi]]).
   - [[LoanAmortizationCalculator]] — Erken ödeme, taksit erteleme ve faiz yeniden hesaplama ([[BR-LOAN-01 - Kredi Amortismani ve Erken Kapama Optimizasyonu]]).
   - [[CreditCardStatementCalculator]] — Asgari ödeme, akdi/gecikme carry faizi ve dönem atlatma ([[BR-CARD-01 - Kredi Karti Carry Faizi ve Asgari Tutar Mantigi]]).
   - [[CashFlowAllocationPlanner]] — Nakit tahsis öncelik sıralaması ([[BR-ALLOC-01 - Nakit Akisi Tahsis Stratejisi ve Gecis Yonetimi]]).
   - [[SimulationCalculator]] — Canlı bütçeyi bozmayan senaryo izolasyonu ([[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim]]).
   - [[CashFlowPeriodCalculator]] — Ay sonu, artık yıl ve artık gün sınır değerleri ([[BR-CALENDAR-01 - Ay Sonu ve Artik Yil Tarih Sabitleme]]).
   - Clean Architecture Sınır Testleri — `Domain` katmanının dış dünyaya bağımsızlığı ve deterministik yapısının bozulmaması ([[ADR-002 - Deterministik Offline-First Projeksiyon Motoru]]).

## ⚖️ Sonuçlar (Consequences)
- **Pozitif (Sıfır Matematiksel Hata Kaçağı):** Kritik finansal algoritmalardaki istenmeyen değişiklikler merge edilmeden önce yakalanır.
- **Pozitif (Yerel ve Uzak Senkronizasyon):** Geliştirici yerelde doğruladığı testlerin birebir aynısının GitHub sunucularında koştuğundan emin olur.
- **Pozitif (Sürdürülebilirlik):** Yeni özellik ekleyen bir geliştirici mevcut hesaplayıcıları bozarsa PR otomatik olarak reddedilir.
- **Nötr (CI Süresi):** Her PR kontrolü ~1-2 dakikalık GitHub runner süresi gerektirir; deterministik ve yerel in-memory testler tasarlandığı için süre minimal kalır.
