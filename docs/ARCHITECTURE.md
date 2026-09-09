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
| `CoinFlow.Infrastructure` | SQLite şema v9, legacy upgrade ve deterministik development seed |
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

`PeriodProgressService` Ana Sayfa'nın motorudur ve yeni bir hesap yapmaz: donmuş planı (varsa en güncel `PeriodPlanRevision` uygulanmış hâlini) gözlemle toplar. Dönem sonu tahmini `gözlenen bakiye − kalan planlı ödemeler − kalan yaşam gideri` ile türetilir; gözlenen bakiye yoksa `null` döner ve ekran gidişat bloğunu hiç göstermez.

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

Store tüm entity'leri exact-date alanlarıyla round-trip eder. Şema v9; `financial_snapshots`, frozen plan/satırları, revision, actual, actual payment/flow ve optional living breakdown tablolarını additive olarak ekler. Existing kullanıcı ilk normal plan okumasında mevcut canonical durumundan initial current snapshot alır; geçmiş actual üretilmez. Şema v7'de eklenen iki global planlama faiz oranı ve eski strategy/card migration davranışları korunur. SQLite-net additive migration finansman planlarına ana tutar ve toplam geri ödeme alanlarını eski kayıtları bozmadan ekler. Legacy upgrade sırasında eksik `ProjectionAnchorDate` bir kez oluşturulur; fresh veritabanında ise ilk maaş planlamasına kadar boş kalır. Fresh development veritabanı otomatik seed edilmez. Clear aksiyonu yeni history tablolarını da temizler.
