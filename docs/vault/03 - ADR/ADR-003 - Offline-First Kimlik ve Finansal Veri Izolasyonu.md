---
id: ADR-003
title: Offline-First Kimlik ve Finansal Veri İzolasyonu
status: Accepted
date: 2026-09-18
tags:
  - adr
  - architecture
  - security
  - privacy
  - offline-first
  - auth
---

# ADR-003: Offline-First Kimlik ve Finansal Veri İzolasyonu

## 📌 Bağlam (Context)
Uygulamada abonelik (In-App Purchase), kimlik doğrulama (Google Sign-In) ve lisans kontrolü yapılması gerekmektedir. Ancak Mizan'ın temel varoluş felsefesi **kullanıcının en mahrem finansal verilerini (gelir, kredi, borç, bakiye) asla bir uzak sunucuya göndermemektir**.

Birçok finans uygulaması üyelik açıldığında tüm veritabanını buluta yedeklemeye çalışır. Bu durum:
1. Veri sızıntısı ve KVKK/GDPR riskleri yaratır.
2. Çevrimdışı (uçakta, çekmeyen yerde) uygulamanın açılmasını engeller.
3. Sunucu ve bakım maliyetlerini astronomik düzeyde artırır.

## 🎯 Alınan Karar (Decision)
**Kimlik (Identity) ile Finansal Veri (Financial Data) mimari olarak %100 birbirinden koparılmıştır:**
1. **Google Hesabı Yalnızca Lisans İçindir:** Google ile giriş yalnızca kullanıcının kimliğini (`Google UserId / Token`) doğrulamak ve Google Play abonelik durumunu (Entitlement) teyit etmek için kullanılır.
2. **Finansal Veri Sıfır Bulut (Zero Cloud):** Kullanıcının maaşları, kredi kartları, planları ve gözlem noktaları **asla internete çıkarılmaz**. Sadece cihazdaki yerel SQLite dosyasında tutulur.
3. **Çevrimdışı Lisanslama (Offline Grace Period):** Satın alınan abonelik bilgisi yerelde imzalı bir token olarak saklanır. Kullanıcı 30 gün boyunca hiç internete bağlanmasa bile Pro özellikleri kesintisiz çalışır.

## ⚖️ Sonuçlar (Consequences)
- **Pozitif (Mutlak Gizlilik):** Kullanıcıya *"Verileriniz Google'a veya Mizan sunucularına asla yüklenmez, cihazınızda kalır"* garantisi verilir (Çok güçlü pazarlama kozu).
- **Pozitif (Sıfır Gecikme & Kesintisiz Çalışma):** İnternet bağlantısı olmasa bile uygulama anında açılır.
- **Negatif (Cihazlar Arası Otomatik Senkronizasyon Yok):** Veriler bulutta olmadığı için cihaz değiştirirken kullanıcının `Yedek Al / Geri Yükle (Backup & Restore)` dosya fonksiyonunu kullanması gerekir (bilerek tercih edilmiştir).
