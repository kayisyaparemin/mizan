---
id: BR-AUTH-01
title: Bulutsuz Finansal Veri ve Çevrimdışı Lisanslama
domain: Kimlik ve Abonelik Yönetimi
status: Active
pillar: 5. Operasyonel Takip ve Hatırlatıcı
tags:
  - business-rule
  - auth
  - subscription
  - offline-first
---

# BR-AUTH-01: Bulutsuz Finansal Veri ve Çevrimdışı Lisanslama

## 🎯 Kuralın Amacı ve Özü
Google ile oturum açılmış olsa dahi, hiçbir finansal verinin uzak sunuculara aktarılmamasını; internet bağlantısı olmasa bile abonelik haklarının yerel cihazda doğrulanabilmesini garanti altına almaktır.

---

## 📐 Mimari ve İşleyiş Kuralları

```
[Kullanıcı Arayüzü]
       │
       ├── Google Sign-In ───────────> [Google Auth API] (Sadece Token Doğrulama)
       │                                       │
       │                                       ▼
       ├── Abonelik Satın Alma ───────> [Google Play Billing]
       │                                       │
       │                                       ▼
       │                              [Yerel Lisans Tokeni (JWT)]
       │                              (30 Gün Çevrimdışı Geçerlilik)
       │
       └── Bütçe / Kredi / Nakit ────> [Yerel SQLite Veritabanı]
           (Asla İnternete Çıkmaz!)
```

### 1. Kimlik ve Veri Ayrımı Kuralı (Data Decoupling)
- Google hesabı yalnızca `UserId` ve `Email` bilgisini döndürür.
- Bu bilgi, yereldeki kullanıcı profiliyle (`UserProfile`) eşleştirilir.
- **Kesin Kural:** Ağ isteklerinde (HTTP payload) asla `FinancialPlan`, `CreditCard`, `Salary` veya `Observation` nesneleri taşınamaz!

### 2. Çevrimdışı Lisans Süresi (Offline Grace Period)
- Kullanıcı internete bağlıyken abonelik satın aldığında veya doğrulandığında cihazda şifreli/imzalı bir lisans kaydı oluşturulur:
  $$\text{Son Doğrulama Tarihi} + 30 \text{ Gün} = \text{Çevrimdışı Lisans Bitişi}$$
- Kullanıcı 30 gün boyunca hiç internete bağlanmasa bile Pro özellikleri kısıtlanamaz.
- 30 gün dolduğunda kullanıcı internete bağlandığı ilk anda sessizce (arka planda) lisans yenilenir.

### 3. İnternete Çıkış İzni Kriteri
Sistem yalnızca şu 3 durumda internete istek atabilir:
1. Kullanıcı bilerek "Google ile Giriş Yap" butonuna bastığında.
2. Kullanıcı "Abonelik Satın Al / Geri Yükle" dediğinde.
3. 30 günlük çevrimdışı yetki süresi dolduğunda arka planda token kontrolü yaparken.

---

## 🧩 İlgili Mimari Sınıflar & Dosyalar
- Karar Belgesi: [[ADR-003 - Offline-First Kimlik ve Finansal Veri Izolasyonu|ADR-003: Kimlik ve Veri İzolasyonu]]
- Görev Kartı: [[TASK-01 - Google Auth ve Offline-First Abonelik Altyapisi|TASK-01: Google Auth & Abonelik]]
- Servisler: `IAuthenticationService`, `ISubscriptionService`, `ProfileService`
- UI: `OnboardingViewModel`, `SettingsViewModel`
