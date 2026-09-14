# Mizan mimarisi

## Tek finansal kaynak

Mizan'ın merkezi çıktısı `SalaryPeriodProjection` modelidir. Dashboard, 12 dönemlik görünüm ve simülatör kendi formüllerini üretmez; aynı `FinancialProjectionCalculator` sonucunu kullanır.

```text
FinancialPlan
   ├─ SalaryPeriodCalculator
   ├─ ProjectionAnchorDate filter
   ├─ ProjectionBoundaryResolver (history'den ilk unrealized maaş)
   ├─ PaymentAssignmentStrategyResolver (effective-dated history)
   ├─ SalaryResolver + IncomeProjectionCalculator
   ├─ LoanScheduleCalculator
   ├─ CreditCardStatementCalculator (principal → carry interest → next carry)
   └─ ScheduledPaymentCalculator
            ↓
 SalaryFundingPlanner (coverage frontier)
            ↓
 FinancialProjectionCalculator (maaş bazında aktif düzen)
            ↓
 Dashboard / 12 Dönem / Simulator baseline + scenario
```

Current/future query'leri, latest current snapshot ve history üzerinden runtime projection boundary türetir. İlk kurulumda `ProjectionAnchorDate` anchor'daki veya sonraki ilk maaşı seçebilir; finalized `PeriodActual` sonucu oluşan snapshot'ta ise `PeriodActual.ResultFinancialSnapshotId` provenance'ı kullanılır ve ilk unrealized maaş kapatılan `PeriodEnd` checkpoint'ından strictly sonra çözülür. `NextProjectionSalaryDate` gibi ikinci bir kalıcı cursor tutulmaz; projection veya simulator için ikinci bir hesap motoru yoktur.

`SalaryPeriodDetailPresenter`, hesaplanmış `SalaryPeriodProjection` sonucunu presentation-only summary, flow, kategori, faiz, transition ve bağımsız ödeme satırlarına ayırır. Formül çalıştırmaz. 12 Dönem ve Simulator aynı `SalaryPeriodDetailPage` / `SalaryPeriodDetailViewModel` ikilisini kullanır; Simulator yalnız aynı modele baseline karşılaştırmasını ekler. Shell navigation hesaplanmış result nesnesini taşır, detail page finans motorunu yeniden kurmaz.

## Katmanlar

| Proje | Sorumluluk |
|---|---|
| `CoinFlow.Domain` | Saf modeller, tarih kuralları, projection ve simulation motorları |
| `CoinFlow.Application` | Kullanım senaryoları, CRUD, açık onaylı scenario apply ve store sözleşmesi |
| `CoinFlow.Infrastructure` | SQLite şema v15, profil klasörleri, legacy upgrade ve deterministik development seed |
| `CoinFlow.App` | .NET MAUI Android görünümü ve servis sonuçlarını sunan MVVM katmanı |
| `CoinFlow.Tests` | Domain regression, kanonik veri ve SQLite entegrasyon testleri |

Bağımlılık yönü `App → Application → Domain`; `Infrastructure → Application + Domain` şeklindedir.

## Tarih ve para kuralları

- Maaş dönemi `[başlangıç, bitiş)` semantiğine sahiptir.
- Snapshot review tarihi `SalaryPeriodCalculator.GetNextReviewDate` ile çözülür ve snapshot'tan strictly sonra gelen ilk maaş tarihidir. Review hareket penceresi `(SnapshotDate, ReviewDate]` semantiğine sahiptir; ödeme atama modu bu checkpoint'ı değiştirmez.
- Maaş dönemi ortasındaki ilk snapshot'ın yaşam bütçesi `MonthlyLivingBudget × review gün sayısı / tam maaş dönemi gün sayısı` ile iki hane `AwayFromZero` oranlanır. Maaş günündeki snapshot tam aylık bütçeyi kullanır.
- `ProjectionAnchorDate`, anchor öncesini plan dışı sayar ve ilk projection maaşını anchor'daki veya anchor sonrasındaki ilk maaş olarak belirler.
- Actual finalization sonrası current/future projection, obligation anchor'ını kapatılan checkpoint'te tutar fakat ilk projection maaşını strictly sonrasına taşır. Böylece taşınan ödenmemiş yükümlülükler kaybolmaz, kapatılmış checkpoint maaşı ise tekrar income olmaz.
- `PaymentAssignmentStrategyResolver`, her maaşta effective tarihi o maaştan büyük olmayan en yeni history kaydını seçer.
- `SalaryFundingPlanner`, son kapsanan günü izler; her maaşta yalnız yeni coverage aralığını atar. `Previous → Upcoming` geçişinde gap'i catch-up olarak dahil eder, `Upcoming → Previous` geçişinde daha önce fonlanan günleri tekrar saymaz.
- `PreviousPeriod` penceresi `(önceki maaş, mevcut maaş]` olduğundan maaş günü ödemesi hiçbir zaman bir ay geriye kaymaz.
- Maaş günü kısa ayda ayın son gününe kırpılır; aynı kural kredi ve tekrarlı ödeme tarihlerinde kullanılır.
- Dönem maaşı, dönem başlangıcında yürürlükteki son maaş kaydıdır.
- Diğer gelir ve tüm yükümlülükler exact date ile tek bir döneme girer.
- Para hesapları `decimal` ile yapılır; eşit taksitlerde kalan kuruş yalnız son taksite eklenir.
- Planlama faizleri iki haneye `MidpointRounding.AwayFromZero` ile yuvarlanır.
- Kümülatif finansal durum her dönemin `OpeningProjectedSavings` değerinden devam eder.
- Negatif opening değerinin mutlak tutarı `CarryOverDeficit`, zorunlu ödemeler sonrası alandan görünüm amaçlı düşülmüş hali `AvailableAfterCarryOverDeficit` olarak türetilir. Bunlar obligation değildir ve `EndingProjectedSavingsBeforeDeficitInterest = OpeningProjectedSavings + CurrentPeriodNetContribution` hesabında yeniden düşülmez.
- Dönem sonucu negatif kaldığında `DeficitFinancingInterest` bu negatif principal üzerinde hesaplanır; final ending'e bir kez uygulanır ve sonraki opening'e taşınır. Sonuç sıfır veya pozitifse açık faizi üretilmez.

## Kredi kartı motoru

`CreditCardStatementCalculator`, devreden borç, dönem içi harcama ve exact posting kayıtlarını kesim tarihine taşır. Ödeme kesimden sonra son ödeme tarihinde yükümlülük olur.

Kart başına gerçek ödeme stratejisi (`AskEachStatement`, asgari, tam ekstre, sabit) ile yalnız projection için kullanılabilen fallback ayrıdır. Exact due-date override varsa stratejinin önüne geçer. Sabit tutar ekstre borcunu aşamaz; asgarinin altındaysa asgariye yükseltilir. Belirsiz ödeme planı tutar uydurmaz ve açıkça işaretlenir.

Faiz, bir bakiyenin devrettiği anda değil, devrettiği bakiyenin girdiği ekstrede işlenir: `CarryInterest` = açılış carry bakiyesi × yapılandırılabilir aylık `CreditCardCarryInterestRate`, ve `StatementBalance` bu faizi içerir. Ödeme sonrası kalan principal sıfıra kırpılır ve faizsiz olarak `NextCarriedBalance` olur; faizi bir sonraki ekstre işler. Böylece faiz tek bir yerde, tek bir kez hesaplanır ve ekstresini tamamen ödeyen kullanıcı da devraldığı borcun faizini öder. Bankanın kestiği gerçek ekstre (`CurrentStatement`) nihai tutardır; faiz zaten içinde olduğu için üzerine eklenmez. Faiz aynı dönemin mandatory outflow'una ayrı kalem olarak yazılmaz; nakit etkisi ekstre ödemesinin içindedir (I9). `CarryInterest` ve `DeficitFinancingInterest` ayrı state'lerdir.

## Kredi motoru

`Loan` eşit taksitli bir annüitedir: `MonthlyPayment × RemainingInstallmentCount`, ilk taksit `NextPaymentDate`'te. `RemainingDebt` **kalan anaparadır** — `NextPaymentDate`'teki taksitten hemen önceki hâli — kalan taksitlerin toplamı değildir.

`LoanAmortizationCalculator` faizi girdi olarak almaz, **türetir**: `anapara = taksit × (1 − (1+r)^−n) / r` denkleminin (0, 1] aralığında bisection çözümü. Faiz gerçek taksitten türediği için BSMV ve KKDF zaten içindedir. Faizin iki kaynağı vardır, öncelik sırasıyla:

1. **Bankanın kapatma tutarı** (`EarlyClosureAmount` + `EarlyClosureAmountAsOf`). Form tarih sormaz: tutar yalnız görüldüğü gün geçerli olduğu için `LoanPayoffService.PrepareForSave` onu bugünün tarihiyle damgalar; değiştirilmeden yeniden kaydedilen tutar kendi tarihini korur. (v1.11.1 öncesi "Tutarı aldığın gün" alanı kredinin çekildiği gün diye okunuyordu.) `tutar = kalan anapara × (1 + r × gün ÷ 30)` ile birlikte çözülür; o güne kadar vadesi gelen taksitlerin ödendiği varsayılır. Tarihi son ödenen taksitten eskiyse bayattır, yok sayılır ve reconciliation kanonik kayıttan siler. Kaydedilirken anapara bu çözümden `RemainingDebt`'e yazılır.
2. **Kalan anapara** (`RemainingDebt`).

Korkuluklar (`LoanAnalysisIssue`): anapara ≥ kalan taksitlerin toplamıysa faiz ≤ 0 çıkar (alana toplam borç girilmiştir); türetilen faiz aylık `MaxPlausibleMonthlyRate` (%8) üstündeyse anapara güncel değildir. İkisinde de kapatma tutarı **üretilmez**, kullanıcıya bankadan tutar girmesi söylenir. Kaydederken ikisi de reddedilir.

**Kapatma tutarı** `d` gününde: o güne kadar (vade günü dahil) `k` taksit ödenmiş sayılır → `anapara_k + anapara_k × r × (d − son taksit) ÷ 30 + ücret`. Ücret 6502 sayılı Kanun'a göre: tüketici kredisinde yok (md. 27), sabit faizli konutta kalan vade ≤36 ay %1 / üstü %2 (md. 37), değişken faizli konutta yok. Faiz tasarrufu = ödenmeyecek taksitler − kapatma tutarı.

**Taksit ödenince yalnız anapara payı düşer (I17).** `FinancialInstrumentReconciliationService` `yeni anapara = anapara − (ödenen − anapara × r)` uygular. v1.9.0 öncesi taksitin tamamı düşülüyordu: 190.188 anaparalı, 22 taksitli bir kredide 12 taksit sonra gerçek anapara 111.758 iken kayıt 16.173 gösteriyordu. Faiz türetilemiyorsa anaparaya dokunulmaz — yanlış düşmektense bırakmak uyarı olarak görünür.

**Erken ödeme bir olaydır, kredi mutasyonu değil (I18).** `LoanPrepayment` (`loan_prepayments`, şema v15) krediye tarih, mod (`FullClosure` / `ReduceTerm` / `ReduceInstallment`) ve ara ödemede anaparadan düşecek tutarı ekler; kredinin kendisi değişmez. `LoanPaymentScheduleBuilder.Replay` ödeme listesini olayları sırayla oynatarak üretir: olay gününe kadar (vade günü dahil) taksitler ödenir, sonra anaparadan `X` düşer ve ödenen tutar `X × (1 + r × gün ÷ 30) + ücret` olur. Böylece `X`'in o günlere düşen faizi tahsil edilir, kalan anaparanın faizi bir sonraki taksitte her zamanki gibi ödenir — aynı faiz iki kez sayılmaz. Tam kapama `X = anapara` hâlidir ve sonraki taksitleri siler. Vade kısaltmada taksit sabit kalır ve son taksit küçülür (`Loan.FinalPaymentAmount`); taksit azaltmada kalan sayı sabit kalır, yeni taksit annüiteden gelir. Tam kapamanın tutarı saklanmaz, anapara reconcile oldukça yeniden hesaplanır. Faizi türetilemeyen kredide olaylar oynatılmaz ve bu işaretlenir.

`MandatoryPaymentCalculator` erken ödeme satırını `ObligationType.Loan` olarak üretir ama `PaymentId`'sinde olayın kimliğini taşır. Toplamlar (`PlannedLoanPayments` vb.) onu kredi ödemesi sayar; frozen plan satırı `SourceEntityId` olarak olay kimliğini alır. Checkpoint'te `FinancialInstrumentReconciliationService` kimliğe bakar: bir olaysa *ödendi* → `Replay` sonrası kredi hâli kanonik krediye yazılır; *ödenmedi* → gönüllü bir karardı, iptal olur. İki durumda da olay tüketilir (`FinancialReviewCommit.RemovedLoanPrepaymentIds`). Aynı günde önce taksit, sonra olay işlenir. Checkpoint'i geçmiş ama hiçbir satıra düşmemiş olay da iptal olur; plan donduktan sonra geri alınmış bir olayın satırı sessizce atlanır.

Simülatör `LoanEarlyClosure` ve `LoanPartialPrepayment` koşullarıyla olayı yalnız bellekteki senaryo planına ekler; `LoanPaymentScheduleBuilder.Validate` kredinin faizi türetilebilir mi, tarih son ödenen taksitten sonra ve son taksitten önce mi, aynı kredi zaten kapatılıyor mu, ara ödeme o günkü anaparadan küçük mü diye bakar. `SimulationResult.LoanImpacts` kredi başına ödenen tutarı, bitiş tarihini ve **ömür boyu** kredi faizi tasarrufunu (olaysız toplam − olaylı toplam) taşır; bu 12 dönemlik kart + KMH faiz tablosuna katılmaz. Uygulama idempotenttir (`Id = ScenarioId`) ve Finansal Yapı'dan silinerek geri alınır.

**Kapatma önerisi.** `LoanPayoffAdvisor` baz projeksiyonu bir kez hesaplar; her aktif ve faizi türetilebilir kredi için ufuktaki (ilk dönem başı ile son dönem sonu arası, son taksit hariç) her taksit gününde `LoanEarlyClosure` senaryosunu kurup projeksiyonu yeniden çalıştırır. Bir gün yalnız ikisi birden sağlanırsa önerilir: **açık yok** — her dönemde `max(0, −dönem sonu)` baz çizgidekinden büyük değil; **net kazanç** — `12. dönem sonu farkı + (olaysız ufuk dışı kredi ödemeleri − olaylı ufuk dışı kredi ödemeleri)` pozitif. İkinci terim, kazancının çoğu 12 dönemin dışında kalan uzun kredilerin haksız yere reddedilmesini önler; ufuk dışındaki ödemeler dönem `MandatoryItems`'ında görünmeyen kredi ödemeleri olarak ölçülür, böylece ödeme atama modundan bağımsızdır. Ufuk sonrası açık faizi sayılmaz (bilinen sadeleştirme). En erken uygun gün önerilir; en kârlısı farklıysa o da. Sonuç yoksa sebep ayrılır: kazançlı ama açık oluşturan gün vardı (`NoSafeMonth`) ya da hiçbir gün kazandırmadı (`NotWorthIt`) — açık faizi krediden yüksekken tipik sonuç budur. `CoinFlowService.GetLoanPayoffAdviceAsync` simülatörle aynı sınırı (`FirstUnrealizedSalaryDate`) kullanır ki önerilen gün simülatörde denendiğinde aynı rakamı versin. 12 Dönem hesabı arka planda çalıştırır; öneri `//simulation/simulation-content?closeLoan=…&date=…` ile Simülatöre koşul olarak eklenip hesaplanır.

## Simülatör

`SimulationCalculator` önce mevcut `FinancialPlan` ile baseline hesaplar, sonra yalnız bellekte scenario planı kurup aynı projection motorunu yeniden çalıştırır. Payment strategy senaryosu history kopyasına future effective kayıt ekler; önizleme veritabanına yazmaz. Bu sayede baseline ve scenario kolonları aynı anchor, coverage, tarih, kart, carry-over deficit, faiz ve finansal durum kurallarına tabidir. Risk özeti ilk deficit dönemini, maksimum devreden açığı ve recovery dönemini aynı sonuçlardan türetir. Karşılaştırma baseline/scenario kart faizini, açık faizini, ek faiz yükünü veya faiz tasarrufunu ayrı ayrı üretir; “kart ekstresini tamamen kapat” senaryosu exact due-date tam ödeme override'ı kullanır.

`SimulationDraft`, simülatörde kurulan koşul listesinin adlandırılmış ve kalıcı kopyasıdır (`simulation_drafts` + `simulation_draft_conditions`, şema v12). Projeksiyonun hiçbir yerine girmez; yalnız simülatöre geri yüklenir. Her koşul `SimulationRequest` alanlarıyla sütun sütun saklanır ve `ScenarioId` olduğu gibi korunur — apply yolu entity kimliklerini bu değerden deterministik ürettiği için, geri yüklenip ikinci kez uygulanan bir plan mükerrer yükümlülük oluşturmaz. Adı `TemporaryPaymentPlan`'dan bilinçli olarak ayrıdır: o gerçek bir borç aracı, bu bir taslak.

Senaryoyu kaydetmek ayrı bir işlemdir. `CoinFlowService.ApplySimulationAsync` açık `confirmed=true` olmadan kalıcı değişiklik yapmaz. Her hesaplanan scenario kalıcı bir application kimliği taşır; entity ve child charge/taksit kimlikleri bundan deterministik üretilir. Böylece hızlı çift tıklama veya retry aynı canonical kaydı ikinci kez oluşturmaz. Apply switch'i nakit gideri `PlannedLargeExpense`, finansmanı `TemporaryPaymentPlan`, kart alışverişini seçili `CreditCard` aggregate'ının charge'ları, gelecek geliri `OneTimeIncome`, maaş ve ödeme düzeni değişikliklerini yeni effective-dated history kayıtları olarak persist eder. Maaş/strategy geçmişi apply sırasında overwrite edilmez.

### Üç zaman dilimi

Uygulamanın üç amacı vardır ve her biri tek bir ekrana ve tek bir veri kaynağına sahiptir (I16):

| Zaman | Ekran | Kaynak |
|---|---|---|
| `t+1 … t+12` | 12 Dönem · Simülatör | `FinancialProjectionCalculator` |
| `[t0, t1)` | Ana Sayfa | donmuş `PeriodPlanSnapshot` + `PeriodObservation` |
| `< t0` | Geçmiş | `PeriodActual` + `PlanActualComparisonCalculator` |

Zaman dilimleri arasında veri yalnız **checkpoint'te** akar: dönem açılışında projeksiyon donar (`FinancialSnapshotService.Build` → `Freeze`), dönem kapanışında gözlem `PeriodActual`'a dönüşür ve onaylanan dönem sonu yeni projeksiyonun çapası olur (`FinalizePeriodReviewAsync`). Dönem içinde snapshot zinciri **ilerlemez** (I14).

`PeriodObservation`, açık dönemin gözlem defteridir (`period_observations` + payment/flow child tabloları, şema v13). Alanları `PeriodReviewDraft` ile birebir eşlenir; checkpoint'te `GetObservedReviewDraftAsync` ile review'ı doldurur ve finalization sırasında tüketilip silinir (I15). Böylece dönem içi gözlem ile dönem sonu gerçekleşmesi iki ayrı veri kümesi değildir — aynı defter, farklı zamanda okunur.

`PeriodProgressService` Ana Sayfa'nın motorudur. Donmuş planı (varsa en güncel `PeriodPlanRevision` uygulanmış hâlini) gözlemle toplar ve **bu dönemin değişebilen parametrelerini gözlenen pozisyondan yeniden hesaplar**. Sorulan soru şudur: *elimdeki bu tutarla kalan ödemeleri yapınca dönem sonu ve faiz ne olur?*

```
faiz öncesi dönem sonu = gözlenen bakiye − kalan planlı ödemeler − kalan yaşam gideri
açık faizi             = faiz öncesi < 0 ? |faiz öncesi| × DeficitFinancingInterestRate : 0
dönem sonu tahmini     = faiz öncesi − açık faizi
```

Bu, `PeriodPlanSnapshotService.Freeze`'in kullandığı kuralın birebir aynısıdır; iki taraf aynı formülü kullanmazsa "plana göre fark" yalan söyler. Gözlenen bakiye yoksa hepsi `null` döner ve ekran gidişat bloğunu hiç göstermez.

**Kart faizi nakit dönem sonuna girmez.** `PlannedEndingSavings` da onu içermez — kart carry faizi karta kapitalize olur, nakit akışına yazılmaz (I9). Ana Sayfa'nın gidişat bloğu onu hiç göstermez: kart borcuna ve ödeme kararına bağlıdır, gözlenen nakit pozisyonu onu oynatmaz. Faizin yeri 12 Dönem ekranıdır.

**Açık faizi kopyalanmaz, yeniden hesaplanır.** Kullanıcının ana sayfadan beklediği şey tam olarak budur: pozisyon kötüleşince faizin ne olacağını görmek. Plandan sabit almak, gözlemin değiştirdiği tek faiz kalemini dondurmak olurdu. I16 ana sayfanın **başka zaman dilimlerinin** rakamını göstermesini yasaklar; mevcut dönemi hesaplamasını değil — ve bu hesap için projeksiyon motoru gerekmez, tek formül yukarıdadır.

**Yaşam gideri bir havuzdur, günlük hız değil.** `PlannedLivingBudget` dönem boyunca harcanabilecek toplamdır; 20.000 planlanmış ve 15.000 harcanmışsa 5.000 bakiye kalmıştır. Harcanan, gözlenen bakiyedeki düşüşten ödenmiş plan satırları çıkarılarak bulunur:

```
harcanan = OpeningSavings + PlannedIncome − ödenmiş plan satırları − gözlenen bakiye
kalan    = max(0, PlannedLivingBudget − harcanan)
```

Havuzun **içinde** harcamak dönem sonunu değiştirmez (harcanan + kalan her zaman planlanana eşittir); yalnız havuz aşılınca fazlası dönem sonuna ve dolayısıyla açık faizine yansır. Önceki sürüm bu havuzu geçen güne bölüp "bugüne kadar şu kadar harcamış olmalıydın" diye kıyaslıyordu ve plana tam uyan kullanıcıya yoktan sapma üretiyordu. Kart harcaması bu havuza girmez — kartın içinde ekstre tarihiyle yönetilir (I9).

**Vadesi geçen plan satırı ödenmiş sayılır.** Bir satır iki yoldan "yapılmış" olur: gözlem defterinde açıkça işaretlenmişse, ya da `PlannedDate <= today` ise. İkincisi planın kendi varsayımıdır — karta ekstre kesilmeden ödeme yapılmaz, vade günü gelen ödeme de yapılır. Kullanıcıdan her satır için ayrıca onay istemek gereksizdir; gerçekleşen tutar zaten sonraki checkpoint'in review'ında kaydedilir.

Ekran tek bir fark rakamı değil, **parametre başına karşılaştırma** gösterir: yaşam gideri (planlanan / harcanan / kalan), kartlar (planlanan ekstre / mevcut ekstre, kart başına), açık faizi ve dönem sonu (planlanan / mevcut). Açık faizi satırı yalnız gerçekten açık varsa açılır — sıfır yazmak için alan ayrılmaz. Ana Sayfa, Geçmiş'in dönem kapanmadan önceki hâlidir.

`PeriodPlanSnapshot` şu kimliği sağlar:

```
faiz öncesi          = OpeningSavings + PlannedIncome
                     − PlannedMandatoryPayments − PlannedLivingBudget
                     − PlannedLargeExpenses
PlannedDeficitInterest = faiz öncesi < 0 ? |faiz öncesi| × oran : 0
PlannedEndingSavings = faiz öncesi − PlannedDeficitInterest
```

`PaymentLines` hem zorunlu ödemeleri hem planlı büyük giderleri taşır; toplamları `PlannedMandatoryPayments + PlannedLargeExpenses`'a eşittir, çift sayım yoktur.

`RefreshCurrentFinancialStateAsync` yalnız checkpoint yolundan çağrılır (kurulum ve review finalization). Dönem içinde çağrılırsa açık planın penceresi kısalır, orijinal plan yetim kalır ve plan/gerçek karşılaştırması anlamsızlaşır — ölçülmüş bir bozulmadır, `PLAN-IZOLASYON.md`.

Kart kontrol ekranındaki dört ödeme kararı ayrı kapsamlara sahiptir ve model tarafında da ayrıdır: `CurrentStatementPaymentPlan` (tek, kesilmiş ekstre), `CreditCardPaymentPlan` (tek, belirli bir vade), `PaymentStrategy` (tüm gelecek ekstreler), `ProjectionFallbackStrategy` (karar verilmemiş ekstrelerde hesaplama varsayımı — bir ödeme kararı değil). Sunum bu kapsamları zaman eksenine göre gruplar; `CreditCardPaymentResolution` hangi kapsamın geçerli olduğunu döndürdüğü için her satır kararın nereden geldiğini yazabilir.

Kart ve ödeme planı aggregate upsert'leri SQLite transaction içinde ana kayıt ve tüm child satırları birlikte yazar. Apply sonucu hedef bölüm ve entity kimliğini UI'a döndürür; Finansal Yapı sayfası `OnAppearing` sırasında canonical store'u yeniden okur ve istenen gelir/ödeme bölümünü, kart işlemlerinde ise kart kontrol ekranını açar. Projection katmanında cache bulunmadığından Dashboard, 12 Dönem, Target Amount ve sonraki simulator baseline her çağrıda güncel canonical planı kullanır.

12 Dönem ve Simulator, Dönem Detayı'ndan geri dönüşte collection'ı yeniden üretmez; mevcut page instance ve scroll/scenario state korunur. Başka bir kök ekrandan geri gelindiğinde normal canonical reload davranışı devam eder.

## Aylık snapshot ve history

```text
Current FinancialSnapshot
        ↓ aynı FinancialProjectionCalculator
Frozen PeriodPlanSnapshot
        ↓ review tarihi
PeriodActual + optional PeriodPlanRevision
        ↓ FinancialInstrumentReconciliationService
Canonical kart/kredi/plan state + yeni UserSettings baseline
        ↓ tek transaction
New Current FinancialSnapshot + New Frozen Plan
```

- `PeriodPlanSnapshotService`, snapshot→ilk sonraki maaş checkpoint aralığını doğrudan dondurur. Ödeme adaylarını mevcut projection/kart motorundan alır, yalnız `(SnapshotDate, ReviewDate]` satırlarını tutar ve ilk kısmi yaşam bütçesini oranlar. Bu historical pencere 12 Dönem projection dönemlerini değiştirmez.
- `PeriodReviewService`, due kontrolü, actual doğrulaması ve idempotent finalization orkestrasyonunu yapar.
- `ProjectionBoundaryResolver`, latest current snapshot'ın bir `PeriodActual.ResultFinancialSnapshotId` sonucu olup olmadığını history'den anlık çözer. Actual-generated snapshot için closed checkpoint `PeriodActual.PeriodEnd`, first unrealized salary ise salary calendar'da strictly sonraki maaştır.
- `FinancialStateReconciliationService`, başlangıç durumu semantiğini değiştirmeden dönem sonu önerisini hesaplar.
- `CreditCardActualPaymentReconciler`, actual kart ödemesini exact due-date statement ile eşler; canonical karta yalnız kalan principal'i yazar. Faizi kapitalize etmez — onu bir sonraki ekstre işler, aksi halde aynı faiz iki kez sayılır.
- `FinancialInstrumentReconciliationService`, ödenen kredi/taksitleri ilerletir; ödenmeyen veya kaçırılmış yükümlülükleri yeni anchor'a taşıyarak gelecek plandan kaybolmalarını engeller.
- `PlanActualComparisonCalculator` ve `HistoryQueryService` yalnız frozen tarihsel veriyi okur. Gelecek ayar değişiklikleri eski planı yeniden hesaplamaz.

SQLite finalization transaction'ı source snapshot'ın hâlâ current olduğunu ve plan için actual bulunmadığını kontrol eder. Unique `PeriodPlanSnapshotId` indeksi hızlı çift dokunma/retry durumunda ikinci actual ve snapshot oluşmasını engeller.

Normal veya gecikmiş finalization yeni snapshot'ı cihazın açıldığı güne değil planın `ReviewAvailableFrom` checkpoint'ına yazar. Böylece 20 Ağustos → 10 Eylül → 10 Ekim zinciri korunur. Tarih ileri alınırsa pending review due olur fakat actual otomatik üretilmez. Henüz actual'ı olmayan eski hatalı current plan, `ReplacePendingFinancialSnapshotPlanAsync` transaction'ıyla yeniden dondurulabilir; completed historical planlar değiştirilemez.

## Veri ve migration

Store tüm entity'leri exact-date alanlarıyla round-trip eder. Şema v9; `financial_snapshots`, frozen plan/satırları, revision, actual, actual payment/flow ve optional living breakdown tablolarını additive olarak ekler. Existing kullanıcı ilk normal plan okumasında mevcut canonical durumundan initial current snapshot alır; geçmiş actual üretilmez. Şema v7'de eklenen iki global planlama faiz oranı ve eski strategy/card migration davranışları korunur. SQLite-net additive migration finansman planlarına ana tutar ve toplam geri ödeme alanlarını eski kayıtları bozmadan ekler. Legacy upgrade sırasında eksik `ProjectionAnchorDate` bir kez oluşturulur; fresh veritabanında ise ilk maaş planlamasına kadar boş kalır. Fresh development veritabanı otomatik seed edilmez. Clear aksiyonu yeni history tablolarını da temizler. Şema v14 `loans` tablosuna `Kind` (varsayılan tüketici kredisi) ve `EarlyClosureAmountAsOf` sütunlarını additive ekler; tarihsiz eski kapatma tutarları hiçbir şeyi kalibre etmez. Şema v15 `loan_prepayments` tablosunu, `loans.FinalPaymentAmount` ve simülasyon taslak koşullarına `LoanId` / `PrepaymentMode` sütunlarını additive ekler; bir kredi silinince olayları da silinir.

## Profiller

Profil = ayrı veritabanı dosyası. Finans verisinin tamamı zaten tek SQLite dosyasında durduğu için izolasyon şema değişikliği gerektirmez; hiçbir tabloya `ProfileId` sütunu eklenmez.

```text
AppDataDirectory/
  profiles/
    {profileId:N}/
      coinflow.db3    ← o profilin bütün finans verisi (şema v15)
      profile.json    ← ad, oluşturulma, son açılış
```

- **`FileSystemProfileRepository`** profilleri klasörlerden okur; merkezi bir liste yoktur, dolayısıyla "listede var ama verisi yok" durumu oluşamaz. Meta dosyası okunamayan ama veritabanı olan klasör `Profilim` adıyla yine listelenir.
- **`ProfileScopedCoinFlowStore`** uygulamanın gördüğü tek `ICoinFlowStore`'dur ve her çağrıyı açık profilin `SqliteCoinFlowStore`'una iletir. Servisler singleton olduğu için yeniden kurulmaz; altlarındaki veritabanı değişir. Hiçbir profil açık değilken her çağrı hata verir — kapanmış bir ekrandan gelen geç çağrı başka profile yazamaz.
- **`ProfileService`** kuralları taşır: ad boş olamaz, en fazla 30 karakter, Türkçe büyük/küçük harfe duyarsız benzersiz; son kalan ve açık olan profil silinemez. Her açılışta yeni bir `SessionId` üretir ("bu oturumda bir kez sor" kararları buna bağlıdır).
- **`ProfileNavigator`** (App) kök sayfayı değiştirir. Uygulama her soğuk açılışta `ProfileSelectionPage` ile başlar; profil açılınca `AppShell` **her seferinde yeniden** kurulur (transient), böylece önceki profilin sayfa ve view model durumu taşınmaz. "Profil Değiştir" önce ekranı seçim sayfasına alır, sonra bağlantıyı kapatır. Arka plandan dönüşte kök sayfa değişmediği için profil yeniden sorulmaz.
- **Profil öncesi sürümden geçiş.** Kökte `coinflow.db3` varsa ilk profil listelemesinde `Profilim` profiline taşınır (dosya kopyalanmaz, yerinden alınır; varsa SQLite yan dosyaları önce). Taşıma meta yazımından önce yapılır; ikisi arasında kesilirse klasör kurtarma kuralıyla yine listelenir.

## Yedekleme

Uygulama kaldırılınca Android uygulamanın kendi klasörünü (bütün profil veritabanları dahil) siler. Yedek bu yüzden uygulamanın dışında, depolamanın en üstündeki **`Mizan`** klasöründe durur (geliştirme sürümü: `Mizan Dev`, aynı telefonda iki sürüm birbirinin yedeğinin üzerine yazmasın diye).

```text
/storage/emulated/0/Mizan/
  Mizan-yedek-2026-09-14.zip
    mizan-backup.json          ← biçim, tarih, şema, profil listesi
    profiles/{id:N}/coinflow.db3
```

- **`ProfileBackupArchive`** (Infrastructure) bütün profilleri tek zip'e yazar. Veritabanı dosyası kopyalanmaz: açık profil o anda yazıyor olabilir, `VACUUM INTO` tek okuma işleminde tutarlı bir anlık görüntü üretir. Geri yükleme yalnız hiç profil yokken çalışır; zip, manifest, her veritabanının `quick_check`'i ve şema sürümü (uygulamanınkinden yeni olamaz) doğrulanır, profiller önce geçici klasöre açılır ve ancak hepsi geçerse yerine taşınır.
- **Parmak izi içerikten hesaplanır**, dosya zamanından değil: store her açılışta ayar satırını aynı değerlerle yeniden yazıyor. Son açılış tarihi parmak izine girmez.
- **`BackupService`** (Application): gün başına bir dosya (aynı gün üzerine yazılır), değişiklik yoksa gece yeni dosya yazılmaz (son yedek klasörden silinmişse yazılır), en yeni 7 yedek kalır, Mizan'ın adlandırmadığı dosyalara dokunulmaz. Yedek önce önbellekte oluşturulur, klasöre geçici adla kopyalanıp yerine taşınır.
- **`FolderBackupStorage`** düz dosya işlemi; izin kısmı **`AndroidStorageAccess`**: Android 11+ "Tüm dosyalara erişim" (`MANAGE_EXTERNAL_STORAGE`, sistem ayarındaki anahtar), 10 ve altı `WRITE_EXTERNAL_STORAGE` + `requestLegacyExternalStorage`. İzin sayesinde yeniden kurulan uygulama da klasördeki eski yedekleri listeleyebilir.
- **`NightlyBackupJob`** — JobScheduler, ek kütüphane yok. Tek seferlik görev 23:30'dan önce başlamaz, en geç 3 saat içinde çalışır, bitince ertesi gece için yeniden kurulur; `persisted` olduğu için telefon yeniden başlayınca da durur. Uygulama açılışında (`MainApplication.OnCreate`) kurulu değilse kurulur. Java adı sabittir (`com.coinflow.mobile.NightlyBackupJob`); kalıcı görev sınıf adıyla saklanır.
- **Akış.** Profil seçim ekranı izin yoksa kurulum başına bir kez izin ister (sayfa pencereye bağlandıktan sonra; ilk sayfada erken gösterilen uyarı Android'de düşüyor). Hiç profil yokken "Yedekten Geri Yükle / Temiz Başla"; geri yükleme klasördeki yedekleri tarihleriyle listeler, "Başka bir dosya seç…" sistem seçicisini `Mizan` klasöründe açar (`ActivityResults` + `EXTRA_INITIAL_URI`). Ayarlar'da son yedek, "Şimdi Yedekle" ve izin yoksa "İzin Ver".
- **Profil varken "Yedekten Ekle"** (`BackupService.AddFromBackupAsync`). Önce yedek seçilir, sonra içindeki profil (tek profilse sorulmaz; birden fazlaysa biri ya da "Hepsi"). Mevcut hiçbir profile dokunulmaz: yedekteki profil telefonda zaten varsa (aynı kimlik) yeni kimlikle "Ad (14 Eylül yedeği)" adıyla **kopya** olarak eklenir; ad başka bir profille çakışırsa "Ad 2". Ad sınırına sığmazsa ad kısaltılır. İlk kurulumdaki geri yükleme ile ekleme aynı `ProfileBackupArchive.ImportAsync` yolunu kullanır: hedef kimlik ve ad dışarıdan verilir, hepsi doğrulanmadan hiçbiri taşınmaz, hedef klasör varsa hiçbir şeyin üzerine yazılmaz. Yedek iki kez okunduğu (özet, sonra içe aktarma) için önce önbelleğe kopyalanır.
