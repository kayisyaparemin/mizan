---
title: İş Kuralları ve Deterministik Hesaplama İndeksi
type: moc
updated: 2026-09-18
tags:
  - moc
  - business-rules
  - mizan
---

# 📜 İş Kuralları ve Deterministik Hesaplama İndeksi

Mizan'ın en büyük gücü, kullanıcıyı belirsizlikten kurtaran ve kuruşu kuruşuna doğru çalışan **deterministik matematiksel kurallarıdır**. 

Bu indekste, projenin "hafızasını" oluşturan, neden-sonuç ilişkileri netleştirilmiş temel iş kuralları listelenmektedir.

---

## 📑 Kural Listesi

| Kural Kodu | Kural Adı | İlgili Sütun / Alan | Çekirdek Sınıf |
|---|---|---|---|
| [[BR-RECON-01 - Donem Mutabakati ve Gozlem Noktasi|BR-RECON-01: Dönem Mutabakatı ve Gözlem Noktası]] | Plan dondurma, Gözlem Noktası sapması ve Fiili Yaşam Gideri mutabakatı | 1. Mutabakat | [[CashFlowPeriodCalculator]], [[PeriodWorkflowService]] |
| [[BR-PROJ-01 - 12 Aylik Kumulatif Likidite ve Finansman Acigi|BR-PROJ-01: 12 Aylık Kümülatif Likidite ve Finansman Açığı]] | Anchor tarihinden itibaren 365 günlük kümülatif bakiye ve eksi gün maliyetleri | 2. Projeksiyon | [[FinancialProjectionCalculator]] |
| [[BR-SIM-01 - Senaryo Kosul Izolasyonu ve Canliya Aktarim|BR-SIM-01: Senaryo Koşul İzolasyonu ve Canlıya Aktarım]] | Geçici plan izolasyonu, bağımsız What-If toggle ve "Planı Uygula" orkestrasyonu | 3. Simülasyon | [[SimulationCalculator]] |
| [[BR-CARD-01 - Kredi Karti Carry Faizi ve Asgari Tutar Mantigi|BR-CARD-01: Kredi Kartı Carry Faizi ve Asgari Tutar Mantığı]] | Ekstre devreden bakiye faizi, asgari ödeme kuralı ve kesilmiş ekstre immutability | 4. Faiz & Kart | [[CreditCardStatementCalculator]] |
| [[BR-REMIND-01 - Operasyonel Hatirlatici ve Vade Erteleme|BR-REMIND-01: Operasyonel Hatırlatıcı ve Vade Erteleme]] | Rahat / Agresif bildirim pencereleri, sessiz saatler ve Snooze ötelemesi | 5. Hatırlatıcı | [[PaymentReminderPlanner]] |

---

## 📊 Dataview: Tüm İş Kuralları

```dataview
TABLE id AS "ID", domain AS "Alan", status AS "Durum", file.outlinks AS "İlgili Kodlar"
FROM #business-rule
SORT id ASC
```
