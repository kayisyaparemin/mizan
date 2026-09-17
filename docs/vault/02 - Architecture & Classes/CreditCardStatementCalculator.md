---
title: CreditCardStatementCalculator
type: class
layer: Domain
namespace: Mizan.Domain.Calculations
tags:
  - class
  - domain
  - credit-card
  - banking-interest
---

# `CreditCardStatementCalculator`

Kredi kartı ekstre döngülerini, dönem içi bekleyen harcamaları, asgari ödeme tutarlarını ve bir sonraki ekstreye devreden bakiye üzerindeki **carry faizini (akdi faiz)** gerçek bankacılık yuvarlama standartlarında simüle eden hesaplayıcı.

---

## 🧭 Mimari Konum & Sorumluluk
- **Katman:** `Domain`
- **Türü:** Saf Hesaplayıcı (Partial Class: Core, Dates, Validation)
- **Sorumluluğu:** 
  - Kesilmiş ekstre (`CurrentStatement`) ile henüz kesilmemiş dönem içi harcamaları (`UnbilledSpending`) ve bekleyen provizyonları ayrıştırmak.
  - Kesilmiş ekstre varsa, bankanın faizi zaten işlettiğini kabul ederek üzerine tekrar faiz eklememek (Immutability).
  - Devreden borç (`CarriedBalance`) varsa, bir sonraki ekstreye `carried * carryInterestRate` şeklinde bankacılık standartlarında yuvarlanmış faiz yansıtmak.
  - Asgari ödeme kuralını hesaplamak.

---

## 🔗 Bağımlılıklar (Depends On)
- `CalendarRules` — Ekstre kesim günü ve son ödeme tarihi hesapları.
- `CreditCard` (Domain Model) — Kart limit, ekstre günü, devreden bakiye ve işlem listesi.

## 👥 Kimler Tarafından Kullanılıyor? (Referenced By)
- [[FinancialProjectionCalculator]] — Kart yükümlülüklerini projeksiyona eklemek için.
- `CreditCardObligationService` — Ekstre operasyonları ve kart kontrol ekranları için.
- `CardControlViewModel` — Kullanıcının kart ekstresini yönettiği ekran.

---

## 📜 Bağlı İş Kuralları
- [[BR-CARD-01 - Kredi Karti Carry Faizi ve Asgari Tutar Mantigi|BR-CARD-01: Kredi Kartı Carry Faizi ve Asgari Tutar Mantığı]]

---

## 💡 Kritik Kural Notu (Kod İçi Invariant)

> *"Devreden bakiyenin faizi, o bakiyenin girdiği ekstreye eklenir (gerçek banka davranışı). Kesilmiş ekstrede banka faizi zaten işlemiştir; `StatementAmount` nihai tutardır, üzerine eklenmez."*
