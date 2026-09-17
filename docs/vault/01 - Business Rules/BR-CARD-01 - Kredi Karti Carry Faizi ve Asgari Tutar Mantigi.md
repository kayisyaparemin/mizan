---
id: BR-CARD-01
title: Kredi Kartı Carry Faizi ve Asgari Tutar Mantığı
domain: Bankacılık ve Kredi Kartı
status: Active
pillar: 4. Gerçekçi Maliyet ve Faiz Modellemesi
tags:
  - business-rule
  - credit-card
  - carry-interest
---

# BR-CARD-01: Kredi Kartı Carry Faizi ve Asgari Tutar Mantığı

## 🎯 Kuralın Amacı ve Özü
Kredi kartı ekstrelerinin yalnızca borç toplamını değil; asgari ödeme yapıldığında bir sonraki aya devreden tutarın getireceği faiz maliyetini (akdi/carry faizi) kuruşu kuruşuna modellemektir.

---

## 📐 Bankacılık Kuralları ve Algoritma

### 1. Kesilmiş Ekstre Dokunulmazlığı (Immutability)
- Eğer kartın kesilmiş bir ekstresi varsa (`CurrentStatement is not null`):
  - Banka bu ekstre üzerindeki faizleri zaten işletmiştir.
  - `StatementAmount` nihai tutardır; sistem bunun üzerine **ikinci kez faiz işletmez**.

### 2. Devreden Bakiye Faizi (Carry Interest)
- Önceki ekstrelerden kalan devreden borç (`CarriedBalance`) varsa:
  $$\text{Carry Faizi} = \text{RoundMoney}(\text{CarriedBalance} \times \text{CarryInterestRate})$$
- Bu faiz, devreden borcun girdiği yeni ekstreye yeni harcama gibi eklenir.

### 3. Yeni Ekstre Bakiye Hesabı
$$\text{Ekstre Bakiyesi} = \text{Devreden Bakiye} + \text{Carry Faizi} + \text{Dönem İçi Yeni Harcamalar}$$

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[CreditCardStatementCalculator]]
- Sınıf: `CreditCardActualPaymentReconciler`
- Application: `CreditCardObligationService`
- UI: `CardControlViewModel`
