---
id: BR-REMIND-01
title: Operasyonel Hatırlatıcı ve Vade Erteleme
domain: Bildirimler ve Operasyonel Disiplin
status: Active
pillar: 5. Operasyonel Takip ve Hatırlatıcı
tags:
  - business-rule
  - notifications
  - reminders
---

# BR-REMIND-01: Operasyonel Hatırlatıcı ve Vade Erteleme

## 🎯 Kuralın Amacı ve Özü
Kullanıcının kritik ödeme tarihlerini kaçırmasını önlerken, aynı güne düşen çoklu ödemelerle bildirim spam'i yapmamak ve gece saatlerinde rahatsız etmemektir.

---

## 📐 Kurallar ve Zaman Pencereleri

### 1. 35 Günlük Ufuk (Horizon)
- Hatırlatıcı takvimi her uygulama açılışında ileriye dönük 35 gün (`HorizonDays = 35`) için yeniden kurulur.

### 2. Gece Sessiz Saatleri (Quiet Hours)
- **22:00 ile 08:00** arasında hiçbir bildirim kurulmaz veya çalmaz.
- Geceye düşen herhangi bir bildirim veya erteleme zamanı otomatik olarak **ertesi sabah 09:00**'a ötelenir.

### 3. Çoklu Ödeme Konsolidasyonu
- Aynı güne birden fazla ödeme düştüğünde her biri için ayrı bildirim atılmaz.
- Bildirim başlığı ve içeriğinde ilk 3 ödemenin adı birleştirilir (örn: *"Bugün 3 ödeme var: Kira, Kredi Kartı, Araç Kredisi"*).

### 4. Ertele (Snooze) Mantığı
- Kullanıcı bildirim üzerinden veya karttan "Ertele" aksiyonuna bastığında hatırlatma **3 saat sonraya** ötelenir (`SnoozeDelay = 3h`). Gece saatine denk gelirse sabah 09:00 kuralı devreye girer.

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[PaymentReminderPlanner]]
- UI: `PaymentReminderCardViewModel`
