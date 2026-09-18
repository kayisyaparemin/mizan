---
id: TASK-02
title: Otomatik Regresyon Test Paketi ve CI-CD Kalite Kapısı
type: task
status: planned
priority: high
pillar: 2. Projeksiyon
created: 2026-09-18
tags:
  - task
  - backlog
  - testing
  - regression
  - ci-cd
  - quality-gate
---

# TASK-02: Otomatik Regresyon Test Paketi ve CI-CD Kalite Kapısı

## 🎯 Amacı ve Özeti
Mizan'ın finansal hesaplama motorları (12 aylık projeksiyon, kredi amortismanı, kredi kartı faizi ve senaryo simülasyonları) üzerindeki herhangi bir kod değişikliğinin mevcut iş kurallarını bozmamasını garanti altına almak. Geliştiricinin yerel makinesinde geliştirme hemen sonrasında tek komutla koşabilen; aynı zamanda GitHub üzerinde Pull Request açıldığında otomatik tetiklenerek regresyon testini geçemeyen sürümleri kesin olarak reddeden çift katmanlı bir test ve CI/CD kalite kapısı altyapısı kurmak.

---

## 🔗 Mimari Ağ Bağlantıları (Etki Alanı)

### 📜 Bağlı İş Kuralları ve Mimari Kararlar
- [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi]] — 12 aylık kümülatif nakit dengesi ve finansman açığı hesaplamasının regresyon testleriyle mühürlenmesi.
- [[BR-LOAN-01 - Kredi Amortismani ve Erken Kapama Optimizasyonu]] — Kredi erken kapama ve faiz tenzilatı algoritmalarının regresyon senaryolarında doğrulanması.
- [[BR-CARD-01 - Kredi Karti Carry Faizi ve Asgari Tutar Mantigi]] — Asgari tutar ve carry faizi hesaplayıcısının geriye dönük hatasız çalışmasının teyidi.
- [[BR-CALENDAR-01 - Ay Sonu ve Artik Yil Tarih Sabitleme]] — 28/29 Şubat ve 30/31 çeken aylardaki tarih kayması regresyonlarının önlenmesi.
- [[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim]] — Simülasyon senaryolarının canlı veri modellerini mutasyona uğratmadığının regresyon testiyle denetlenmesi.
- [[ADR-002 - Deterministik Offline-First Projeksiyon Motoru]] — Saf ve deterministik fonksiyon mimarisinin CI/CD ortamında dış bağımlılıksız (sıfır ağ, sıfır harici servis) test edilebilirliği.
- [[ADR-004 - Cift Katmanli Regresyon Test Stratejisi ve CI-CD Kalite Kapisi]] — Yerel hızlı koşu ve GitHub PR ret mekanizmasının mimari kalite kapısı standardı.

### 🛠️ Dokunacağı / Eklenecek Sınıflar
- [[FinancialProjectionCalculator]] — 12 aylık projeksiyon ve likidite açığı regresyon test senaryolarının bağlanması.
- [[LoanAmortizationCalculator]] — Erken kapama, ara ödeme ve amortisman tablosu değişmezlerinin test seti.
- [[CreditCardStatementCalculator]] — Ekstre, gecikme faizi ve carry faizi sınır testleri.
- [[SimulationCalculator]] — Çoklu koşul ve canlı plan izolasyonu regresyon seti.
- [[CashFlowAllocationPlanner]] — Nakit tahsis ve öncelik sıralaması regresyon kontrolü.
- [[CashFlowPeriodCalculator]] — Ay sonu ve artık yıl dönem hesaplama regresyon kontrolü.
- `RegressionTestFixture` — *(Tests - Yeni)* Ortak finansal senaryoları, mock profilleri ve değişmez veri snapshot'larını sağlayan test altyapı sınıfı.
- `ArchitectureInvariantTests` — *(Tests - Yeni)* Clean Architecture bağımlılık yönlerini (Domain -> Dış dünya bağımsızlığı) doğrulayan mimari birim testleri.

---

## 📋 Adım Adım Kodlama Planı

- [ ] 1. Aşama: Test Kategorizasyonu ve Regresyon Paketi Tasarımı (Test Katmanı)
  - `tests/Mizan.Tests` altındaki mevcut birim testleri gözden geçirip regresyon kapsamına girecek testleri `[Trait("Category", "Regression")]` ile işaretlemek.
  - Finansal matematik motorları için sınır değer (boundary/edge case) regresyon testleri yazmak (artık yıl, sıfır bakiye, negatif nakit akışı, limit aşımı).
  - Clean Architecture katman ihlallerini denetleyen `ArchitectureInvariantTests` sınıfını eklemek.

- [ ] 2. Aşama: Yerel Hızlı Koşu Altyapısı (Local Execution)
  - Geliştiricinin geliştirme biter bitmez yerelde tek tıkla/komutla regresyon koşturabilmesi için `scripts/run-regression-tests.ps1` scriptini hazırlamak.
  - Yerel test koşusunun hem özet hem de hata durumunda detaylı rapor üretmesini sağlamak (`dotnet test --filter "Category=Regression"`).

- [ ] 3. Aşama: GitHub Actions CI Kalite Kapısı Entegrasyonu (CI/CD)
  - `.github/workflows/regression-tests.yml` dosyasını oluşturmak.
  - İş akışını `pull_request` ve `push` (main/develop dalları) olaylarına bağlamak.
  - .NET SDK kurulumu, bağımlılıkların geri yüklenmesi (`dotnet restore`), derleme (`dotnet build --no-restore`) ve regresyon test adımlarını (`dotnet test --filter "Category=Regression" --no-build --verbosity normal`) tanımlamak.
  - Testlerden herhangi biri başarısız olduğunda GitHub'ın ilgili commit veya PR'ı "Checks Failed" ile işaretlemesini ve merge engellemesini yapılandırmak.

---

## 🧪 Test & Doğrulama Kriterleri
- **Kasıtlı Hata (Fault Injection) Testi:** [[FinancialProjectionCalculator]] içerisindeki bir faiz/kümülatif toplam formülünde kasıtlı 1 kuruşluk sapma yapıldığında yerel scriptin ve GitHub CI iş akışının anında hata verip derlemeyi/PR'ı kırmızıya düşürdüğü doğrulanmalı.
- **Yerel Hızlı Koşu Doğrulaması:** `scripts/run-regression-tests.ps1` çalıştırıldığında tüm regresyon test paketinin harici bir SQLite veritabanı veya internet bağlantısına ihtiyaç duymadan 10 saniyenin altında yeşil olarak tamamlandığı görülmeli.
- **GitHub PR Blocker Doğrulaması:** GitHub üzerinde açılan bir test PR'ında regresyon test adımı başarısız olduğunda "Merge pull request" butonunun kilitlendiği ve birleştirmenin engellendiği doğrulanmalı.
- **Mimari İhlal Kontrolü:** `Mizan.Domain` katmanına kasıtlı olarak `Mizan.Infrastructure` veya MAUI referansı eklendiğinde `ArchitectureInvariantTests` testinin patladığı ve PR'ı engellediği teyit edilmeli.
