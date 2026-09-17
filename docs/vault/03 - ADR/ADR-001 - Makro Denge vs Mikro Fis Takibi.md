---
id: ADR-001
title: Makro Denge vs Mikro Fiş Takibi
status: Accepted
date: 2026-09-18
tags:
  - adr
  - architecture
  - product-philosophy
---

# ADR-001: Makro Denge vs Mikro Fiş Takibi

## 📌 Bağlam (Context)
Piyasadaki çoğu bütçe uygulaması (YNAB, Money Lover vb.) kullanıcıdan her harcadığı kahveyi, market fişini ve otobüs biletini tek tek girmesini bekler. Kullanıcılar ilk 2-3 haftadan sonra veri giriş memuru olmaktan yorulup uygulamayı silmektedir.

## 🎯 Alınan Karar (Decision)
Mizan'da **mikro harcama takibi tamamen reddedilmiştir**. 
Bunun yerine:
1. Sabit ve zorunlu giderler (Kredi, Ekstre, Kira, Taksitler) dondurulmuş plan ile yönetilir.
2. Geriye kalan tutar tek bir **"Serbest Yaşam Bütçesi Havuzu"** olarak tanımlanır.
3. Dönem içinde yalnızca anlık bakiye gözlem noktası girilir.
4. Dönem sonu mutabakatında tek kalemde gerçekleşen harcama teyit edilir.

## ⚖️ Sonuçlar (Consequences)
- **Pozitif:** Kullanıcı sürtünmesi (friction) sıfıra iner; sürdürülebilirlik en üst düzeye çıkar.
- **Pozitif:** Kod tabanında karmaşık fiş, etiket ve kategori muhasebesi yerine deterministik nakit projeksiyonuna odaklanılır.
- **Negatif:** "Hangi markete ne kadar verdim?" raporu üretilmez (bu bilerek kapsam dışı bırakılmıştır).
