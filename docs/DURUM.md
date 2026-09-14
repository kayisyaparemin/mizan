# Mizan — Proje Durumu

> Son güncelleme: 14.09.2026

## Şu anki durum

- **Branch:** `main` (temiz, push edilmiş)
- **Son commit:** `c962081`
- **Testler:** 424/424
- **Android Release build:** 0 uyarı, 0 hata
- **Son sürüm:** `v1.14.0` — yedekten ekle (`Mizan-1.14.0.apk`)
- **Şema:** v15 (`loan_prepayments`, `loans.FinalPaymentAmount`, taslak koşulunda `LoanId`/`PrepaymentMode`)
- **Veri yeri:** profil başına `profiles/{id}/coinflow.db3` (v1.12.0'dan beri)
- **Yedek yeri:** `/storage/emulated/0/Mizan/Mizan-yedek-YYYY-MM-DD.zip` (dev: `Mizan Dev`), v1.13.0'dan beri

## Ne yapıldı (06–07.09.2026 turu)

`v1.0.3` → `v1.5.0`, 29 commit, 11 sürüm. Altı düzeltme, üç özellik, bir
hesap hatası, iki sadeleştirme. Hepsi gerçek kullanımdan çıktı; çoğunun ortak
paydası **motor doğru hesaplıyordu ama ekran eksik ya da yanlış söylüyordu**.
Dört istisna: v1.2.1'de motorun kendisi eksik hesaplıyordu; v1.3.0 ve v1.4.0'da
ekran doğruydu ama okunmuyordu; v1.5.0 ise eksik olan bir şeyi ekledi.

| Sürüm | Ne çözdü |
|---|---|
| v1.0.4 | Release notları sabit yazılmıştı, hâlâ v1.0.3'ü anlatıyordu |
| v1.0.5 | `[çapa, ilk maaş)` arasındaki tek seferlik gelir sessizce yutuluyordu |
| v1.0.6 | `FinancingLoan` kredi anaparasını hiç yazmıyordu; kredi saf maliyet gibi görünüyordu |
| v1.0.7 | Kart faizi / KMH faizi kırılımı hesaplanıyordu ama ekranda tek toplama katlanıyordu |
| v1.0.8 | Finansman maliyeti faiz toplamına girmiyordu · kart ödeme şekli simüle edilemiyordu |
| v1.0.9 | Kart ödeme kararı Dönem Detayı'ndan verilebilir hale geldi |
| **v1.1.0** | **Simülatörde Nakit Seyri grafiği (Faz 1 + 2)** — liste kaldırıldı |
| **v1.2.0** | **Grafik canlandı (Faz 3 + 4)** — koşul switch'leri ve yaşam gideri kadranı |
| **v1.2.1** | **Devreden kart borcunun faizi hiç işlenmiyordu** — tamamını ödeyende faiz kalıcı olarak sıfır görünüyordu |
| **v1.3.0** | **UI/UX yaması** — grafik geri alındı, mevcut tutar Ana Sayfa'ya taşındı, tekrar eden bölümler kalktı |
| **v1.4.0** | **Kart sayfası zaman eksenine göre yeniden kuruldu** — beş seçici dört kavramı aynı kelimelerle sunuyordu |
| **v1.5.0** | **Geçici planlar** — simülasyon denemesi uygulama kapanınca kayboluyordu |
| **v1.6.0** | **Üç zaman diliminin izolasyonu** — dönem içi gözlem dönem planını ve geçmiş logunu bozuyordu |
| **v1.6.1** | **Gidişat faizi saymıyordu** — plana tam uygun gidene faiz kadar kâr gösteriyordu |
| **v1.7.0** | **Gidişat parametre parametre** — açık faizi gözlemden yeniden hesaplanıyor; kart faizi hesaptan çıktı |

Grafik çalışmasının tamamı (Faz 1–4) ve alınan tasarım kararları:
**`DEVIR-FAZ3.md`** — dört faz da bitti, belge artık geriye dönük referans.

### v1.2.0 ne getirdi

- **Koşul aç/kapa (Faz 3).** Her koşulun switch'i var; kapalı koşul hesaba
  girmez, grafik anında yeniden çizilir (200 ms debounce, son-kazanır).
  "Planı Uygula" yalnızca açıkları kaydeder, kapalılar listede kalır.
  Neden switch: koşulların etkileri **toplanmıyor** (ölçüldü: kredi −23.764,
  kart +1.220, ikisi birden −17.922). Ekranda her an tek bir gerçek
  kombinasyon olması bu tuzağı ortadan kaldırıyor.
- **Hepsi kapalıyken baz çizgi.** `SimulationCalculator.Validate` koşulsuz
  simülasyonu reddetmeye devam ediyor; ekran o dalda `SimulateAsync`'i hiç
  çağırmadan `GetFuturePeriodsAsync()` ile tek çizgi gösteriyor.
- **Yaşam gideri kadranı (Faz 4).** `SimulateAsync` /
  `GetFuturePeriodsAsync` opsiyonel `monthlyLivingBudgetOverride` alıyor;
  override plan kopyasına yazılıyor, `UserSettings` dokunulmuyor (I13).
  Baz ve senaryo **aynı** plandan türediği için iki çizgi birlikte kayıyor —
  ortak zemin değişir, planın etkisi değil.
- **Düzeltme:** `Populate` içinde grafik `Results` dolmadan yenileniyordu;
  ikinci simülasyondan sonra "Seçili Dönem" bir önceki hesabın satırında
  kalıyordu.

### v1.2.1 ne getirdi

- **Devreden borcun faizi artık işleniyor.** Faiz, bakiyenin *girdiği* ekstrede
  bir kez hesaplanıp ekstre tutarına ekleniyor; ödeme sonrası kalan anapara
  faizsiz devrediyor. Eskiden tam tersiydi — faiz yalnızca ödemeden sonra
  kalana uygulanıyordu, yani devraldığın borcun faizi hiç yazılmıyordu.
  Ekstresini **tamamını ödeyen** kullanıcıda faiz kalıcı olarak sıfır
  görünüyordu; bulguyu ortaya çıkaran durum buydu.
- **İki koruma.** Bankanın kestiği gerçek ekstre (`CurrentStatement`) nihai
  tutardır, üzerine faiz eklenmez. `CreditCardActualPaymentReconciler` da artık
  faizi kapitalize etmiyor, yalnız kalan anaparayı yazıyor — ikisi de aynı
  faizin iki kez sayılmasını engelliyor.
- **Etki.** Kanonik 12 dönem kart faizi 7.101,67 → 10.116,26. Bulgudaki
  senaryoda (60.000 devreden + 17.900 harcama, tam ödeme) ekstre 77.900 → 80.900
  ve daha önce hiç görünmeyen 3.000 TL faiz ortaya çıkıyor.
- **Test.** 283 → 284; yeni test bulgunun birebir reprodüksiyonu. Rakam bekleyen
  assertion'lar probe ile ölçülüp kesin eşitlik olarak yeniden sabitlendi,
  hiçbiri gevşetilmedi. `ProductContractInvariantTests` dokunulmadan geçti.
- Tam kayıt: **`BULGU-KART-FAIZI.md`** (bulgu + uygulanan çözüm).

### v1.3.0 ne getirdi (UI/UX yaması)

Kullanıcı ürünü ilk tasarım amacıyla kullandı ve grafiğin işe yaramadığını
söyledi. Bu sürüm ekleme değil, **geri alma ve sadeleştirme**. Plan:
`PLAN-UIUX-YAMASI.md`.

- **Grafik silindi, liste geri geldi.** `CashProjectionChartDrawable`,
  `SimulatorChartSeries/Point`, `BuildCashChart`, aralık çipleri, efsane ve
  yaşam gideri slider'ı gitti; Simülatör yeniden `Results` listesini
  gösteriyor. Servis tarafındaki `monthlyLivingBudgetOverride` parametresi
  **duruyor** (kullanıcı "oraya da geleceğim" dedi), yalnız arayüzü kalktı.
- **Koşul switch'leri korundu** — kullanıcının sevdiği tek şeydi. Hepsi
  kapalıyken liste boşalmıyor, baz projeksiyonu gösteriyor (emülatörde
  doğrulandı).
- **12 Dönem kutusuz.** `CollectionView` kendi içinde kayıyordu; `ScrollView`
  + `BindableLayout`'a geçildi, sayfanın tamamı tek yüzey olarak kayıyor.
- **Mevcut tutar Ana Sayfa'da, en üstte.** `RefreshCurrentFinancialStateAsync`
  üzerinden gidiyor: tutar ve çapa birlikte bugüne yazılıyor. Ayarlar'daki
  alan kaldırıldı.
- **Ana sayfa aynı şeyi bir kez söylüyor.** Dönem sonu rakamı uyarı kutusu,
  durum özeti ve "Bu dönem nasıl oluşuyor" tablosunda üç kez duruyordu;
  yalnız durum özeti kaldı, kırılım için Dönem Detayı'na bağlantı verildi.
  Aksiyon bekleyen uyarılar (review, kart tercihi) duruyor.
- **12 dönem faizi kırılımlı** (kart / KMH / toplam), simülatördeki gibi.

**Emülatörde yakalanan kusur:** kırılım satırları `N0` ile yuvarlanınca
ekranda toplanmıyordu (10.328 + 12.650 = 22.978, motor 22.977). Üç satır da
kuruşa çevrildi; aynı hata 12 Dönem sayfasında da vardı, orada da düzeltildi.
Hiçbir teste düşmüyordu — bu projede dördüncü kez yalnız ekranda görülen bir
kusur.

**Not — plandaki bir teşhis yanlıştı.** Plan, Ayarlar'dan mevcut tutarı
değiştirmenin çapayı ilerletmediğini ve çift sayıma yol açtığını söylüyordu.
Ölçüldü: `CoinFlowService.SaveSettingsAsync` tutar değiştiğinde çapayı zaten
`clock.Today`'e normalize ediyor (`SettingsViewModel`'in eski çapayı geri
yazması bu yüzden zararsızdı). Taşıma yine de yapıldı — gerekçe UX: kullanıcı
uygulamaya girince ilk işi bugünkü bakiyeyi girmek. Çift sayım hatası yoktu.

### v1.4.0 ne getirdi (kart sayfası)

Kullanıcı "kart sayfası kaos, her yerde asgari/tamamı görüyorum" demişti.
Ölçüldü: 446 satır, 9 bölüm, **beş ayrı seçici dört farklı kavramı** aynı
kelimelerle sunuyordu. Aralarındaki tek fark kapsam — hangi ekstreleri
etkiledikleri — ve ekranda bunu söyleyen hiçbir şey yoktu.

| Kavram | Model alanı | Kapsam |
|---|---|---|
| Kesilmiş ekstrenin kararı | `CurrentStatementPaymentPlan` | Tek, şu anki ekstre |
| Vadeye özel override | `CreditCardPaymentPlan` | Tek, belirli bir vade |
| Kartın genel şekli | `PaymentStrategy` | Tüm gelecek ekstreler |
| Hesaplama varsayımı | `ProjectionFallbackStrategy` | Karar verilmemiş ekstreler |

Sayfa artık **zaman eksenine** göre okunuyor: ŞU AN → SIRADAKİ → GENEL. Her
başlık altında tek satırlık kapsam açıklaması var.

- **GENEL varsayılan kapalı.** Kartın varsayılan şekli ve hesaplama varsayımı
  nadiren değişiyor; sürekli ekranda olmaları kaosun büyük kısmıydı.
- **Etiketler kapsamı söylüyor.** "Varsayılan Plan" → *"Kartın varsayılan
  ödeme şekli · ayrı karar vermediğin her ekstre için geçerli olur"*.
  "Kararsız Gelecek Ekstrelerde" → *"Karar vermediğin ekstrelerde varsayım ·
  bir karar değil, hesaplama varsayımı; ekstre gelince yine sana sorulur"*.
- **Gelecek ekstre satırları kararın kaynağını yazıyor:** "Bu vade için: X",
  "Kartın varsayılanı: X", "Karar yok · varsayım: X". Eskiden "Bu ay",
  "Genel plan", "Kararsızda" deniyordu; üçü de aynı kelimeyle bitiyor, ayrımı
  önek yapıyor.
- **Referans listeler açılır-kapanır.** Ödeme tercihi geçmişi ve gelecek kart
  hareketleri varsayılan kapalı. Gelecek ekstreler açık kalır — planın kendisi.

**Hiçbir özellik kaldırılmadı.** Dört kararın hepsi yerinde; bir test bunu
ayrıca sabitliyor (`EveryPaymentDecision_IsStillReachable`).

### v1.5.0 ne getirdi (geçici planlar)

Kullanıcı: *"app'i kill ettiğimde deneme yaptığım simülasyonlar siliniyor."*
Doğruydu — simülasyon taslağı yalnız `SimulationViewModel`'in belleğinde
duruyordu, hiçbir yere yazılmıyordu.

- **`SimulationDraft`** — koşul listesinin adlandırılmış, kalıcı kopyası.
  `simulation_drafts` + `simulation_draft_conditions`, **şema v12**. Sadece
  iki tablo ekler; mevcut hiçbir tabloya dokunmaz, veri taşınmaz. (Sürüm
  atlaması `EnsureInitialPaymentAssignmentStrategyAsync`'i tetikliyor ama o
  metot strateji varken no-op; bir test yükseltme yolunu ayrıca doğruluyor.)
- **Sunum.** Simülasyon Planı kartının altında ad alanı + "Geçici Plan
  Oluştur"; sayfanın devamında "Geçici Planların" listesi (Yükle · Sil).
- **Açık/kapalı durumu da kaydedilir.** Bilerek kapatılmış koşul kapalı
  geri gelir.
- **Yükleme yerine geçer, eklemez.** İki planın koşulları karışırsa "bu plan
  neydi" sorusu cevapsız kalır; ekranda koşul varsa önce onay istenir.
- **`ScenarioId` korunur.** Apply yolu entity kimliklerini bundan
  deterministik üretiyor; kimlik değişseydi geri yüklenip ikinci kez
  uygulanan plan mükerrer yükümlülük oluştururdu.
- **Apply akışı dokunulmadı.** Uygulamak kaydedilmiş planı silmez; bir test
  bunu sabitliyor.

**Emülatörde yakalanan kusur:** liste tarihini varsayılan kültürle
biçimlendirdiğim için "08 September 2026" yazıyordu. Kültür açıkça verildi.
Bu projede beşinci kez yalnız ekranda görülen bir kusur — ve DURUM'un daha
önce not ettiği "İngilizce ay adı" hatasının aynısı.

**Doğrulama:** emülatörde kaydet → `am force-stop` → yeniden aç → plan
listede duruyor → Yükle → koşul geri geldi → Simülasyonu Yap çalıştı.

### v1.6.0 ne getirdi (üç zaman diliminin izolasyonu)

Kullanıcının tespiti: uygulama üç ayrı amaca hizmet ediyor ve bunlar birbirine
karışmış. Doğru çerçeveyi de kendisi koydu — **her ekran bir zaman dilimine
sahip olmalı**: 12 Dönem + Simülatör gelecek, Ana Sayfa mevcut dönem, Geçmiş
geçmiş.

**Asıl teşhis:** ana sayfanın kendi verisi yoktu. Üstündeki her rakam
`GetDashboardAsync` → `FinancialProjectionCalculator` render'ıydı. Mevcut
dönemin evi bir sayfa olarak vardı ama tamamen gelecek sorumluluğunun
mobilyasıyla döşeliydi; dönem içi gözlemin kaçak yaşamasının sebebi buydu.

**Ölçülen bozulma:** dönem içinde ana sayfadan bakiye güncellemek
`RefreshCurrentFinancialStateAsync` çağırıyordu — bir **checkpoint** işlemi.
20.08–10.09 penceresi 07.09–10.09'a düşüyor, zorunlu 54.823 → 0, ödeme satırı
2 → 0, orijinal plan yetim kalıyor ve `PeriodPlanRevision` **hiç** üretilmiyordu
(pencere sınırı değişince `HistoricalPlanRevisionService` sessizce çıkıyor).
En yıkıcı işlem iz bırakmayan tek işlemdi.

- **`PeriodObservation`** — açık dönemin gözlem defteri, şema **v13**
  (üç yeni tablo, mevcut tablolara dokunulmadı). Alanları `PeriodReviewDraft`
  ile birebir eşlenir; checkpoint'te review'ı doldurur, finalization'da
  tüketilip silinir. İki ayrı gerçekleşme kaydı yok — aynı defter, farklı
  zamanda okunuyor.
- **`PeriodProgressService`** — Ana Sayfa'nın motoru. Yeni hesap yapmaz;
  donmuş planı (varsa en güncel revizyonu) gözlemle toplar. Dönem sonu tahmini
  `gözlenen bakiye − kalan planlı ödemeler − kalan yaşam gideri`. Gözlem yoksa
  `null` döner ve gidişat bloğu hiç görünmez.
- **Ana Sayfa saf mevcut dönem.** PLAN · GİDİŞAT · KALAN. 12 dönem sonu, faiz
  kırılımı ve geçmiş özeti kalktı; bağlantılar rakamsız kaldı.
- **Mevcut tutar artık gözlem.** `ObserveCurrentBalanceAsync` snapshot
  zincirine dokunmaz.
- **12 Dönem sapmayı söylüyor** ama projeksiyonu kaydırmıyor.

**Yeni invariant'lar:** I14 (snapshot yalnız checkpoint'te ilerler),
I15 (gözlem review'ın girdisidir), I16 (her ekran tek zaman dilimi).

**Emülatörde yakalanan kusur:** KALAN başlığı "09 Eyl → 10 Eyl" derken altında
07 Eyl tarihli satırlar duruyordu. Satırlar doğruydu (ödenmiş işaretlenmemiş),
başlık yanlıştı; "Ödenmiş işaretlemediklerin" oldu ve vadesi geçmiş satırlara
işaret eklendi. Bu projede altıncı kez yalnız ekranda görülen kusur.

**Doğrulama:** temiz kanonik veriyle PLAN 39.854,22 · KALAN 54.823,20
(iki satır) · gözlem sonrası GİDİŞAT −136.791, fark −176.645 — ve **PLAN
değişmedi**. 12 Dönem'in projeksiyonu da kaymadı.

**Not:** plan belgesindeki adım sırası korundu ama Adım 3 (Ana Sayfa) plandan
daha geniş uygulandı: `DashboardViewModel`'in gelecek/geçmiş özet property'leri
tamamen kaldırıldı, yalnız gizlenmedi.

### v1.6.1 ne getirdi (gidişat faizi)

Kullanıcı `v1.6.0`'ı gerçek verisiyle kullandı ve gidişatın yanlış olduğunu
fark etti: dönem harcaması 20.000 planlanmışken ekran **+4.062 TL kâr**
gösteriyordu — "16 harcamış olamam".

**Kök neden:** gidişat `gözlenen bakiye − kalan ödemeler − kalan yaşam gideri`
hesaplıyordu; faiz terimi yoktu. `PlannedEndingSavings` ise kart ve açık
finansman faizini **içeriyor**. İki rakam aynı kalemleri içermeyince "plana
göre fark" faiz kadar kayıyordu.

**Ölçüm:** plana birebir uyan bir dönem kurup (açık durumda, finansman açığı
faizi 5.507,29) gözlem girildi. Fark **0 olması gerekirken 5.507,29** çıktı —
sapmanın tamamı faizdi (`SAPMA − faiz = 0.00`).

**Neden testler yakalamadı:** kanonik seed'in faizi **sıfır**. Mevcut
`PeriodProgress` testlerinin hiçbiri faizli bir plan kurmuyordu, dolayısıyla
eksik terim hiç görünmüyordu.

- Düzeltme: `gözlenen bakiye − kalan ödemeler − kalan yaşam gideri −
  planlanan faiz`.
- Faiz gözlenen nakit bakiyenin içinde değil: kart faizi karta kapitalize olur
  (I9), açık faizi bir planlama kalemidir — ikisi de dönem sonuna kadar
  önümüzde.
- Kullanılan faiz **planın** faizi; gözlenen pozisyondan yeniden hesaplanmıyor
  çünkü bu projeksiyon motorunu çağırmak olurdu (I16). Plandan çok sapılmışsa
  gerçekleşecek faiz daha yüksek olabilir — bilinen sadeleştirme, sürüm
  notunda da söyleniyor.
- Ekranda tahminin altına neyin düşüldüğünü söyleyen bir satır eklendi.
- `PeriodTrajectoryTests` (**yeni, 4 test**) bilinçli olarak faizli senaryo
  kurar. Asıl testi: *plana tam uygun gidiliyorsa fark sıfırdır.*

**Ders:** kanonik seed bir riski sıfırlıyorsa o risk test edilmiyor demektir.
Faiz, büyük gider ve açık gibi kalemleri sıfır olan bir fixture, o kalemlere
bağlı hataları göstermez.

### v1.7.0 ne getirdi (gidişat parametre parametre)

Kullanıcı `v1.6.1`'i görünce feature'ın **amacını** hatırlattı: *"elimdeki
mevcut miktarı girdiğimde planda hangi değişiklikler olduğunu görmem"*. Yani
"18, 20 Eylül ve 7 Ekim'deki ödemeleri yapınca dönem sonu X oluyor, planda Y
idi; faiz A oluyor, planda B idi" — **değişebilen parametrelerin listesi**.

v1.6.0/v1.6.1 bunu tek bir "fark" rakamına indirmişti ve faizi plandan sabit
alıyordu. İkisi de yanlıştı.

- **Ekran artık parametre başına karşılaştırma:** her satırda *şimdi / planda
  / fark*. Sütun düzeni Geçmiş ekranıyla aynı — Ana Sayfa, Geçmiş'in dönem
  kapanmadan önceki hâli.
- **Açık faizi gözlenen pozisyondan yeniden hesaplanıyor.** Pozisyonla değişen
  tek faiz kalemi o; plandan kopyalamak, kullanıcının görmek istediği şeyi
  dondurmaktı. Formül `PeriodPlanSnapshotService.Freeze` ile birebir aynı.
- **Kart faizi hesaptan çıktı.** `v1.6.1` onu da düşüyordu; oysa motor
  `PlannedEndingSavings`'e katmıyor — karta kapitalize olur, nakit dönem
  sonunu değiştirmez (I9). Ekranda bilgi satırı olarak duruyor.
- `PeriodProgress` artık `ExpectedBalanceToday`, `ProjectedDeficitInterest`,
  `PlannedDeficitInterest`, `PlannedCardInterest`, `RemainingLivingBudget`
  taşıyor.

**I16 fazla geniş yorumlanmıştı.** O kural ana sayfanın *başka zaman
dilimlerinin* rakamını göstermesini yasaklar; mevcut dönemi hesaplamasını
değil. "Projeksiyon motoru çağrılamaz" gerekçesiyle faizi dondurmak, kuralı
ürünün aleyhine çevirmekti — üstelik bu hesap için motor gerekmiyor, tek
formül yeterli.

**Doğrulama (emülatör, gerçek rakamlarla):** gözlem −81.000 · kalan ödeme
54.823,20 → faiz öncesi −135.823,20 · açık faizi 6.791,16 (planda 0) · dönem
sonu −142.614,36 (planda 39.854,22) · fark −182.468,58. Dördü de elle
doğrulandı.

**Testler 320 → 323.** `PeriodTrajectoryTests` yeniden yazıldı; asıl testi
hâlâ *plana tam uygun gidiliyorsa her satırın farkı sıfırdır*, üstüne
pozisyon kötüleşince faizin arttığı ve kart faizinin nakde girmediği eklendi.

### v1.8.0 ne getirdi (yaşam gideri bir havuz)

`v1.7.0`'ı gerçek rakamlarla deneyen kullanıcı **üçüncü** kez aynı yeri
gösterdi ve bu kez kuralı kendisi yazdı: *"yaşam giderini bir harcama bakiyesi
olarak düşünmelisin. 15k harcadıysam orada hâlâ 5k bakiyem var. günlere
bölerek düşünme."*

Sorun `v1.6.0`'dan beri sessizce duruyordu: `ExpectedBalanceToday` planı geçen
güne bölüp "bugüne kadar bu kadar harcamış olmalıydın" diye kıyaslıyordu.
Kullanıcı dönem başında 20.000 bütçenin 4.807'sini harcamışken ekran
−3.474 sapma gösteriyordu. **Plana tam uyan kullanıcıya yoktan sapma
uyduruyordu.** Kod doğru aritmetik yapıyordu, yanlış soruyu soruyordu.

- **Yaşam gideri havuz oldu.** `planlanan − harcanan = kalan`; günlere
  bölünmüyor. Havuzun *içinde* harcamak dönem sonunu hiç oynatmıyor
  (harcanan + kalan her zaman planlanana eşit), yalnız havuz aşılınca fazlası
  dönem sonuna ve açık faizine yansıyor.
- **Kart harcaması yaşam gideri sayılmıyor.** Kartın içinde ekstre tarihiyle
  yönetiliyor. Kartlar kendi bölümünde, kart başına: *planlanan ekstre /
  mevcut ekstre*. Mevcut, kartın bugünkü hâlinden `CreditCardStatementCalculator`
  ile 6 ekstre ileriye projelendirilip dönem penceresine düşenlerin toplamı.
- **Vadesi geçen satır kendiliğinden ödenmiş sayılıyor.** Kullanıcı "Ödedim"
  butonu teklifimi reddetti: *"ekstre kesilmeden kart'a ödeme yapılmıyor ki?
  ödeme günü geldiğinde kart'ı ödedim dememi kast ediyorsan sonraki maaş
  döneminde yazıyoruz zaten onu."* Haklı — gerçekleşen tutarın yeri
  checkpoint'teki review. Kural: açıkça işaretlenmiş **veya**
  `PlannedDate <= today` → yapılmış.
- **KMH satırı yalnız gerçekten açık varsa açılıyor.** Sıfır yazmak için alan
  ayrılmıyor (kullanıcının kendi isteği).
- **Kart faizi ana sayfadan tamamen çıktı.** Kart borcuna ve ödeme kararına
  bağlı; gözlenen nakit pozisyonu onu oynatmıyor. Yeri 12 Dönem ekranı.
- `PeriodProgress`'ten `Deviation`, `BalanceDeviation`, `ExpectedBalanceToday`,
  `PlannedInterest`, `PlannedCardInterest` silindi; `PlannedLivingBudget`,
  `ObservedLivingSpend`, `RemainingLivingBudget`, `Cards`, `EndingDeviation`,
  `LivingOverspend` geldi.

**Model koda dokunmadan kullanıcının gerçek rakamlarıyla doğrulandı:**
harcanan 4.807 · kalan yaşam 15.193 · faiz öncesi −150.721 · KMH faizi
7.536,05 · dönem sonu −158.257,05 — planda −158.257. Havuz modeli planın tam
üstüne oturuyor; yani kullanıcı plana uygun gidiyordu ve günlere bölmek
−3.474'lük hayalet sapmayı **üretiyordu**.

**Testler 323 → 325.** `PeriodTrajectoryTests` havuz modeline göre yeniden
yazıldı; asıl testi artık *havuzun içinde harcamak dönem sonunu değiştirmez*.
`PeriodObservationTests` vadesi gelmemiş bir tarihe taşındı (elle işaretlemeyi
ölçebilmek için) ve `DashboardCurrentBalanceTests`'e vade kuralının kendi
testi eklendi.

### v1.9.0 ne getirdi (doğru kredi durumu — erken kapama Faz 1)

`PLAN-KREDI-KAPATMA.md`'nin zemin fazı. Kullanıcı *"şu ayda krediyi
kapatabilirsin"* diyen bir özellik istedi; araştırırken üstüne kurulacak her
rakamın yanlış olacağı ortaya çıktı.

- **🔴 Kalan anapara her checkpoint'te yanlış düşülüyordu.** Reconciliation
  ödenen taksitin **tamamını** `RemainingDebt`'ten çıkarıyordu. Garanti
  (190.188 · 14.501,23 × 22): 12 taksit sonra gerçek anapara 111.758, kayıt
  **16.173**. Artık yalnız anapara payı düşüyor (**I17**). Faiz
  türetilemiyorsa anaparaya dokunulmuyor.
- **`LoanAmortizationCalculator`.** Faiz girilmiyor, taksit + anapara + kalan
  sayıdan türetiliyor (BSMV/KKDF içinde). Bankanın **tarihli** kapatma tutarı
  girilirse faiz ve anapara ondan çözülüyor ve otorite oluyor. Herhangi bir
  günün kapatma tutarı: anapara + işleyen faiz + 6502 md. 37 ücreti (yalnız
  sabit faizli konut).
- **Korkuluklar.** Anapara ≥ kalan taksit toplamı (toplam borç girilmiş) ve
  aylık %8 üstü faiz (anapara bayat) kaydederken reddediliyor, listede uyarı
  olarak görünüyor.
- **Taahhütler sayfası.** Kredi **düzenlenebiliyor** (önceden yalnız
  sil/ekle vardı — silmek frozen plan satırlarını yetim bırakırdı). Kredi
  türü, "Kalan anapara" etiketi + açıklama, tarihli banka tutarı. Kredi
  başına: *kalan anapara · aylık faiz · bugün kapatma ≈ X · Y faiz ödemezsin*.
- `EarlyClosureAmount` alanı v1.0'dan beri **ölüydü** — alınıyor, hiçbir hesap
  okumuyordu. Artık tarihiyle birlikte faizin birinci kaynağı.
- Şema **v14**: `loans.Kind`, `loans.EarlyClosureAmountAsOf` (additive).

**Emülatörde elle doğrulandı (13.09):** Garanti — 07.09 taksiti ödenmiş
sayılır, anapara 185.271,98 + 6 gün faiz 1.867,5 = **187.139**, tasarruf
21 × 14.501,23 − 187.139 = **117.386**. Burgan — 55.777 + 26 gün faiz
1.753,2 = **57.530**, tasarruf **8.841**. Toplam borç girilince kayıt
reddi ekranda görüldü.

**⚠️ Kullanıcının gerçek veritabanı:** v1.3.0'dan beri kapatılan her
checkpoint anaparayı fazla düşürdü. Bu kurtarılamaz. Kredi satırında
*"anaparası güncel görünmüyor"* uyarısı çıkarsa bankadan kapatma tutarı
tarihiyle girilmeli.

**Testler 325 → 351.** `LoanAmortizationTests` (25) + review entegrasyonu
(Garanti ilk taksit sonrası 185.271,98).

### v1.10.0 ne getirdi (erken kapama ve ara ödeme — Faz 2)

Simülatörde **"Krediyi erken kapat"** ve **"Krediye ara ödeme yap"**
koşulları. Kullanıcı ara ödemeyi de ilk tura istedi (plan önerisi yalnız tam
kapamaydı).

- **Erken ödeme bir olay (I18).** `LoanPrepayment` krediyi değiştirmiyor;
  `LoanPaymentScheduleBuilder.Replay` taksit listesini olayları oynatarak
  üretiyor. Sebep ölçüldü: "planı uygula" yalnız **ekleme** yapabiliyordu —
  `SimulationPersistenceBatch` kredi taşımıyordu, mevcut krediyi değiştirmenin
  yolu yoktu.
- **Tutar kuralı.** Olay günü: o güne kadar taksitler ödenir, anaparadan X
  düşer, ödenen `X × (1 + faiz × gün ÷ 30) + ücret`. X'in o günlere düşen faizi
  tahsil edilir, kalanın faizi bir sonraki taksitte — faiz iki kez sayılmaz.
- **Vade kısaltmada küçük son taksit.** `Loan` tek taksit × sayı
  taşıyordu; `FinalPaymentAmount` eklendi ve amortizasyon (faiz türetme,
  kalibrasyon) son taksiti hesaba katıyor. Test: vade kısaltmadan sonraki
  kanonik krediden faiz yine %3 türetiliyor.
- **Checkpoint.** Planda yeni kaynak tipi yerine (K5 madde 3) **kimlik
  dispatch'i** seçildi: erken ödeme satırı `Loan` tipiyle ama olay kimliğiyle
  geliyor. Böylece `PlannedLoanPayments`, etiketler, karşılaştırmalar
  kendiliğinden doğru; reconciliation kimliğe bakıp ayırıyor. Ödendi → krediye
  işlenir; ödenmedi → iptal (K6); ikisinde de olay silinir.
- **Sonuç ekranı.** Yeni "Kredi Faizi Tasarrufu" bölümü — kredinin **ömrü
  boyunca**, 12 dönemlik faiz tablosundan ayrı (K8). Senaryo maliyetine
  erken ödeme tutarı eklendi.
- **Finansal Yapı.** Uygulanan erken ödeme kredinin altında, o günkü
  tutarıyla; **Sil** = geri al.
- **Tarih varsayılanı.** Kredi seçilince tarih bugünden sonraki ilk taksit
  gününe gidiyor (işleyen faiz 0); plan adı krediden türüyor.
- Şema **v15**.

**Emülatörde doğrulandı:** Garanti 07.10.2026 kapama → ödenecek **180.108**
(07.09 ve 07.10 taksitleri sonrası anapara, elle 180.108,19), ömür boyu tasarruf
20 × 14.501,23 − 180.108 = **109.916**, son ödeme Haziran 2028 → Ekim 2026.
Aynı ekranda KMH faizi **+30.343** (12 dönem) — kararın iki tarafı yan yana.
Uygula → Finansal Yapı'da satır → Sil → satır gitti.

**Ekranda bulunan hata:** onay metni *"07 October 2026"* yazıyordu —
**yedinci** kültürsüz tarih. `SimulationViewModel`'deki bütün onay tarihleri
`TurkishCulture` ile düzeltildi. Aynı kalıp Geçmiş ve review sihirbazında
5 yerde daha duruyor → ayrı görev önerildi.

**Testler 351 → 371.** `LoanPrepaymentScheduleTests` (13, bağımsız hesaplanmış
beklenenlerle), `LoanEarlyClosureFlowTests` (7: simüle, uygula/idempotent/geri
al, anaparasız kredi reddi, ödendi kapanır, ödenmedi iptal, vade kısaltma
kanonik krediye işlenir, taslak round-trip).

**Flaky test:** `CreditCardStatementImportWorkflowTests.Cancellation_...`
tam koşuda ~1/3 düşüyor (75 ms'lik iptal zamanlayıcısı). Değişiklikle ilgisiz;
ayrı görev önerildi.

### v1.11.0 ne getirdi (kredi kapatma önerisi — Faz 3)

Kullanıcının asıl istediği cümle: *"şu ayda krediyi kapatabilirsin"*.

- **`LoanPayoffAdvisor`.** Baz projeksiyon bir kez; her kredi için ufuktaki
  her taksit günü (son taksit hariç) kapatılıp motor yeniden çalışır. Kural
  kullanıcının seçtiği: **açık yok** (hiçbir dönemde açık baz çizgiden büyük
  değil) **ve net kazanç** (12. dönem sonu farkı + ufuk dışı ödenmeyecek
  kredi ödemeleri). En erken uygun gün; en kârlısı farklıysa o da.
- **Sonuç yoksa sebep ayrılıyor:** `NoSafeMonth` (kazançlı ama açık
  oluşturuyor), `NotWorthIt` (hiç kazandırmıyor), `NeedsPrincipal`,
  `AlreadyClosing`.
- **12 Dönem → Kredi Kapatma kartı**, arka planda hesaplanıyor.
  *"Simülatörde dene →"* öneriyi Simülatöre koşul olarak ekleyip hesaplıyor
  (`//simulation/simulation-content?closeLoan=…&date=…`).
- Önbellek yok: sayfa her açılışta hesaplıyor, önceki hesabı iptal ediyor.
  Kanonik veride iki kredi için ~70 ms (test makinesi).

**Araştırmadaki içgörü birebir çıktı** (kanonik plan, KMH %5):

| Başlangıç | Garanti (%5,04) | Burgan (%3,63) |
|---|---|---|
| 0 | 7 Mart 2027 · 150.113 · 67.405 tasarruf | 18 Aralık 2026 · 33.177 · 3.696 |
| 5.000.000 | ilk taksit günü (7 Eki 2026) | ilk taksit günü (18 Eyl 2026) |
| −2.000.000 | NoSafeMonth | **NotWorthIt** — %3,63'lük krediyi %5'lik KMH ile kapatmak zarar |

**Emülatörde doğrulandı:** kart birim testle aynı iki öneriyi gösterdi;
Burgan için *Simülatörde dene* → koşul eklendi, 33.177 ödenecek, 3.696
tasarruf, KMH faizi değişmiyor, açık yok.

**Testler 371 → 376.** `LoanPayoffAdvisorTests` — asıl testi kuralın
**bağımsız** uygulamasıyla ölçüyor: önerilen gün geçer, bir önceki taksit
günü geçmez. Başta `InADeepDeficit` testi 6 ms'de geçti; boş listede
`Assert.All` hiçbir şey ölçmez — sayı kontrolü eklendi, gerçek öneriler
probe ile döküldü.

**`PLAN-KREDI-KAPATMA.md` tamamlandı.** Plandan bilinçli sapmalar:
yeni kaynak tipi yerine kimlik dispatch'i (K5.3), `Loan.FinalPaymentAmount`
(planda yoktu, vade kısaltma için şart), kredi düzenleme (planda yoktu,
bankadan yeniden giriş için şart), şema v14 + v15 (plan tek v14 diyordu),
öneri önbelleği yerine iptal edilebilir arka plan hesabı.

### v1.11.1 ne getirdi (düzeltme paketi)

Üç düzeltme, ikisi ayrı oturumlardan birleştirildi.

- **🐛 Kullanıcı ekranından: kapatma tutarı kaydedilemiyordu.** Kredi
  formundaki *"Tutarı aldığın gün"* alanı **kredinin çekildiği gün** diye
  okundu: kullanıcı Burgan için 57.529 TL tutarı ve 18.11.2025'i girdi, kayıt
  *"son ödenen taksitten sonra alınmış olmalı"* hatasıyla reddedildi. Tutarın
  kendisi doğruydu — bugünün tarihiyle çözülünce faiz Burgan'ın gerçek faizine
  (%3,63) oturdu. **Tarih alanı kaldırıldı**; tutar bugünün tarihiyle
  damgalanıyor, değiştirilmeden yeniden kaydedilen tutar kendi tarihini koruyor
  ve form bunu yazıyor (*"Kayıtlı tutar 13.09.2026 tarihli"*). Emülatörde
  kullanıcının girişi birebir tekrarlandı: kayıt geçti, *"aylık %3,63 · banka
  tutarından · bugün kapatma ≈ 57.529"*.
- **Kültürsüz biçimler (ayrı oturum).** Geçmiş, dönem kapatma sihirbazı,
  review servisi, kart satırı, plan/gerçek özeti Türkçe biçimleniyor. Özet
  metni `PeriodActual` ile kaydedildiği için hatalı biçim kalıcılaşıyordu.
  **`CultureFormattingSourceTests`** `src` altındaki kültüre duyarlı
  biçimleri tarayıp sağlayıcı yoksa testi düşürüyor — yedi kez parça parça
  düzeltilen hata sınıfının kalıcı korkuluğu. v1.10/v1.11 kodunda ihlal
  bulmadı. Global `DefaultThreadCurrentCulture` bilinçli olarak seçilmedi
  (Türkçe I/İ karşılaştırmalarını sessizce değiştirirdi).
- **Flaky test (ayrı oturum).** `Cancellation_ReturnsPromptly...` 75 ms'lik
  zamanlayıcı yerine importer'a girildiği sinyalini bekliyor. Tam koşu üç kez
  arka arkaya 395/395.

**Birleştirme notu:** iki oturum da v1.9.0'dan (`04a9e45`) dallanmıştı ve
değişiklikler commit'lenmemiş hâlde worktree'lerde duruyordu. Flaky düzeltmesi
commit'lenip main'e rebase edildi, kültür düzeltmesi patch olarak uygulandı.
Tek çakışma `SimulationViewModel`'deki onay tarihleriydi (v1.10.0'da aynı yer
zaten düzeltilmişti); main tutuldu, çift `LongDate` yardımcısından biri
silindi (boş tarihte eski davranış olan boş metni döndüren kaldı).

**Testler 376 → 395.**

### v1.12.0 ne getirdi (profiller)

Kullanıcı: *"Uygulama açıldığında profil soracak. Tüm uygulama datası profile
özel olacak. Yeni bir profil açtığımda Mizan yeni yüklenmiş gibi davranacak."*

Kararlar (dördü de önerilen seçenek): **korumasız** (PIN yok) · menüde
**Profil Değiştir** · **oluştur + seç + adlandır + sil** · profil yalnız
**soğuk açılışta** sorulur, arka plandan dönüşte sorulmaz.

- **Profil = ayrı veritabanı dosyası.** Tüm finans verisi zaten tek SQLite
  dosyasındaydı; `Preferences` kullanılmıyor, singleton servisler durum
  tutmuyor. Bu yüzden izolasyon şema değişikliği olmadan, hiçbir tabloya
  `ProfileId` eklemeden sağlandı: `profiles/{id:N}/coinflow.db3` +
  `profile.json` (ad, oluşturulma, son açılış).
- **`ProfileScopedCoinFlowStore`** uygulamanın gördüğü tek `ICoinFlowStore`;
  çağrıları açık profilin store'una iletir. Profil kapalıyken her çağrı hata
  verir — kapanmış bir ekrandan gelen geç çağrı başka profile yazamaz.
- **`FileSystemProfileRepository`** merkezi liste tutmaz, klasörleri okur;
  meta dosyası bozuk ama veritabanı olan klasör `Profilim` adıyla listelenir.
- **`ProfileService`**: ad boş değil, ≤ 30 karakter, Türkçe harfe duyarsız
  benzersiz ("ipek" = "İpek"); son kalan ve açık profil silinemez.
  Her açılışta yeni `SessionId`.
- **`AppShell` artık transient** — her profil açılışında yeniden kurulur;
  önceki profilin sayfa/view model durumu yeni profile taşınmaz.
  `MainPage`'deki statik "dönem sorusunu sordum" bayrağı `SessionId`'ye
  bağlandı (başka profile geçip dönünce soru yeniden gelir).
- **Geçiş:** kökteki eski `coinflow.db3` ilk açılışta `Profilim` profiline
  **taşınır** (kopyalanmaz). Kullanıcının gerçek verisi v1.12.0'ı ilk açtığında
  bu profilde olacak.
- Geliştirme sürümündeki "Verileri Sil" artık yalnız açık profili siler
  (metin buna göre değişti).

**Emülatörde yakalanan çökme:** `App` yapıcısı `ProfileSelectionPage`'i
parametre olarak alıyordu; XAML, `InitializeComponent` uygulama kaynaklarını
yüklemeden çözüldü → *"StaticResource not found for key Eyebrow"*. Eski
veritabanı dokunulmadan kaldı (taşıma sayfa yüklenince yapılıyor). Sayfa
artık `InitializeComponent`'ten sonra `IServiceProvider`'dan alınıyor;
`ProfileStartupSourceTests` sabitliyor. Bu projede yedinci kez yalnız ekranda
görülen kusur.

**Doğrulama (emülatör, dev build'in gerçek eski veritabanıyla):** taşıma →
Profilim → Plan 39.854 TL ve 09 Eylül gözlemi aynen · Deneme profili → ilk
kurulum · geri dönüş → Profilim verisi yerinde · Deneme silindi, klasörü
gitti · tek profilde "Sil" seçeneği yok · arka plandan dönüşte profil
sorulmadı · soğuk açılışta soruldu. **Release** derlemesi (kırpma açık)
ayrıca kuruldu: taşıma, profil açma ve soğuk açılış sonrası `profile.json`
okuma çalıştı.

**Testler 395 → 406.** `ProfileTests` (8): izolasyon (Ayşe/Mehmet —
birinde silmek diğerine ulaşmaz), kapalı profilde çağrı reddi, eski
veritabanının taşınması, bozuk meta kurtarma, ad kuralları, silme kuralları,
sıra/oturum, ve sarmalayıcının 40+ iletiminin her birini `DispatchProxy` ile
ölçen test (bir iletim bilerek bozulunca düştüğü görüldü).
`ProfileStartupSourceTests` (3).

**Bilinen sadeleştirmeler:** profil adı yalnız seçim ekranında değiştirilir
(menü başlığı bir sonraki açılışta güncellenir). Profil değiştirilirken
eski profilin sayfasında süren bir işlem varsa bağlantı kapanınca hata
alır ve view model'in hata satırına düşer — yanlış profile yazmaz.
Emülatörde üst çubuk koyu lacivert görünüyor; `Styles.xaml`'daki
`TargetType="Shell"` stili `ApplyToDerivedTypes` olmadan türetilmiş
`AppShell`'e uygulanmıyor olmalı. Stil koduna dokunulmadı; v1.11.1'de de
böyle olduğu **koddan çıkarım, ekranda karşılaştırılmadı**.

### v1.13.0 ne getirdi (otomatik yedek)

Kullanıcı: *"her gün sonunda backup güncelleyelim; uygulama kaldırılıp tekrar
kurulursa yedekten geri yükle veya temiz kurulum şeklinde kırılım yapalım."*

Kararlar: yedek **telefonda bir klasörde** · **her gece arka planda** ·
**son 7 yedek** · geri yükleme **ilk açılışta, hiç profil yokken** (bütün
profiller birlikte). Klasör önce İndirilenler/Mizan olarak yazıldı ve
emülatörde uçtan uca çalıştı; kullanıcı ardından **kökte kendi Mizan
klasörünü** istedi → "Tüm dosyalara erişim" izni seçildi.

- **Neden uygulama dışında:** Android kaldırılan uygulamanın klasörünü
  siler. Android 10+ izinsiz kök klasör açtırmıyor; `MANAGE_EXTERNAL_STORAGE`
  (Android 11+, sistem ayarındaki anahtar) ve 10 ve altında
  `WRITE_EXTERNAL_STORAGE` + `requestLegacyExternalStorage`. APK GitHub'dan
  dağıtıldığı için Play politikası engel değil. İznin artısı: yeniden kurulan
  uygulama eski yedekleri **kendisi listeliyor**, dosya seçici gerekmiyor.
- **Biçim:** `Mizan-yedek-YYYY-MM-DD.zip` = `mizan-backup.json` + her profilin
  `coinflow.db3`'ü. Veritabanı `VACUUM INTO` ile alınıyor (açık profil
  yazarken bile tutarlı).
- **Geri yükleme doğrulaması:** zip, manifest, `PRAGMA quick_check`, şema
  sürümü ≤ uygulamanınki. Profiller geçici klasörde açılır, hepsi geçerse
  taşınır; yabancı/bozuk dosya iz bırakmaz. Yalnız hiç profil yokken.
- **Gece görevi:** `JobScheduler` (WorkManager eklenmedi), 23:30'dan önce
  başlamaz, ≤ 3 saat içinde; persisted; her çalışmada ertesi geceye kurulur.
  Değişiklik yoksa dosya yazılmaz, son yedek klasörden silindiyse yazılır.
  Gün başına tek dosya (aynı gün üzerine yazılır), en yeni 7 kalır.
- **Dev ve kararlı ayrı klasör** (`Mizan Dev` / `Mizan`).
- Ayarlar → Yedekleme: son yedek, Şimdi Yedekle, izin yoksa uyarı + İzin Ver.

**Yakalanan üç kusur:**
1. **Parmak izi dosya zamanına bakıyordu.** Store her açılışta `settings`
   satırını aynı değerlerle yeniden yazıyor → her açılış "değişti" sayılır,
   7 yedek sınırı anlamsızlaşırdı. İlgili test Windows'ta geçti çünkü NTFS
   açık dosyanın değişiklik zamanını geç güncelliyor. Parmak izi artık
   tabloların içeriğinden; iki bilinçli bozmayla testin düştüğü görüldü.
2. **`SQLiteAsyncConnection.ResetPool()`** test temizliğinde paralel koşan
   15 ilgisiz testi düşürdü (havuz süreç genelidir). Kaldırıldı.
3. **İzin sorusu hiç çıkmadı** (emülatör): uygulamanın ilk sayfası pencereye
   bağlanmadan `DisplayAlert` Android'de sessizce düşüyor; "soruldu" bayrağı
   da önceden yazıldığı için bir daha sorulmayacaktı. Sayfa `Loaded` sonrası
   soruyor, bayrak soru gösterilince yazılıyor.

Ayrıca: MAUI `FilePicker` seçiciyi "Son dosyalar"da boş açıyordu; yerine
`ACTION_OPEN_DOCUMENT` + `EXTRA_INITIAL_URI` ile Mizan klasöründe açılan
seçici (`ActivityResults` yardımcısı, `MainActivity.OnActivityResult`).

**Emülatörde doğrulandı:** Şimdi Yedekle → dosya · uygulama kapalıyken zorla
çalıştırılan gece görevi süreci başlattı (Unchanged / Created) · kaldır →
yedek kaldı → kur → izin ekranı → listeden en yeni yedek → Plan 39.854 TL,
09 Eylül gözlemi · "Yedek Bulunamadı" · yabancı zip reddi · seçiciden vazgeç ·
izinsiz Ayarlar uyarısı · **Release** derlemesinde gece görevi yedeği.

**Testler 406 → 420.** `BackupTests` (10): açık profil varken gidiş-dönüş
(3 profil, biri hiç açılmamış), profil varken geri yükleme reddi,
yabancı/bozuk dosya, yeni şema reddi, değişiklik yoksa yazmama + günde tek
dosya, 7 yedek sınırı, yabancı dosyaya dokunmama, izinsiz durum, yeniden
kurulumda klasörden listeleme + geri yükleme, silinen yedeğin yeniden
yazılması. `BackupAppSourceTests` (4): manifest izinleri, görevin sabit Java
adı, etkinlik sonucu iletimi, kök klasör.

**Bilinen sınırlar:**
- Telefon sıfırlanır/kaybolursa yedek de gider (bulut yok). Klasör elle
  bilgisayara/Drive'a kopyalanabilir; "Başka bir dosya seç…" onu geri yükler.
- ~~Geri yükleme yalnız hiç profil yokken~~ → v1.14.0 "Yedekten Ekle" çözdü.
- Yedek şifresiz; `Mizan` klasörü dosya erişimi olan her uygulamaya açık.
- Güncelleyen kullanıcıda ilk yedek o gece (ya da Ayarlar → Şimdi Yedekle).
- `backup-state.json` kurulum başına; yeniden kurulumdan sonra Ayarlar
  "Henüz yedek alınmadı" der, klasördeki eski yedekleri göstermez.

### v1.14.0 ne getirdi (yedekten ekle)

Kullanıcı: *"profil varken de yedekten ekleme yapalım."* Kararlar: çakışan
profil **kopya olarak eklenir** (mevcuda dokunulmaz) · yedekte birden fazla
profil varsa **hangisi sorulur** (tek profil sorulmaz, "Hepsi" seçeneği var).

- Profil seçim ekranında **Yedekten Ekle** (Yeni Profil'in altında).
  Yedek seçimi ilk kurulumdaki geri yüklemeyle ortak.
- `BackupService.AddFromBackupAsync`: aynı kimlik telefonda varsa yeni
  kimlik + `"Ad (14 Eylül yedeği)"` (`CopyName`, 30 karakter sınırına
  sığmazsa ad `…` ile kısaltılır); kimlik çakışmasız ama ad çakışırsa
  `ProfileService.UniqueName` ile "Ad 2". Kopyanın oluşturulma tarihi
  bugün, son açılışı boş.
- `ProfileBackupArchive.RestoreAsync` → `ReadSummaryAsync` + `ImportAsync`
  (hedef kimlik/ad dışarıdan). İlk kurulumdaki geri yükleme de bunu
  kullanıyor. Hepsi doğrulanmadan hiçbiri taşınmaz, hedef klasör varsa
  üzerine yazılmaz.
- Yedek iki kez okunuyor (özet, sonra ekleme); ekranda önbelleğe kopyalanıyor,
  vazgeçince siliniyor. `BackupService` konumlanamayan akışı reddediyor.

**Emülatörde:** "Profilim" varken aynı profili içeren yedekten ekleme →
"Profilim (14 Eylül yedeği)", verisi tam (Plan 39.854 TL), mevcut profil
değişmedi · iki profilli yedekte seçim listesi ("telefonda var, kopya
eklenir" + Hepsi) · vazgeç → önbellekte dosya yok, profil sayısı aynı.

**Testler 420 → 424** (`BackupTests`): kopya ekleme + mevcut profilin
korunması, başka kurulumda kimliklerin korunması + ad numaralama, bilinmeyen
seçim ve bozuk veri reddi (hiçbir şey eklenmez), kopya adının sınıra sığması.

**Bilinen:** kopyanın kopyası uzun ad üretir (`Profilim (14 Eylül… (15
Eylül yedeği)`); yedekten "üzerine yazma" bilinçli olarak yok.

## Açık işler

1. **Yetim plan satırları (🟠 ölçülmedi).** `v1.3.0`–`v1.5.0` arasında ana
   sayfadan bakiye güncelleyen gerçek veritabanında, emekliye ayrılmış
   snapshot'lara bağlı ve actual'ı olmayan `period_plan_snapshots` satırları
   birikmiş olabilir. `v1.6.0` bu birikimi durdurdu (I14) ama geçmişte
   oluşanları temizlemedi. Hiçbir sorgu onları okumuyor — `ResolveOpenPlan`
   yalnız güncel snapshot'ın planına bakıyor — yani zararsız görünüyorlar.
   Karar için önce **kendi veritabanındaki satır sayısını ölçmek** gerekiyor;
   sonra bırak / `Superseded` alanıyla işaretle / sil seçeneklerinden biri.
   Silmek I6'nın ruhuna aykırı olduğu için varsayılan "bırak".

2. **Geçmiş ekranı düzgün kullanılamıyor.** Kullanıcının notu. İzolasyon
   tamamlandığına göre 2 numaranın evi netleşti; ekranın kendisi ayrı bir tur.
   `PLAN-IZOLASYON.md` "Ayrı tur" bölümüne bak.

3. **Kart harcaması gözlemi (ölçülmedi).** Dönem içi kart hareketleri
   `CreditCard.Charges`'a yazılıyor; bu bir **planlama** değişikliği ve I14
   sağlandığına göre mevcut `PeriodPlanRevision` mekanizmasının bunu doğru
   logladığı **bekleniyor** — ölçülmedi. Ölçülüp karar verilecek.

4. **12 dönem gidişat çizgisi (ayrı tur).** Gözlem tarihinden ileriye
   projeksiyon; ödenmiş yükümlülükleri hariç tutan geçici bir projeksiyon
   girdisi gerekir. Ölçülen bozulmayla aynı sınıfta risk taşıdığı için
   `v1.6.0` kapsamına alınmadı.

5. **Gecikmiş yükümlülük (🟠 bilinen sapma).** Bir krediyi/taksiti ödemezsen
   finalize sırasında gerçek vadesi sessizce yeni döneme kaydırılıyor; kullanıcı
   "gecikmiş" olduğunu göremiyor. Borç kaybolmuyor, sadece tarih bilgisi yok oluyor.
   Düzeltmek için `Loan`, `TemporaryPaymentInstallment`, `PlannedLargeExpense`
   kayıtlarına gecikme alanı + yeni bir şema sürümü + UI rozeti gerekiyor.
   (Eski notta "şema v12" yazıyordu; v12 geçici planlara gitti, v13 izolasyona
   ayrıldı — bu iş sıradaki boş sürümü alır.)
   İlgili: `src/CoinFlow.Application/Services/FinancialInstrumentReconciliationService.cs`
   **Ürün kararı bekliyor — temsil şekli seçilmeli.**

6. **Akbank PDF içe aktarma bozuk.** `PdfPigPdfTextExtractor` gerçek Akbank Axess
   ekstresinden bozuk glif döndürüyor (ToUnicode haritası olmayan subset font).
   `HasUsableText` metni kullanılabilir saydığı için akış `Unreadable` dalına
   girmiyor ve kullanıcıya *"Bu bankanın ekstre düzeni henüz otomatik tanınamadı"*
   deniyor — yanlış; `AkbankAxessStatementParser` mevcut, sorun metin çıkarmada.
   Garanti BBVA ekstresi kusursuz parse ediliyor (confidence 1.00).
   Sessizce yanlış sayı üretmiyor, kapanarak başarısız oluyor.
   **Kullanıcı düzeltmeyi erteledi.**

7. **"Dönem sonu" kart borcunu düşmüyor.** Rakam yalnızca nakdi gösteriyor;
   kartta borç varken bile artı görünebiliyor. Veri mevcut
   (`CardPaymentStatuses[].NextCarriedBalance`), sunum eksik.

8. **Kredilerde "bu taksiti ödedim" eylemi yok.** Erken ödediğin bir taksiti
   düşürmek için `NextPaymentDate`'i elle ileri alıp `RemainingInstallmentCount`'u
   1 azaltmak gerekiyor. Nakit ödeme planlarında `IsPaid` var, kredilerde yok.

9. **Simülatörde ilk maaş öncesi tarihli gider sessizce hesaba girmiyor.**
   v1.0.5'te *gelir* için düzeltilen boşluğun *gider* karşılığı. `CashPurchase`
   varsayılan tarihle (bugün) eklenince `PreFirstSalaryObligations`'a düşüyor ve
   dönem matematiğine hiç girmiyor. Gelirden farkı: giderler en azından Ana
   Sayfa'da listeleniyor, tamamen kaybolmuyor. Simülatörde uyarı yok.
   Faz 3 doğrulamasında canlı görüldü: 07.09.2026 tarihli 120.000 TL
   `CashPurchase` (ilk maaş 10.09) faiz tablosunun her satırını "Değişmiyor"
   bırakıyor ve 12 ay sonu bazla birebir aynı çıkıyor — koşul eklenmiş gibi
   görünüyor ama hiçbir şey değişmiyor.

10. **Doküman kayması.** `README.md` ve `docs/ARCHITECTURE.md` eski:
   "Veri ve migration" girişi şema v9 yazıyor (kod v15; v14 ve v15 notları
   eklendi; katman tablosu v1.12.0'da v15'e düzeltildi, "Profiller" bölümü
   eklendi), README 135 test yazıyor (gerçek 406).
   Kart faizi, ekranlar, mevcut tutar, kart sayfası ve geçici planlar
   bölümleri güncellendi; kalan sapma bu iki sayı.

11. **Faiz oranıyla oynayamıyorsun.** Kart ve KMH için tek bir varsayılan `%5`
   var (Ayarlar'dan ikisi ayrı ayrı girilebiliyor ama kart başına değil).
   Gerçek oranlar farklı — Garanti kart %3,25, KMH %4,25 — ve model bu
   karşılaştırmayı gösteremediği için "kartı KMH'dan kapatmak ucuz mu"
   sorusu yanıtlanamıyor. `BULGU-KART-FAIZI.md` sonundaki not bu boşluğu
   tarif ediyor.

12. **Gerçek veritabanında kredi anaparaları bozuk olabilir (🔴 kullanıcı eylemi).**
   v1.9.0 öncesi her kapatılan checkpoint anaparadan taksitin tamamını düştü.
   Kurtarılamaz; kredi satırında *"anaparası güncel görünmüyor"* uyarısı
   çıkarsa Finansal Yapı → Düzenle → **bankanın bugünkü erken kapama tutarı**.
   Kullanıcı Burgan için bunu 13.09.2026'da yaptı; Garanti için henüz bilinmiyor.

13. **Artık worktree'ler.** `.claude/worktrees/kind-tharp-5d47f1` ve
   `nervous-elbakyan-48fcd9` birleştirildi (v1.11.1) ama silinmedi;
   `kind-tharp`'ta commit'lenmemiş kopya duruyor. Kullanıcı onayıyla
   `git worktree remove` ve dal silme.

14. **Öneri motorunun bilinen sadeleştirmeleri.** Ufuk sonrası KMH faizi
   sayılmaz; pozitif bakiyeye mevduat getirisi verilmez (kapatmak mı, mevduatta
   tutmak mı sorusu cevaplanmaz); değişken faizli kredinin gelecek faizi
   bugünkü sabit varsayılır; ara ödemede ödenen tutar plandakinden farklı
   girilse de olay plandaki tutarla işlenir.

## Bilinen sadeleştirmeler (bug değil, kasıtlı)

- Mizan muhasebe defteri değil; yaşam gideri tek toplam bütçe
- Düz aylık faiz modeli (varsayılan %5), gerçek banka faiz mevzuatı değil
  (karşılaştırma: Garanti akdi faiz %3,25/ay)
- Projeksiyon ufku sabit 12 maaş dönemi
- Projeksiyon gelecek kart harcaması varsaymaz; kart borcu yalnızca erir
- Banka bir sonraki ekstre tarihini vermediyse genel kesim günü kuralına dönülür
- `KnownNextStatementDate` tek kullanımlıktır; tüketilince genel kurala dönülür

## Ürün invariant'ları (kalıcı referans — bozma)

| ID | Kural |
|---|---|
| I1 | Tek projeksiyon motoru: Dashboard, 12 Dönem ve Simülatör aynı sonucu kullanır |
| I2 | Maaş dönemi yarı açık: `[başlangıç, sonraki maaş)` |
| I3 | `ProjectionAnchorDate` öncesi plan dışıdır |
| I4 | Ödenmemiş yükümlülük kaybolmaz |
| I5 | Effective-dated kararlar (maaş, ödeme düzeni) immutable; üzerine yazılmaz |
| I6 | Geçmiş yeniden hesaplanmaz |
| I7 | `CarryOverDeficit` ikinci kez düşülmez |
| I8 | Kart faizi ve açık finansman faizi ayrı state |
| I9 | Kart carry faizi aynı dönemin zorunlu çıkışına yazılmaz |
| I10 | Coverage frontier boşluk/mükerrer üretmez |
| I11 | Bilinen banka tarihi genel takvim kuralını ezer |
| I12 | Finalize/apply idempotent |
| I13 | Gerçek yaşam gideri gelecek bütçeyi sessizce değiştirmez |
| I14 | Snapshot zinciri yalnız checkpoint'te ilerler; dönem içi hiçbir işlem snapshot veya donmuş plan üretmez |
| I15 | Dönem içi gözlem, checkpoint'te review'ın girdisidir; iki ayrı gerçekleşme kaydı tutulmaz |
| I16 | Her ekran tek zaman dilimine sahiptir; Ana Sayfa mevcut dönemin dışından rakam göstermez |
| I17 | Kredi taksiti ödenince kalan anaparadan yalnız anapara payı düşer; faiz türetilemiyorsa anaparaya dokunulmaz |
| I18 | Erken ödeme krediyi değiştirmez, üstüne oynatılan bir olaydır; checkpoint'te ödendiyse krediye işlenir, ödenmediyse iptal olur |

## Bu turda öğrenilen dokuz ürün kuralı

**`UpcomingPeriod` ile `PreviousPeriod` eşdeğer sunum biçimi değildir.**
Yalnızca `UpcomingPeriod`'un `EndingProjectedSavings`'i gerçek banka bakiyesine
eşittir. `PreviousPeriod`'un kapanış rakamı "birikmiş borç temizlendi" demektir;
bir sonraki pencereye atanmış ama hesaptan çoktan çıkmış ödemeleri saymaz.
Mod önerirken "fark etmez" deme.

**Kredi finansman maliyeti faizdir.** 120.000 çekip 145.000 ödüyorsan aradaki
25.000 faiz yüküdür ve faiz karşılaştırmasına dahil edilmelidir. Dönem sonu
rakamı bunu zaten içerir; tablo içermezse kredi faiz düşürüyormuş gibi görünür.

**Faiz tek uçta hesaplanır.** Kart faizi hem dönem sonunda kalana hem de
ekstreye giren bakiyeye uygulanırsa aynı faiz iki kez sayılır: kuyrukta
kapitalize olur, ertesi ekstrenin açılış bakiyesine girince tekrar faizlenir.
Bulgu belgesindeki taslak yama tam olarak bunu yapıyordu. Doğru hamle faizi
*eklemek* değil, tek bir uca — ekstrenin girişine — **taşımaktı**. Kart faizine
dokunan her değişiklikte önce "bu faiz başka nerede işleniyor" diye sor.

**Doğru aritmetik yanlış sorunun cevabı olabilir.** Gidişat üç turda üç kez
düzeltildi ve üçünde de hesap kendi içinde tutarlıydı; yanlış olan sorulan
soruydu. "Bugüne kadar ne kadar harcamış olmalıydın" diye sormak, kullanıcının
hiç sormadığı bir soruydu — o bir havuza bakıyordu, bir hıza değil. Bir rakam
kullanıcıya yanlış geliyorsa önce formülü değil, **formülün cevapladığı
soruyu** kontrol et.

**Modeli koda dokunmadan kullanıcının kendi rakamlarıyla doğrula.** Havuz
modeli yazılmadan önce kullanıcının gerçek verisiyle elle hesaplandı ve
planlanan dönem sonuna tam oturdu (-158.257). Bu, hem modelin doğru olduğunu
hem de eski modelin sapmayı *ürettiğini* kodu yazmadan gösterdi. Üç turluk
düzeltme döngüsünü bitiren şey buydu.

**Kıyaslanan iki rakam aynı kalemleri içermeli.** `v1.6.1`'de gidişat faizi
saymıyor, plan sayıyordu; "plana göre fark" bu yüzden faiz kadar kayıyordu ve
plana tam uyan kullanıcıya kâr gösteriyordu. Bir fark rakamı yazmadan önce iki
tarafın bileşenlerini yan yana yaz. Testi de bu: **aynı gidiliyorsa fark
sıfırdır.**

**Fixture'ın sıfırladığı risk test edilmiyordur.** Aynı hatayı testler
yakalayamadı çünkü kanonik seed'in faizi sıfırdı. Faiz, büyük gider, açık gibi
kalemler fixture'da sıfırsa o kalemlere bağlı hatalar görünmez; riski taşıyan
senaryoyu bilerek kur.

**Kendi invariant'ını ürünün aleyhine çevirme.** I16 "ana sayfa başka zaman
dilimlerinin rakamını göstermez" diyor. `v1.6.1`'de bunu "ana sayfa hesap
yapmaz" diye okuyup açık faizini plandan dondurdum; oysa mevcut dönemi
hesaplamak tam olarak o ekranın işiydi ve kullanıcının istediği şey de buydu.
Bir kural ürünü kötüleştiriyorsa önce kuralı doğru okuduğundan emin ol.

**Bir etiket iki şeye okunabiliyorsa kullanıcı yanlış olanı seçer.**
*"Tutarı aldığın gün"* tasarlayanın aklında "bankanın kapatma tutarını
gördüğün gün"dü; kredi formunun içinde "kredi tutarını aldığın gün" diye
okundu ve doğru bir tutar reddedildi. Kullanıcıdan istenen her alan için
"bu formda başka neye benzer?" diye sor. En iyi düzeltme etiketi
netleştirmek değil, **gereksiz soruyu kaldırmaktı**: tutar zaten görüldüğü
gün girilir.

## Rol promptları

`agents/MENTOR.md` · `agents/DEV.md` · `agents/BA.md`
(BA.md'deki "kod esastır" kuralı hatalı — kod/doküman çelişkisinde dört yollu
triyaj yapılmalı: kod bug'ı / doküman eski / kural değişti / karar eksik)
