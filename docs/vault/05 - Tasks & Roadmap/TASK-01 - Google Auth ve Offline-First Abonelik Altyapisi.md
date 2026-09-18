---
id: TASK-01
title: Google Auth ve Offline-First Abonelik Altyapısı
type: task
status: planned
priority: high
pillar: 5. Operasyonel Takip ve Hatırlatıcı
created: 2026-09-18
tags:
  - task
  - backlog
  - auth
  - subscription
  - offline-first
---

# TASK-01: Google Auth ve Offline-First Abonelik Altyapısı

## 🎯 Amacı ve Özeti
Kullanıcının uygulamayı ilk açtığında "Google Hesabı ile Devam Et" seçeneğiyle kimlik oluşturmasını; ancak **finansal verilerin kesinlikle cihazda (SQLite) kalmasını**, internetin yalnızca kimlik doğrulama ve Google Play abonelik kontrolü için kullanılmasını sağlamak.

---

## 🔗 Mimari Ağ Bağlantıları (Etki Alanı)

### 📜 Bağlı İş Kuralları ve Mimari Kararlar
- [[BR-AUTH-01 - Bulutsuz Finansal Veri ve Cevrimdisi Lisanslama|BR-AUTH-01: Bulutsuz Veri ve Çevrimdışı Lisanslama]]
- [[ADR-003 - Offline-First Kimlik ve Finansal Veri Izolasyonu|ADR-003: Kimlik ve Veri İzolasyonu]]
- [[ADR-002 - Deterministik Offline-First Projeksiyon Motoru|ADR-002: Offline-First Motor]]

### 🛠️ Dokunacağı / Eklenecek Sınıflar
- `IAuthenticationService` *(Application - Yeni)* — Google OAuth akışı ve Token yönetimi.
- `ISubscriptionService` *(Application - Yeni)* — Google Play Billing ve Pro lisans durumu kontrolü.
- `ProfileService` *(Application - Mevcut)* — Google hesabı ile yerel `UserProfile` eşleşmesi.
- `OnboardingViewModel` *(UI - Mevcut)* — "Google ile Giriş Yap" ve "Giriş Yapmadan Devam Et" butonları.
- `SettingsViewModel` *(UI - Mevcut)* — Abonelik durumu, hesap bilgisi ve çıkış yap aksiyonları.

---

## 📋 Adım Adım Kodlama ve Uygulama Planı

### Aşama 1: Soyutlama ve Sözleşmeler (Application Katmanı)
- [ ] `Mizan.Application/Abstractions/IAuthenticationService.cs` arayüzünü tanımla (`SignInWithGoogleAsync`, `SignOutAsync`, `CurrentUserId`).
- [ ] `Mizan.Application/Abstractions/ISubscriptionService.cs` arayüzünü tanımla (`GetSubscriptionStatusAsync`, `PurchaseProAsync`, `RestorePurchasesAsync`).
- [ ] Lisans durumunu temsil eden `SubscriptionEntitlement` modelini oluştur (Status: Free, ProActive, GracePeriod).

### Aşama 2: Çevrimdışı Lisans Doğrulama Motoru (Domain / Application)
- [ ] Yerel cihazda imzalı lisans dosyasını şifreli saklayan `LocalLicenseStore` sınıfını yaz.
- [ ] İnternet yoksa 30 günlük `OfflineGracePeriod` kuralını işlet (`BR-AUTH-01`).

### Aşama 3: Platform Bağımlılıkları (Infrastructure Katmanı)
- [ ] Android için Google Play Billing Client entegrasyonunu yap (`#if ANDROID`).
- [ ] Android Web Authenticator / Google Sign-In intent handler'ı yaz.

### Aşama 4: UI & Onboarding Entegrasyonu (Presentation Katmanı)
- [ ] `OnboardingPage.xaml` ve `OnboardingViewModel` içine Google Sign-In butonunu ekle.
- [ ] Kullanıcı Google ile giriş yapmasa bile yerel profil ile devam edebilmeli ("Şimdilik Atla / Çevrimdışı Devam Et").
- [ ] `SettingsPage.xaml` içine "Aboneliği Yönet / Pro'ya Geç" kartı ekle.

---

## 🧪 Test & Doğrulama Kriterleri
1. **İnternetsiz Açılış Testi:** Cihaz uçak modundayken uygulama sorunsuz açılmalı; yerel veritabanı okunup yazılabilmeli.
2. **Sıfır Veri Gönderimi Testi:** Network sniffer ile incelendiğinde Google API isteklerinde hiçbir finansal modelin (`Salary`, `FinancialPlan` vb.) gitmediği doğrulanmalı.
3. **Grace Period Testi:** Pro lisanslı kullanıcı 7 gün internetsiz kaldığında lisansın düşmediği test edilmeli.
