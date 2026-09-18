---
title: CreditCardObligationService
type: use-case
layer: Application
namespace: Mizan.Application.Services
tags:
  - class
  - use-case
  - application
  - credit-card
---

# `CreditCardObligationService`

Kredi kartı tanımları, ekstre verileri, ödeme tercihi geçmişi ve dönemsel kart ödeme planlarının yönetilmesini sağlayan Application Use Case servisi.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** Application
- **Türü:** Application Service / Obligation Interactor
- **Sorumluluğu:** 
  - Kredi kartı ekleme, güncelleme ve silme işlemlerini doğrulamak (ObligationValidation) ve kaydetmek.
  - Kart ekstrelerini (CreditCardStatement) ve o ekstre için seçilen ödeme modunu (Asgari/Tamamı/Özel) sisteme işlemek.
  - Kullanıcının zaman içindeki kart ödeme tercihlerini geçmiş bazlı takip etmek ve CreditCardPaymentPreferenceResolver üzerinden saklamak.
  - Kart planı değişikliklerinde aktif projeksiyon planının güncellenmesini sağlamak.

---

## 🔗 Bağımlılıklar (Depends On)
- [[CreditCardStatementCalculator]] — Kredi kartı ekstre ve faiz hesaplamaları kuralları.
- `CreditCardPaymentPreferenceResolver` — Kart ödeme tercih geçmişinin zaman ekseninde çözümlenmesi.
- `ObligationValidation` — Kart ve ödeme ayarlarının domain doğrulama kuralları.
- `FinancialPlanQueryService` — Plan değişikliklerinin tetiklenmesi.
- `IMizanStore` — Kredi kartı verilerinin kalıcı saklanması.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `ObligationManagementService` — Taahhüt ve yükümlülük yönetimi orkestratörü.
- `CardControlViewModel` — Kredi kartı kontrol ve ekstre yönetim ekranı.
- `MizanService` — Ana uygulama servis cephesi.

---

## 📜 Bağlı İş Kuralları
- [[BR-CARD-01 - Kredi Karti Carry Faizi ve Asgari Tutar Mantigi|BR-CARD-01: Kredi Kartı Carry Faizi ve Asgari Tutar Mantığı]]
