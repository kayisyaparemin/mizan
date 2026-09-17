---
title: PaymentReminderPlanner
type: class
layer: Application
namespace: Mizan.Application.Services
tags:
  - class
  - application
  - notifications
  - reminders
---

# `PaymentReminderPlanner`

Kullanıcının yaklaşan borç ve taahhütlerini kaçırmaması için işletim sistemi bildirim takvimini kuran, rahat / agresif modları ve gece sessiz saatlerini yöneten deterministik zamanlayıcı.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** `Application`
- **Türü:** Static Planning Utility
- **Sorumluluğu:** 
  - 35 günlük bir zaman ufkunda (`HorizonDays = 35`) vadesi gelen tüm ödemeleri taramak.
  - Aynı güne düşen ödemeleri tek bir bildirimde birleştirerek bildirim kirliliğini önlemek.
  - Gece saatlerinde (22:00 - 08:00) bildirim göndermemek; geceye düşen hatırlatmaları sabah 09:00'a ötelemek (`QuietHours`).
  - "Ertele" (Snooze) aksiyonu için 3 saat sonraya (`SnoozeDelay = 3h`) erteleme zamanı üretmek.

---

## ⏰ Bildirim Modları

### 1. Rahat Mod (Relaxed)
- Yalnızca **ödeme günü saat 09:00**'da tek bir bildirim gönderir.

### 2. Agresif Mod (Aggressive)
- **3 gün önce saat 10:00:** "3 gün sonra ödeme var"
- **1 gün önce saat 20:00:** "Yarın ödeme günü"
- **Ödeme günü saat 09:00:** "Bugün ödeme günü"
- **Ödeme günü saat 18:00:** "Ödemeyi unutma, bugün son gün"

---

## 🔗 Bağımlılıklar (Depends On)
- `PaymentReminderPayload` — Bildirimin yerel cihazda taşıyacağı veri paketi.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- `PaymentReminderCardViewModel` — Kart üzerinden anlık ertele / ödendi aksiyonları.
- `PaymentReminderServices` — Cihaz bildirim servisine takvim enjeksiyonu.

---

## 📜 Bağlı İş Kuralları
- [[BR-REMIND-01 - Operasyonel Hatirlatici ve Vade Erteleme|BR-REMIND-01: Operasyonel Hatırlatıcı ve Vade Erteleme]]
