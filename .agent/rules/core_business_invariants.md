# Core Business ve Finansal Hesaplama İnvaryantları

Bu kurallar Mizan'ın matematiksel ve finansal doğruluğunu garanti eden temel direklerdir.

## 1. Dondurulmuş Plan Dokunulmazlığı (Invariant I16)
- Bir dönem başladığında dönemin dondurulmuş planı (`PeriodPlanSnapshot` / `openPlan`) sabitlenir.
- Dönem içinde yapılan kredi kartı harcamaları veya plansız nakit çıkışları, dönemin `PLANLANAN` (Planned) değerlerini ASLA değiştiremez.
- Bu harcamalar sadece:
  - `MEVCUT` (Current) gerçekleşen değerleri,
  - `GİDİŞAT` (Projected Ending Balance) dönem sonu tahminini,
  - `KMH Faizi` tahminini etkiler.
- 12 Dönemlik Projeksiyon ekranındaki ilk dönem (Period 0) de bu kilitli planı yansıtır ve dönem içi harcamalardan dolayı baseline ödeme tutarlarını şişirmez.

## 2. Kredi Kartı Ödeme ve Ekstre Kuralları
- **Borçtan Fazla Ödeme Yapılamaz:** Bir ekstreye veya döneme ait ödeme planlanırken tutar ekstre borcunu aşamaz: `Math.Min(requested, statementBalance)`.
- **Yasal Asgarinin Altına İnilemez:** Ödeme tutarı yasal asgari tutardan az olamaz: `Math.Max(requested, minimumPayment)`.
- **Sabit Ödeme Planı (FixedAmount):** Kullanıcı karta sabit bir ödeme taahhüdü tanımladıysa, dönem ortasında gelen ek harcamalar o dönemin ödeme tutarını artırmaz; artan borç sonraki ekstreye devreder.
- **Asgari Ödeme Oranları:** Kart limitine göre %20 veya %40 yasal asgari oranı uygulanır.

## 3. Çok Dönemli Nakit Akışı ve Para Korunumu (Conservation of Money)
- Bir dönemin kapanış bakiyesi (`EndingProjectedBalance`), bir sonraki dönemin açılış bakiyesine (`OpeningBalance`) kuruşu kuruşuna eşittir.
- Dönemler arası geçişte para kaybolamaz veya hayali para türetilemez (Zero Drift Invariant).

## 4. KMH ve Eksi Bakiye Faiz Tahakkuku
- Hesap bakiyesi negatif bölgeye düştüğünde, eksi bakiye üzerinden günlük KMH akdi faiz oranı işletilir.
- Dönem içi harcama arttıkça, açık büyüyeceği için `ProjectedDeficitInterest` dinamik olarak artar.
