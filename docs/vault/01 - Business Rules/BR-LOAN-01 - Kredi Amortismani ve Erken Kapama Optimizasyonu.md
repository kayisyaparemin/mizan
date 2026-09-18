---
id: BR-LOAN-01
title: Kredi Amortismanı ve Erken Kapama Optimizasyonu
domain: Kredi ve Borç Yönetimi
status: Active
pillar: 4. Gerçekçi Maliyet ve Faiz Modellemesi
tags:
  - business-rule
  - loan
  - amortization
  - early-closure
  - 6502-law
  - payoff-advisor
---

# BR-LOAN-01: Kredi Amortismanı ve Erken Kapama Optimizasyonu

## 🎯 Kuralın Amacı ve Özü
Kredilerin geri ödeme planlarını standart Fransız amortisman (annüite) modeliyle kuruşu kuruşuna modellemek; ara ödeme ve erken kapama anındaki net borç yükümlülüğünü (kalan anapara + gün işleyen faiz + yasal erken kapama ücreti) hesaplamak ve kullanıcının likidite açığına düşmesine izin vermeden en kârlı erken kapama zamanını belirlemektir.

---

## 📐 Matematiksel Model ve Algoritma

### 1. Aylık Efektif Faizin Çözülmesi (SolveMonthlyRate & Bisection)
Kullanıcının sisteme girdiği kalan taksit tutarı $P$, kalan taksit adedi $n$, son taksit tutarı $P_{\text{final}}$ ve kalan anapara $PV$ üzerinden aylık bileşik efektif faiz oranı $r$ türetilir:

$$PV(r) = P \times \left( \frac{1 - (1 + r)^{-(n-1)}}{r} \right) + P_{\text{final}} \times (1 + r)^{-n}$$

- $r \in (0, 1]$ aralığında azalan bir fonksiyondur ve 200 adımlı İkili Arama (Bisection) yöntemiyle kök çözülür.
- **Türetilen Faiz Doğruluk Sınırı:**
  $$\text{Aylık Oran} \le \text{MaxPlausibleMonthlyRate} = 0.08 \quad (\%8)$$
  Eğer türetilen oran $\%8$'in üzerindeyse veritabanındaki anaparanın güncel olmadığı veya kalan toplam borcun yanlışlıkla anapara hanesine yazıldığı kabul edilir (`ImplausibleRate`).
- **Alternatif Kaynak (BankQuote):** Bankadan alınan tarihli resmi kapatma tutarı varsa, o tarihe kadar işleyen faiz ve kalan taksitlerin bugünkü değeri denklem sistemiyle çözülerek faiz türetilir.

### 2. Taksit Ödemesi Sonrası Anapara İlerlemesi ($I_{17}$ Kuralı)
Taksit ödendiğinde taksitin tamamı anaparadan düşülemez. Yalnızca anapara payı düşülür:

$$\text{Dönem Faizi} = \text{Principal} \times r$$
$$\text{Ödenen Anapara} = P - \text{Dönem Faizi}$$
$$\text{Principal}_{\text{yeni}} = \text{Principal} - \text{Ödenen Anapara}$$

$k$ taksit ödendikten sonra kalan anapara formülü:
$$\text{Principal}(k) = PV \times (1+r)^k - P \times \frac{(1+r)^k - 1}{r}$$

### 3. Tarihli Erken Kapama Bedeli (Payoff Quote)
Herhangi bir $t$ gününde krediyi kapatmanın toplam maliyeti:

$$\text{Kapama Tutarı} = \text{Kalan Anapara} + \text{Gün İşleyen Faiz} + \text{Erken Ödeme Ücreti}$$

- **Gün İşleyen Faiz ($\Delta t$ Gün):** Son ödenen taksitten ($LastPaid$) kapatma tarihine ($t$) kadar geçen gün farkı $\Delta t$ üzerinden bankacılık standardı (30 gün esası):
  $$\text{Gün İşleyen Faiz} = \text{RoundMoney}\left(\text{Principal} \times r \times \frac{\Delta t}{30}\right)$$
- **6502 Sayılı Tüketicinin Korunması Hakkında Kanun Ayrımı:**
  - **Tüketici Kredileri (İhtiyaç, Taşıt - `LoanKind.Consumer`):** Kanun md. 27 gereğince tüketiciden erken ödeme ücreti **kesinlikle alınamaz** ($\text{Ücret} = 0$).
  - **Sabit Faizli Konut Finansmanı (`LoanKind.HousingFixed`):** Kanun md. 37 uyarınca banka yasal tavan komisyon uygulayabilir:
    $$\text{Kalan Taksit} \le 36 \text{ ay} \implies \%1 \quad (\text{Ücret} = \text{Principal} \times 0.01)$$
    $$\text{Kalan Taksit} > 36 \text{ ay} \implies \%2 \quad (\text{Ücret} = \text{Principal} \times 0.02)$$
  - **Değişken Faizli Konut Kredileri (`LoanKind.HousingVariable`):** Kanun md. 37 uyarınca ücret alınamaz ($\text{Ücret} = 0$).

### 4. Faiz Tasarrufu (Interest Saving)
$$\text{InterestSaving} = \sum \text{Kaldırılan Taksitler} - \text{Kapama Tutarı}$$

### 5. Akıllı Erken Kapama Tavsiye Motoru Kriterleri ([[LoanPayoffAdvisor]])
Erken kapama tavsiyesi salt nakit varlığına bakılarak verilemez. Ufuktaki her taksit günü için iki koşul aranır:
1. **Açık Büyütmeme Kriteri (No New Deficit):**
   Kapatma işlemi sonrasında önümüzdeki 12 dönemin hiçbirinde finansman açığı, baz senaryodaki (baseline) açıktan büyük olamaz:
   $$\text{Deficit}_{\text{senaryo}, i} \le \text{Deficit}_{\text{baz}, i} + 0.01$$
2. **Pozitif Net Kazanç Kriteri (Net Gain > 0):**
   $$\text{NetGain} = (\text{EndingBalance}_{12, \text{senaryo}} - \text{EndingBalance}_{12, \text{baz}}) + (\text{Ufuk Dışı Taksit Tasarrufu})$$
   Kredinin vadesi 12 aydan uzunsa, 12. aydan sonra ödenmeyecek taksitler de kazanca dahil edilir.

Tavsiye Durumları:
- `Recommended`: İki şartı da sağlayan en erken tarih (`Date`) ve varsa en kârlı alternatif tarih (`BestDate`).
- `NoSafeMonth`: Kapatmak kazançlı ancak her ayda likidite açığı tetikleniyor/büyüyor.
- `NotWorthIt`: Hiçbir ayda net pozitif getiri sağlanamıyor.
- `NeedsPrincipal`: Kalan anapara bilgisi eksik.
- `AlreadyClosing`: Krediye zaten aktif bir tam kapama planlanmış.

---

## 🧩 İlgili Mimari Sınıflar
- Sınıf: [[LoanAmortizationCalculator]]
- Sınıf: [[LoanScheduleCalculator]]
- Sınıf: [[LoanPaymentScheduleBuilder]]
- Application: [[LoanPayoffAdvisor]]
- Simülasyon Entegrasyonu: [[SimulationCalculator]]
- Model: [[Loan]], [[LoanAmortization]], [[LoanPayoffQuote]], [[LoanPayoffAdvice]]

---

## 💬 Karar Gerekçesi (Rationale)
> 1. **Açık Parasıyla Kredi Kapatılmaz:** Krediyi kapatmak için kullanılan para eğer KMH (kredili mevduat hesabı) veya gecikme faizi açığı yaratıyorsa ve bu açığın maliyeti kredi faizinden yüksekse, kapatma işlemi kullanıcıyı zarara sokar. Bu nedenle "No New Deficit" değişmez bir kuraldır.
> 2. **Yasal Tavan Korunumu:** 6502 sayılı kanuna riayet edilmeyip ihtiyaç kredisine ceza veya konut kredisine sınırsız ceza yazılırsa kapatma maliyeti saptırılır.
> 3. **Ufuk Dışı Tasarruf:** 60 aylık bir konut kredisinde ilk 12 ayın bakiyesine bakmak yanıltıcıdır; 12. aydan sonra ödenecek 48 taksitin faiz kazancı net kazanç hesabına zorunlu olarak dahil edilmelidir.
