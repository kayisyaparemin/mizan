# Mizan — Proje Durumu ve Devir Notu

> Son güncelleme: 18.09.2026 · Son sürüm `v1.18.3`
>
> **Yeni bir sohbet/geliştirici buradan başlar.** Bu dosya tek devir
> belgesidir; eski `HANDOFF.md` ve `TODO.md` kaldırıldı, hâlâ geçerli olan kısımları
> burada (TODO tamamen bitmişti). Önce "Devir" bölümünü, sonra "Açık işler"i ve "Ürün invariant'ları"nı
> oku. Sürüm bölümleri (v1.2.0 → v1.18.3) geriye dönük kayıttır; yalnız
> dokunacağın alanın bölümünü oku.

## Devir

### Şu anki durum

| | |
|---|---|
| Branch | `main`, `origin/main` ile eşit, worktree yok (`git worktree list` yalnız `main`) |
| Son sürüm | `v1.18.3` — Hatırlatıcı Cevapları & Bakiye Zaman Uyumu (`Mizan-1.18.3.apk`) |
| Testler | 584/584 (`dotnet test`, ~8 sn) |
| Android Release build | 0 uyarı, 0 hata |
| Şema | v17 (`SqliteMizanStore.CurrentSchemaVersion`) |
| Veri yeri | profil başına `files/profiles/{id:N}/coinflow.db3` (v1.12.0'dan beri) |
| Yedek yeri | `/storage/emulated/0/Mizan/Mizan-yedek-YYYY-MM-DD.zip` (dev build: `Mizan Dev`), v1.13.0'dan beri |

**Devralırken bekleyen iş yok.** v1.17.0 hatırlatıcıya iki isteği riske
göre dört fazda getirdi (ayrıntı "v1.17.0 ne getirdi"). v1.16.0'da olduğu
gibi kullanıcı soru sorulmamasını, önerilen seçeneklerle ilerlenmesini
istedi; alınan ürün kararları o bölümde "Kararlar" altında yazılı,
kullanıcı değiştirmek isteyebilir. Yeni açık işler 18–19.

### Kullanıcıyla çalışma biçimi

- **Dil Türkçe.** Kullanıcı 4 yıllık .NET backend geliştiricisi, MAUI/istemci
  tarafında yeni. Kod AI ile üretildi; hedefi kodu kendi başına anlayıp
  geliştirebilmek. Açıklamaları gerçek dosya yollarıyla bağla. "Bu ne?" diye
  sorulduğunda kod yazma, anlat. Değişikliği ancak kullanıcı istediğinde yap.
- **Özellik isteklerinde** önce ölç, sonra ürün kararlarını 2–4 seçenekli soru
  olarak sor; önerdiğini ilk sıraya koy ve "(Önerilen)" yaz. Kullanıcı genelde
  önerileni seçiyor ama kararı kendisi vermek istiyor.
- **Tipik tur:** plan → onay → uygulama → test → emülatörde uçtan uca doğrulama
  → DURUM'a "vX.Y.Z ne getirdi" bölümü → tag + push. Kullanıcı "yeni versiyon
  çıkalım" dediyse sürüm çıkarmak işin parçası.
- **Paralel sohbetler:** kullanıcı aynı anda başka Claude oturumları
  çalıştırabiliyor. Onların düzeltmeleri `.claude/worktrees/<ad>` altında
  commit'lenmemiş kalabiliyor (v1.11.1'de yaşandı). Sürümden önce
  `git worktree list` çalıştır.
- **Doküman otorite değil, ipucu.** Kod ile doküman çelişirse dört yollu triyaj
  yap: kod bug'ı / doküman eski / kural değişti / karar eksik. Kullanıcıya farkı
  söyle.

### Ortam ve komutlar (Windows, doğrulanmış)

Hepsini **repo kökünden** çalıştır. `global.json` SDK'yı 8.0.424'e sabitliyor;
repo dışından çalıştırırsan makinedeki .NET 10 seçilir.

```powershell
dotnet test tests/Mizan.Tests/Mizan.Tests.csproj -c Release
dotnet build src/Mizan.App/Mizan.App.csproj -c Release
# Dev build'i açık emülatöre kur (paket: com.coinflow.mobile.dev, ad "Mizan Dev")
dotnet build src/Mizan.App/Mizan.App.csproj -f net8.0-android -c Debug -p:MizanDevBuild=true -p:EmbedAssembliesIntoApk=true -t:Install
emulator -avd mizan34
adb shell monkey -p com.coinflow.mobile.dev -c android.intent.category.LAUNCHER 1
```

- Domain, Application, Infrastructure ve test projeleri düz `net8.0`; testler
  Android araç zinciri gerektirmez. Yalnız `Mizan.App` `net8.0-android`.
- Test projesi App'e referans vermez. App davranışı (XAML, menü seçenekleri)
  **kaynak testleriyle** sabitlenir: `*SourceTests.cs` dosyaları repo kökünü
  `CoinFlow.sln`'den bulup dosyayı metin olarak okur.
- **Emülatör otomasyonu:**
  - Ekran 1080×2400; ekran görüntüsü küçültülmüş gelir, koordinatı 1,2 ile çarp.
  - Soğuk açılış sırası: profil seçim ekranı (kart ≈ 400,728) → varsa "Geçen dönemi
    güncelleyelim mi?" (Daha Sonra ≈ 466,1364) → menü (≈ 72,202).
  - Klavyeyi `adb shell input keyevent 4` kapatır; `111` kapatmaz ve sonraki
    dokunuş klavyeye gider.
  - Veritabanı: `adb shell "run-as com.coinflow.mobile.dev sqlite3 files/profiles/<id>/coinflow.db3 '…'"`.
  - Emülatör anlık görüntüye dönebilir; önceki oturumun verisi kaybolmuş
    olabilir. Şu an dev profilinde test için eklenmiş "Bilgisayar" kart
    harcaması (30.000 · 3 taksit) ve "Prim" geliri (20.000) var.
- Kanonik seed yalnız dev build'de, Ayarlar → Seed Data Yükle ile yüklenir.
  Testler `TestFactory.Service(store, new DateOnly(2026, 8, 20))` ve
  `LoadCanonicalDevelopmentDataAsync()` ile kurar.

### Sürüm çıkarma kontrol listesi

1. Tam test paketi yeşil ve Android Release build'de 0 uyarı.
2. Değişikliği emülatörde gör. Bu projede sekiz kusur yalnız ekranda
   yakalandı: kültürsüz tarih, taşan/görünmeyen kontrol, düşen diyalog.
3. **`.github/workflows/release.yml` → "Write stable release notes" adımını
   yeni sürüm için yeniden yaz.** Not metni sabit; unutulursa yayına bir önceki
   sürümün notu çıkar (v1.0.4 ve v1.15.0'da oldu).
4. `docs/DURUM.md`: bu tablo, sürüm tablosu ve "vX.Y.Z ne getirdi" bölümü.
   Ekran/mimari değiştiyse `docs/README.md` ve `docs/ARCHITECTURE.md`.
5. `git worktree list`: bekleyen paralel iş var mı?
6. `git tag -a vX.Y.Z -m "Mizan X.Y.Z — …"` → `git push origin main` →
   `git push origin vX.Y.Z`. Tag `release.yml`'i tetikler: test, imzalı APK,
   GitHub Release. `main` push'u ayrıca `dev-build.yml` ile `dev-latest`
   ön-sürümünü günceller.
7. `gh run watch <id> --exit-status`, ardından
   `gh release view vX.Y.Z --json assets,body` ile APK'yı ve notu kontrol et.

Commit mesajları İngilizce özet satırı + Türkçe gövde biçiminde (`git log`'a bak).

### Kod haritası

| Nerede | Ne |
|---|---|
| `src/Mizan.Domain/Calculations/` | Saf motorlar: `FinancialProjectionCalculator` (tek projeksiyon, I1), `SimulationCalculator` (senaryo türleri + `Validate`), `CreditCardStatementCalculator`, `LoanAmortizationCalculator`, `LoanPaymentScheduleBuilder` |
| `src/Mizan.Application/Services/MizanService.cs` | UI'ın gördüğü cephe (~1.900 satır): plan okuma, projeksiyon, simüle/uygula, doğrudan giriş, review |
| `src/Mizan.Application/Services/` | `PeriodProgressService` (Ana Sayfa), `PeriodReviewService`, `LoanPayoffService` / `LoanPayoffAdvisor`, `BackupService`, `ProfileService` |
| `src/Mizan.Application/Models/SimulationScenarioCatalog.cs` | Plan türü grupları, tür çözümü, türün simülatör dışındaki yeri (v1.15.0) |
| `src/Mizan.Application/Models/FinancialRecordEntryCatalog.cs` | Finansal Yapı "+ Ekle" türleri ve grupları (v1.16.0) |
| `src/Mizan.Application/Services/PaymentReminderPlanner.cs` | Ödeme hatırlatıcısı takvimi, kartın gün satırları, erteleme saati, `DueKey` (saf); ödemeleri `MizanService.GetUpcomingPaymentDuesAsync`, kartın tamamını `GetPaymentReminderBoardAsync` toplar (v1.16.0, v1.17.0) |
| `src/Mizan.Application/Services/PaymentReminderPayload.cs` | Bildirimin taşıdığı ödemeler ve "Ödedim" / "Ertele" cevap kuyruğunun düz metin biçimi (v1.17.0) |
| `src/Mizan.Infrastructure/Persistence/` | `SqliteMizanStore` (şema + migration), `ProfileScopedMizanStore` (açık profile iletir), `DevelopmentDataSeeder`, `FileSystemProfileRepository`, `ProfileBackupArchive` |
| `src/Mizan.App/Pages` · `ViewModels` · `Controls` | MAUI sayfaları ve MVVM (CommunityToolkit). `Controls/ScenarioConditionFormView` Simülatör ile Finansal Yapı'nın ortak koşul formu; `EntryTypePickerView` ikisinin ortak tür seçicisi; `PaymentReminderCardView` hatırlatıcı kartı; `PaymentReminderPaidView` "Ödediklerin" |
| `src/Mizan.App/Platforms/Android/PaymentReminders.cs` | Hatırlatıcı alarmı, bildirimi ve düğmeleri, cevap alıcısı ve kuyruğu, yeniden başlatma alıcısı (v1.16.0, v1.17.0) |
| `tests/Mizan.Tests/` | Domain, SQLite entegrasyon ve kaynak testleri. Sözleşme testleri: `ProductContractInvariantTests`, `CultureFormattingSourceTests`, `ScenarioDirectEntryTests` (parite), `PlaceholderSourceTests`, `PeriodHistoryTests` (geçmiş dönem tarih/tutar) |

### Tuzaklar (bu projede gerçekten yaşandı)

- **Kültür:** kullanıcıya görünen her tarih/tutar `TurkishCulture` ile
  biçimlenir. Varsayılan kültürde ay adı İngilizce çıkıyor.
  `CultureFormattingSourceTests` sağlayıcısız biçimi düşürür. Global kültür
  bilinçli olarak ayarlanmadı: Türkçe I/İ karşılaştırmalarını bozar.
- **Store'a yeni metot** eklenirse `ProfileScopedMizanStore` iletimini de
  ekle (`ProfileTests` her iletimi ölçer). Singleton servislere durum koyma;
  profil değişince servisler yeniden kurulmaz.
- **XAML:** bir elemana `BindingContext="{Binding X}"` verirsen aynı elemanın
  `IsVisible` gibi diğer bağlamaları da X'e bakar. Görünürlüğü dıştaki bir
  `ContentView`'a koy (`CommitmentsPage.xaml`'daki ortak form örneği).
- **Renk:** Finansal Yapı formu `SoftSky` zeminde durur; `SecondaryButton` da
  `SoftSky` olduğu için o zeminde görünmez olur.
- **Kimlik ve idempotency:** simülasyon/doğrudan giriş kayıtları `ScenarioId`'den
  deterministik kimlik alır. Uygulanmış bir koşulun türünü değiştirme;
  `FindAppliedSimulation` onu tanımaz ve ikinci kez kaydeder.
- **Fixture'ın sıfırladığı risk test edilmez:** kanonik seed'de faiz sıfırdı ve
  gidişat hatası testlere düşmedi. Riski taşıyan senaryoyu bilerek kur. Yeni bir
  testi bir kez bilerek bozup düştüğünü gör.
- **Pencere sınırı günleri:** donmuş plan `(snapshot, checkpoint]` okur,
  projeksiyon `>= çapa`. Bir kaydı tam checkpoint gününe taşıyan kod onu yeni
  dönemin planından düşürür (v1.16.0'da bulunan hata). Tarih taşıyan her
  değişiklikte iki ucu da test et.
- **Debug derlemesini emülatöre kurmak:** `-t:Install` assembly'leri cihazdaki
  `files/.__override__` klasörüne yazar (fast deployment). O klasörü silersen
  uygulama açılışta *"No assemblies found"* diye kapanır; derleme değişmediyse
  yeniden kurmak da doldurmaz. `-p:EmbedAssembliesIntoApk=true` ile kur.
- **Alarm penceresi:** `SetAndAllowWhileIdle` Android 14'te bir saatlik pencere
  alıyor (`adb shell dumpsys alarm` → `window=+1h`). Kesin alarm izni olmadan
  `SetWindow` ile 10 dakika. Emülatörde saati ilerletmek için `adb root` +
  `adb shell settings put global auto_time 0` + `adb shell "date MMDDhhmmYYYY.ss"`.
- **Bash heredoc'ta `\\`:** araç çift ters eğik çizgiyi teke indirebiliyor;
  C# regex içeren dosyaları heredoc yerine dosya yazma aracıyla yaz.
- **Dönem içinde `RefreshCurrentFinancialStateAsync` çağırma:** checkpoint
  işlemidir, açık planı bozar (I14).
- **Plan revizyonu satır kimliklerini yeniler.** `HistoricalPlanRevisionService`
  revizyon satırlarına `Guid.NewGuid()` verir. Bir plan satırına kimlikle
  bağlanan her kayıt (gözlem defteri dahil) ilk revizyonda eşleşmesini
  kaybeder; v1.17.0'ın hatırlatıcı defteri bu yüzden kaynak + vadeyle
  bağlanır (açık iş 18).
- **Testi bilerek bozarken dosyayı `git checkout` ile geri alma.** Dosyadaki
  commit'lenmemiş değişiklikleri de siler (v1.17.0'da yaşandı; bozmadan önce
  alınan yedekle kurtarıldı). Önce kopyala ya da commit'le.
- **`ProfileTests` sarmalayıcı testi** her store metodunu sentinel
  argümanlarla çağırır; yeni bir parametre türü (v1.17.0'da `string` ve
  `IReadOnlyList<T>`) eklenirse `SentinelFor`'a da ekle.
- **Emülatörde uzun sayfada hedef bulmak:** kaydırma adımı kaba;
  `adb shell uiautomator dump /sdcard/ui.xml` ile metnin `bounds`'unu oku.
- Aşağıdaki sürüm bölümlerinde anılan `PLAN-*.md`, `DEVIR-FAZ3.md`,
  `BULGU-KART-FAIZI.md`, `agents/*.md` ve `CLAUDE.md` **repoda yok** (hiç
  commit'lenmemiş yerel dosyalardı). Gerekli bilgi bu dosyada ve
  `ARCHITECTURE.md`'de.

### Sıradaki olası işler

Öncelik kullanıcının. Somut adayları "Açık işler" altında: Geçmiş ekranı (2),
gecikmiş yükümlülüğün temsili (5, ürün kararı bekliyor), dönem sonunun kart
borcunu göstermemesi (7), kredide "bu taksiti ödedim" (8), simülatörde ilk maaş
öncesi gider uyarısı (9), kart başına faiz oranı (11), hatırlatıcının gece
yenilenmesi (16), dönem sihirbazının gözlemi kullanmaması (17), gözlem
defterinin plan revizyonunda eşleşmesini kaybetmesi (18), Dönem Detayı'nda
ödenen satırın işaretsiz görünmesi (19). Akbank PDF içe aktarma (6)
kullanıcı tarafından ertelendi.

## Sürüm geçmişi

İlk tur (06–07.09.2026): `v1.0.3` → `v1.5.0`, 29 commit, 11 sürüm. Altı düzeltme, üç özellik, bir
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
| **v1.8.0** | **Yaşam gideri bir havuz** — günlere bölmek plana uyana hayalet sapma üretiyordu |
| **v1.9.0** | **Doğru kredi durumu** — checkpoint taksitin tamamını anaparadan düşüyordu; faiz taksitten türetiliyor |
| **v1.10.0** | **Krediye erken kapama ve ara ödeme** simülatörde; erken ödeme kredinin üstüne oynatılan bir olay |
| **v1.11.0** | **Kredi kapatma önerisi** — 12 Dönem'de "şu gün kapatabilirsin" |
| v1.11.1 | Kapatma tutarı formu tarihi yanlış okunuyordu · kültürsüz biçimler · flaky test |
| **v1.12.0** | **Profiller** — her profil ayrı veritabanı |
| **v1.13.0** | **Gece otomatik yedek** ve yeniden kurulumda geri yükleme |
| **v1.14.0** | **Yedekten ekle** — profil varken yedekteki profili kopya olarak ekleme |
| **v1.15.0** | **Plan türleri gruplandı, Finansal Yapı'dan doğrudan giriş** — simüle edilen her tür aynı sonuçla doğrudan girilebiliyor |
| **v1.16.0** | **Ödeme günü hatırlatıcısı** (rahat / agresif) · Finansal Yapı ekleme alanı simülatör tasarımında · placeholder'lar · atıl kod temizliği · ödenmeyen borç yeni dönemin planından düşüyordu |
| **v1.17.0** | **Bildirimde "Ödedim" / "Ertele"** · ertelenenler kartta kırmızı, ödenenler "Ödediklerin"de yeşil · deneme bildirimi · kart 15 Eylül'de 18 Eylül'e "bugün" diyordu |
| **v1.18.0** | **Clean Architecture & Modern MVVM Mimarisi** · 350 satır sınıf limiti (%100 uyum, 0 kural ihlali) · Segregated Repositories (10 ISP arayüzü) · UI/Navigasyon soyutlaması (`INavigationService`) · 576/576 test yeşil |
| v1.18.1 | ANR Bug Fix — `UserFeedbackService` ve `MauiNavigationService` UI thread kilitlenme çözümü |
| **v1.18.2** | **Açılış Çökmesi Çözümü & İsimlendirme Refactoring** — `MainActivity` insets null emniyeti, `ProfileSelectionPage` `OnAppearing` try-catch koruması, `UserFeedbackService` hata dayanıklılığı, `MainApplication` global hata yakalayıcılar, CoinFlow -> Mizan refactoring |
| **v1.18.3** | **Hatırlatıcı Cevapları & Bakiye Zaman Uyumu** — Bakiye gözlemi ile bildirim ödeme cevaplarının zaman uyumlandırılması, erteleme/ödedim/geri al adımlarında yaşam gideri hesabı tutarlılığı, 584/584 test yeşil |

Grafik çalışması (Faz 1–4, v1.1.0–v1.2.0) v1.3.0'da geri alındı; ayrıntı
aşağıdaki sürüm bölümlerinde. Anılan `DEVIR-FAZ3.md` repoda yok.

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
Ölçüldü: `MizanService.SaveSettingsAsync` tutar değiştiğinde çapayı zaten
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
- **`ProfileScopedMizanStore`** uygulamanın gördüğü tek `IMizanStore`;
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

### v1.15.0 ne getirdi (plan türleri ve doğrudan giriş)

Kullanıcı: *"Simülatördeki plan türü çok şişti, listeden bir şey bulmak
zorlaşıyor"* ve *"bazı plan türleri finansal yapı tarafında yok; bir harcama
girmek için simülatörde simülasyon yapıp planı uygula demek gerekiyor."*

Kararlar (üçü de önerilen seçenek): **grup çipleri + açıklamalı kartlar** ·
nakit satın alma ile tek seferlik ödeme **birleşir, planlı büyük gidere**
yazılır · **ortak form**: Finansal Yapı simülatörün formunu ve uygulama yolunu
kullanır.

**Teşhis.**
- Simülatörde tek `Picker`'da 13 tür vardı; açıklama ancak seçimden sonra
  görünüyordu. İki çift aynı hesabı yapıyordu (`AddCardPurchase`: tek çekim ile
  taksit arasındaki tek fark sayı; `AddLoanPrepayment`: kapama ile ara ödeme
  arasındaki tek fark mod). "Nakit satın alma" `PlannedLargeExpense`'e, "Tek
  seferlik ödeme" ise `OtherScheduled` ödeme planına yazılıyordu. İkincisi
  uygulanınca Finansal Yapı'da "Düzenli Ödemeler" altında görünüyordu.
- `89d7f90`'da (27.08) Finansal Yapı'nın kayıt türü `Picker`'ı 5 seçenekli bir
  menüyle değiştirilmişti. `income`, `installment` ve `temporary` formları view
  model'de kaldı ama **hiçbir yerden açılmıyordu**. Kartta taksitli harcama,
  taksitli nakit borç, finansman ve krediye erken ödeme yalnız Simülatör →
  Planı Uygula yoluyla girilebiliyordu.

**Yapılan.**
- **`SimulationScenarioCatalog`** (Application) grupları tanımlar: Harcama
  (Nakit ödeme · Kartla harcama · Düzenli ödeme), Borç / Kredi (Kredi /
  finansman çek · Taksitli nakit borç · Krediye erken ödeme), Gelir (Tek
  seferlik gelir · Gelir değişikliği), Ayar (Kart ödeme şekli · Gelir kullanım
  düzeni). Her grupta en fazla üç tür var. `Resolve` motor türünü taksit
  sayısından ve moddan çözer.
- **Enum ve şema değişmedi.** Eski "Tek seferlik ödeme" koşulları yüklenir;
  düzenlenince türünü korur. Uygulanmış bir koşulun kimliği başka türe geçseydi
  uygulama onu tanımaz, ikinci kez kaydederdi.
- **`ScenarioConditionForm` + `ScenarioConditionFormView`**: alanlar, tür
  seçimi ve `SimulationRequest` üretimi `SimulationViewModel`'den buraya
  taşındı. Simülatör ve Finansal Yapı aynı formu kullanıyor.
- **Finansal Yapı "+ Ekle":** Harcama · Borç / Kredi · Tek Seferlik Gelir
  ortak formu açar. Maaş / Gelir Değişikliği · Bankadaki Kredimi Ekle · Kredi
  Kartı · Tarihleri Farklı Ödeme Planı kendi formlarında kalır; sonuncusu gizli
  kalmış `temporary` formudur. Ulaşılamayan ekleme dalları silindi.
- **`MizanService.AddRecordFromScenarioAsync`** `ApplySimulationAsync` ile
  aynı gövdeden geçer (doğrulama, idempotency, çakışma, plan revizyonu;
  tetikleyici "Finansal Yapı'dan eklendi"). Kendi ekranı olan türleri (gelir
  değişikliği, kart ödeme şekli, düzen değişikliği) reddeder. Form açılınca
  üretilen kimlik sayesinde çift dokunuş ikinci kayıt oluşturmaz.

**Testler 424 → 454.**
- `SimulationScenarioCatalogTests` (12): her enum değeri tam bir seçenekte,
  grup başına en fazla 3 tür, çözüm kuralları, eski türün korunması.
- `ScenarioDirectEntryTests` (15). Asıl test **parite**: her doğrudan giriş
  türünde simülasyonun 12 dönemi (dönem sonu ve zorunlu ödeme), aynı koşul
  doğrudan girildikten sonraki projeksiyonla birebir aynı. Servis bilerek
  bozulunca (tutar +1.000) 9 durumun 8'i düştü; erken kapamada tutar
  kullanılmadığı için dokuzuncunun geçmesi bekleniyordu.
- `ScenarioEntrySourceTests` (3): "+ Ekle" menüsü her kayıt formuna ulaşıyor.
  Bu, `89d7f90`'daki "kodda var, ekranda yok" durumunun korkuluğu.

**Emülatörde doğrulandı.**
- Çipler ve kartlar görünüyor. Grup değişiyor. Krediye erken ödeme seçilince
  tarih bir sonraki taksit gününe (07.10.2026) kayıyor ve ad krediden
  türüyor. Düzenle, koşulu grubu, türü ve moduyla geri yüklüyor.
- Kartla harcama 30.000 · 3 taksit simülasyonda **12 ay sonu 675.437,55**. Aynı
  koşul Finansal Yapı'dan girildi (çift dokunuşla); kart borcu
  128.071 → 158.071 (tek kayıt). Simülatörde koşul kapatılınca baz çizgi de
  **675.437,55**.
- Tek seferlik gelir Gelirler'de görünüyor. Tarihleri Farklı Ödeme Planı eski
  formla açılıyor.

**Emülatörde yakalanan iki kusur.**
1. Dört çip iki satıra taşıyordu; yatay iç boşluk azaltıldı.
2. Finansal Yapı'nın mavi form zemininde seçili olmayan çipler ve seçili tür
   kartı zeminle aynı renkteydi. Çipler açık lavanta, seçili kart lavanta
   oldu.

Bu projede sekizinci kez yalnız ekranda görülen kusur.

**Bilinen.**
- Doğrudan girilen kart harcamasının açıklaması plan adıdır; eski formdaki
  "Not" alanı ortak formda yok.
- Finansal Yapı formundaki "Vazgeç" butonu mavi zeminde zayıf görünüyor. Bu
  sürümden önce de böyleydi, dokunulmadı.

### v1.16.0 ne getirdi (hatırlatıcı, ekleme alanı, placeholder, temizlik)

Kullanıcı beş istek verdi ve **soru sorulmamasını, önerilen seçeneklerle
ilerlenmesini, işin riske göre fazlara bölünmesini** istedi:

1. Simülatördeki yeni ekleme tasarımı Finansal Yapı'ya taşınsın.
2. Ödeme günü geldiğinde hatırlatıcı bildirim; Ana Sayfa'dan ve/veya "Bu dönem
   nasıl oluşuyor" ekranından kurulsun; **agresif** ve **rahat** iki davranış;
   UI/UX tasarımı geliştiriciye bırakıldı.
3. Atıl / kullanılmayan kod kaldırılsın.
4. Metin kutuları otomatik dolmasın; dolanlar genel kullanıcıya yazılmış
   placeholder'a dönsün.
5. Geçmiş dönemlerin tutulduğu business için tarih ve tutar ağırlıklı ayrıntılı
   testler.

Fazlar düşük riskten yükseğe sıralandı; her faz ayrı commit, testler yeşil:
**1** testler → **2** atıl kod → **3** placeholder → **4** ekleme alanı →
**5** hatırlatıcı (yeni izin, alarm, şema).

**Kararlar (önerilen seçenekle alındı; kullanıcı değiştirebilir).**
- Hatırlatıcı profil başına tek ayar: Kapalı · Rahat · Agresif. Ödeme başına
  aç/kapa yok.
- Rahat: ödeme günü 09:00, tek bildirim. Agresif: 3 gün önce 10:00, bir gün
  önce 20:00, ödeme günü 09:00 ve 18:00. Aynı güne düşen ödemeler tek
  bildirimde toplanır (ilk üç ad + "ve N ödeme daha").
- Kaynak: açık dönemde donmuş planın satırları (Ana Sayfa ile aynı; ödendi
  işaretlenen hatırlatılmaz, **bugün vadesi gelen hatırlatılır** — Ana Sayfa
  onu ödenmiş sayar), dönem sonrası projeksiyon. Ufuk 35 gün.
- Kart Ana Sayfa'da KALAN'ın altında; "Bu dönem nasıl oluşuyor" ekranında
  yalnız Ana Sayfa'dan açılınca (12 Dönem ve Simülatör'den açılınca yok).
- Finansal Yapı'da dört grup: Harcama · Borç / Kredi · Gelir · **Hesap**
  (kredi kartı, bankadaki kredi, değişken ödeme planı). Tür değiştirmek
  yazılanı silmez.
- Dönem sihirbazında boş alan planlananı kabul eder (placeholder "Boş
  bırakırsan planlanan: …"); "Her şey planlandığı gibi" kısayolu aynen çalışır.
- Düzenleme ekranları (kart, kredi, Ayarlar) kaydın kendi değerlerini
  göstermeye devam eder; bu otomatik doldurma sayılmadı.

**Faz 1 — geçmiş dönem testleri ve bulunan hata.** `PeriodHistoryTests` (48):
donmuş planın penceresi (maaş günü 31, Şubat, artık yıl, yıl dönümü), kısmi
dönem yaşam gideri ve yarım kuruş (`1.000,01 × 15/30 = 500,005 → 500,01`),
açık faizi `AwayFromZero` (`617,285 → 617,29`), checkpoint maaşı,
plan/gerçek karşılaştırmasının her satırı ve özet cümlesi, elle hesaplanan
gerçekleşen (`110.150`), tarih doğrulamasının dört sınır günü, üç ardışık
dönem zinciri, maaş günü 31 zinciri, geç kapanış ve geçmiş sorgusunda
revizyon seçimi (UTC tarih sınırı, eşit zamanda revizyon numarası).

Testler bir **hata** buldu: ödenmeyen kredi taksiti, geçici ödeme ve büyük
gider dönem kapanışında **checkpoint gününe** taşınıyordu. Yeni dönemin donmuş
planı `(checkpoint, sonraki checkpoint]` penceresini okuduğu için bu borç Ana
Sayfa'da hiç görünmüyordu (kanonik veride 14.501,23 TL'lik taksit ölçüldü);
geçici ödeme ve büyük gider hiçbir review'da kapatılamıyor, her checkpoint'te
yeniden taşınıyordu. Projeksiyon `>= çapa` okuduğu için 12 Dönem onu sayıyordu —
iki ekran farklı söylüyordu. Düzeltme: ödenmeyen yükümlülük **yeni dönemin ilk
gününe** devreder; checkpoint gününde vadesi olup ödenmeyen de. Snapshot günü
(çapa) tarihli, hiçbir plana girmemiş kalem de ilk kapanışta devreder. Açık
iş 5'in (gecikmenin temsili) ürün kararı hâlâ açık; bu düzeltme yalnız borcun
görünmesini sağlar. Düzeltme bilerek geri alınınca 4 test düştü.

**Faz 2 — atıl kod.** Analizör (IDE0051/0052/0060/0005) ve tanım–referans
taraması. Kaldırılanlar: Finansal Yapı'daki eski kart ödeme planı dalı
(komut, koleksiyon, alanlar, işleyici — kart düzenlenirken kartın ödeme
kararları artık ayrı alanda korunup geri yazılıyor), bölüm listesi, v1.3.0'da
ekrandan kalkan Simülatör özet alanları, bağlanmayan Ana Sayfa/Geçmiş/12 Dönem
özellikleri, çağrılmayan domain özellikleri, `CalendarRules.MonthlyDates`,
ekstre içe aktarmada hiç doldurulmayan gelecek taksit ve faiz alanları,
`ToStatement`, gereksiz `using`'ler. ~450 satır. Servis cephesindeki yalnız
testlerin kullandığı metotlar (`SaveOtherIncomeAsync`, `ObservePaymentAsync`
…) bilinçli bırakıldı: test kurulumu onları kullanıyor.

**Faz 3 — placeholder.** Ortak formdaki "Beyaz eşya · 120000 · 9 · 145000" ve
"Yeni koşul", Finansal Yapı ve ilk kurulumdaki günler (10/12/25/5/40), "0"
tutarlar, kart adı tahmini ("Axess"/"Bonus"), Ana Sayfa'da son gözlemle dolan
mevcut tutar, profil adı penceresinin başlangıç değeri kalktı. Krediye erken
ödemede ad alana yazılmıyor; krediden türeyen ad placeholder'da, boş bırakılırsa
o ad kullanılıyor. Yeni form önceki kaydın değerlerini taşımıyor. Ana Sayfa'da
son gözlemin tutarı artık "Son gözlem" satırında. `PlaceholderSourceTests` (9)
her `Entry`'nin placeholder'ı olduğunu ve Entry'ye bağlı alanların boş
başladığını sabitler.

**Faz 4 — Finansal Yapı ekleme alanı.** "+ Ekle" menü yerine formun üstünde
`EntryTypePickerView` (Simülatör'ün koşul formu da aynı kontrolü kullanıyor).
`FinancialRecordEntryCatalog` ortak formdan girilen türleri simülatör
seçeneğinin kendisiyle taşır. `RecordTypes` / `IsIncomeSection` kalktı;
yönlendirmeyle gelinince açık form kapanır. v1.15.0'dan kalan "Vazgeç" ve kart
formundaki "Hayır" butonları mavi zeminde görünür oldu.

**Faz 5 — ödeme günü hatırlatıcısı.**
- **Şema v16:** `settings.PaymentReminderMode` (0 kapalı). Sütun yalnız
  kendi metoduyla yazılır; finans ayarlarını kaydetmek onu sıfırlamaz,
  "Verileri Sil" kapatır. Eski veritabanı açılınca sütun eklenir (test sütunu
  silip v15'e indirerek doğruluyor). Yedeğe girer.
- `PaymentReminderPlanner` (Application, saf) takvimi üretir;
  `MizanService.GetPaymentRemindersAsync(now)` ödemeleri toplar.
- Android (`Platforms/Android/PaymentReminders.cs`): `AlarmManager` +
  `BroadcastReceiver` + bildirim kanalı "Ödeme hatırlatıcısı".
  `POST_NOTIFICATIONS` Android 13+'ta kart ilk açılınca sorulur; izin yoksa
  kartta uyarı + "Bildirimleri Aç". Kurulan bildirimler
  `files/payment-reminders.txt`'de de tutulur; `BOOT_COMPLETED` ve
  `MY_PACKAGE_REPLACED` alıcısı veritabanını açmadan oradan geri kurar. İstek
  kodu profil + anahtardan FNV-1a (süreçten bağımsız). Profil silinince
  bildirimleri de silinir.
- Eşitleme: Ana Sayfa her yüklenişte ve davranış değişince açık profilin
  bildirimleri güncel planla değiştirilir (diğer profillere dokunulmaz).
- Testler: `PaymentReminderTests` (23), `PaymentReminderAppSourceTests` (5).

**Emülatörde (Android 14) doğrulandı.** Ana Sayfa'da kart; Agresif → izin
penceresi → İzin ver → "Önümüzdeki 35 gün için 24 bildirim kuruldu"; dosyada
24 satır, `dumpsys alarm`'da kayıtlar. Saat alarmın öncesine alınınca bildirim
10 dakikalık pencerede düştü ("3 gün sonra ödeme var · Eminevim · 28.167,40
TL · 20 Eylül Pazar"). Uygulama güncellenince (`MY_PACKAGE_REPLACED`) alarmlar
dosyadan yeni pencereyle geri kuruldu. Yeniden açılışta ayar kalıcı; "Bu dönem
nasıl oluşuyor" ekranında aynı kart. Finansal Yapı'da çipler ve kartlar,
Hesap grubu, krediye erken ödemede krediden türeyen ad placeholder'ı;
Simülatör aynı seçiciyle.

**Emülatörde yakalanan üç kusur.**
1. `SetAndAllowWhileIdle` Android 14'te **bir saatlik** pencere aldı (09:00
   bildirimi 10:00'a kayabilirdi). Kesin alarm izni yoksa `SetWindow` ile 10
   dakika; varsa ya da Android 12 öncesinde tam saatinde.
2. Ana Sayfa'daki mevcut tutar placeholder'ı ("Bugün hesabındaki toplam para")
   kutuya sığmıyordu → "Bugünkü tutar".
3. Kart formundaki "Hayır" butonu mavi zeminde görünmüyordu.

Bu projede on birinci kez yalnız ekranda görülen kusur.

**Testler 454 → 543.**

**Sürümden sonra.** Tag'in kararlı derlemesi yeşildi (`Mizan-1.16.0.apk`),
ama aynı commit'in `main` geliştirme derlemesinde ilgisiz ve eski bir test
düştü: `Watchdog_ReturnsTimedOutAndDoesNotRetryHungImporter`'ın 75 ms'lik
bekçisi yavaş CI makinesinde içe aktarıcı çağrılmadan doluyordu. Bekçi 500 ms'ye
çıkarıldı (yalnız test). v1.11.1'deki kararsız testle aynı sınıf: zamanlamaya
bağlı içe aktarma testlerinde süreyi bekleme koşulu yapma.

**Bilinen.**
- Uygulama 35 günden uzun açılmazsa yeni bildirim kurulmaz (açık iş 16).
- Birden fazla profil varsa her profilin bildirimi, o profil son açıldığında
  kurulan takvimle çalar.
- Bildirim küçük simgesi uygulama simgesi; tek renkli ayrı simge yok.

### v1.17.0 ne getirdi (hatırlatıcıya cevap: Ödedim / Ertele)

Kullanıcı v1.16.0'ı gerçek telefonunda açtı ve iki şey söyledi; v1.16.0'daki
gibi **soru sorulmamasını, önerilen seçeneklerle ilerlenmesini, işin riske
göre fazlara bölünmesini** istedi:

1. *"Bugün 15 Eylül ama bugün diyor."* Kartta 18 Eylül satırı "Bugün ödeme
   günü" yazıyordu.
2. *"Bildirimi test edemedim."* Bildirim **Ödedim / Ertele** şeklinde olsun.
   Ödedim → ilgili yerler güncellensin. Ertele → uygulamada hatırlatıcı
   alanında kayıt saydam kırmızı, sağda "Ertelendi"; dokununca "Bu ödeme
   yapıldı mı?", Evet → tebrik mesajı, listeden kalksın, parametreler
   güncellensin (Ana Sayfa ve ayrı sayfadaki durum değişebiliyorsa
   geliştirici ayarlasın). Dönem içinde böyle ödenenler Ana Sayfa'da ve
   mevcut dönemin detayında saydam yeşille gösterilebilir, **ama
   hatırlatıcının listesiyle karışmasın**.

Fazlar düşük riskten yükseğe; her faz ayrı commit, testler yeşil:
**1** kartın metni → **2** hatırlatıcı defteri (şema v17, Ana Sayfa hesabı)
→ **3** uygulama içi ekran → **4** Android bildirim düğmeleri (arka plan
alıcısı, dosya kuyruğu).

**Faz 1 — "bugün" hatası.** Kök neden: kart, kurulan bildirimin *çaldığı
andaki* başlığını ("Bugün ödeme günü") bugünkü önizlemede aynen
gösteriyordu. Bildirim doğruydu, kart yanlış soruyu cevaplıyordu. Kart
artık ödeme günü başına bir satır (`PaymentReminderPlanner.Preview`):
"18 Eylül Cuma · 3 gün sonra", ödeme ve tutar, "Bildirimler: 3 gün önce
10:00 · bir gün önce 20:00 · ödeme günü 09:00 ve 18:00".

**Kararlar (önerilen seçenekle alındı; kullanıcı değiştirebilir).**
- **"Ertele" 3 saat sonra yeniden hatırlatır.** 22:00'ye ya da sonrasına
  düşerse ertesi sabah 09:00, 08:00'den önceye düşerse aynı sabah 09:00.
  Yeniden hatırlatmanın da düğmeleri var; ertelemeye devam edilebilir.
- **Aynı güne düşen ödemeler tek bildirimde kaldı** (v1.16.0 kararı). O
  bildirimde düğme "Hepsini ödedim"; biri ödenmediyse Ertele → kartta tek
  tek çözülür.
- **Ertelenen ödeme ödenmemiş sayılır.** Ana Sayfa normalde vadesi geçen
  satırı ödenmiş sayar; ertelenen satır vadesi geçse de KALAN'da kalır ve
  "Ertelendi" yazar. Kullanıcı "ödemedim" demiş; bakiye o parayı hâlâ
  içeriyor.
- **Ödendi geri alınabilir.** "Ödediklerin" satırına dokununca "Ödendi işareti
  kaldırılsın mı?". Bildirimde yanlış düğmeye basmak finans uygulamasında
  kalıcı olmamalı; deneme bildirimi de bunu gerektiriyor.
- **Geç gelen "Ertele" ödendi kaydını geri almaz** (aynı bildirimin eski
  kopyası).
- **"Ödediklerin" kartın dışında** ayrı blok: Ana Sayfa'da KALAN ile kart
  arasında, Ana Sayfa'dan açılan Dönem Detayı'nda kartın altında.
- **Dönem Detayı'nın rakamları değişmedi.** O ekran projeksiyondur (I1);
  cevaplar yalnız Ana Sayfa'nın donmuş plan hesabına girer. Loan'ın
  `NextPaymentDate`'i ya da kartın ekstresi checkpoint'e kadar değişmez
  (I14).
- **Dönem sihirbazı ertelenen ödemeyi "Ödenmedi" açar** (not: "Hatırlatıcıda
  ertelendi"). Ödenenler zaten "Ödendi" açılıyordu. "Her şey planlandığı
  gibi" kısayolu hepsini ödendi yapar, aynen.
- **"Deneme bildirimi gönder"** sıradaki ödeme gününün bildirimini gerçek
  düğmeleriyle hemen düşürür — kullanıcının "test edemedim" dediği şey.
  "Ödedim" gerçekten işaretler; geri alma buradan.
- Tebrik mesajı beş cümleden rastgele biri ("Ödemeleri yapmak böyledir:
  zamanında, dert etmeden." …).

**Faz 2 — hatırlatıcı defteri.**
- **Şema v17:** `payment_reminder_responses` (yalnız yeni tablo). Satır:
  anahtar, ad, vade, tutar, tür (ertelendi/ödendi), cevap zamanı, erteleme
  saati. "Verileri Sil" temizler, yedeğe girer (parmak izi tabloları genel
  tarıyor).
- **Anahtar kaynak + vade, satır kimliği değil.** Önce cevabı gözlem
  defterine (`ObservePaymentAsync`, satır kimliğiyle) yazmak düşünüldü.
  Ölçüldü: plan revizyonu satırlara yeni kimlik veriyor, dönem sonrasındaki
  ödemelerin henüz satırı yok. Kimlikle bağlanan cevap ilk revizyonda
  kaybolurdu. Test bunu düzen değişikliğiyle revizyon üretip ölçüyor.
  Aynı sebeple gözlem defterinin kendisinde gizli bir eşleşme sorunu var
  (açık iş 18).
- Servis: `GetPaymentReminderBoardAsync` (bildirimler + yeniden hatırlatmalar
  + gün satırları + ertelenenler + ödenenler + deneme), `RecordPaymentReminderAnswerAsync`,
  `UndoPaymentReminderAnswerAsync`. Kapanmış dönemin (vadesi açık dönemin
  başlangıç gününe eşit ya da önce) cevapları kartta gösterilmez.
- `PeriodProgressService.Build` kural sırası: gözlem defteri → hatırlatıcı
  cevabı → vade kuralı.

**Faz 3 — ekran.** Kartta ertelenenler (`SnoozedSurface`, #AARRGGBB saydam
kırmızı; ilk hâli lavanta kartın üstünde mor okunduğu için emülatörde
koyulaştırıldı), "Ödediklerin" (`PaidSurface`, `PaymentReminderPaidView`).
Ana Sayfa kartın `AnswersChanged` olayında yeniden hesaplar.

**Faz 4 — bildirim düğmeleri.**
- `PaymentReminderActionReceiver` **veritabanını açmaz**: başka profil açıkken
  ya da süreç ölüyken de çalışıyor ve `ProfileScopedMizanStore` yalnız açık
  profili tanıyor. Cevabı `files/payment-reminder-answers.txt`'ye ekler,
  "Ödedim"de o ödemelerin kalan alarmlarını iptal eder, "Ertele"de
  `yyyyMMdd-ertele` alarmını kurar, bildirimi kapatır, Toast gösterir
  ("Tebrikler! Ödendi olarak kaydedildi." / "Ertelendi. Yeniden hatırlatma:
  yarın 09:00") ve `WeakReferenceMessenger` ile açık ekranlara haber verir.
- Ana Sayfa kuyruğu **kalan ödemeleri hesaplamadan önce** işler; satırlar
  deftere yazıldıktan sonra silinir. Uygulama açıksa kart mesajla hemen
  yenilenir.
- Alarm dosyasına ödemeler altıncı sütun olarak eklendi; v1.16.0'ın beş
  sütunlu satırları düğmesiz bildirim olarak okunur (güncellemeden sonra
  `MY_PACKAGE_REPLACED` onları geri kurar, ilk Ana Sayfa açılışı düğmeli
  hâlleriyle değiştirir).

**Testler 543 → 576.** `PaymentReminderAnswerTests` (yeni): erteleme saati
(gece, ay/yıl sonu), yeniden hatırlatma, deneme bildirimi, anahtar, bildirim
verisi ve cevap satırının gidiş-dönüşü (Türkçe ad, sekme, satır sonu, bozuk
veri), ödendi → KALAN'dan düşer ve hatırlatılmaz, ertelendi → vadesi geçse de
KALAN'da + yeniden hatırlatma, ertelendi → ödendi → geç ertele → geri al,
**revizyondan sonra ödendi hâlâ eşleşiyor**, kapanmış dönemin cevabı
gizli / erken ödenen gelecek ödeme görünür / kapalı modda da liste, kalıcılık
+ "Verileri Sil" + v16'dan yükseltme. `PaymentReminderTests`'e kartın gün
satırları (kullanıcının ekranındaki rakamlarla), `PaymentReminderAppSourceTests`'e
kırmızı/yeşil, ayrı liste, sihirbaz, bildirim düğmeleri, kuyruğun sırası.
Ana Sayfa'daki cevap eşlemesi bilerek kapatılınca 3 test düştü.

**Emülatörde (Android 14) doğrulandı.** Kart "20 Eylül Pazar · 5 gün sonra".
Deneme bildirimi → Ödedim / Ertele düğmeli bildirim → Ertele (uygulama arka
planda) → bildirim kapandı, kuyruk anında işlendi, kartta kırmızı "Ertelendi"
satırı, "Yeniden hatırlatma: yarın 09:00" (19:37 + 3 saat gece), alarm
dosyasında `20260918-ertele` → dokun → "Bu ödeme yapıldı mı?" → Evet → tebrik
→ "Ödediklerin"de yeşil, bildirim sayısı 24 → 20 → dokun → geri al.
**Süreç öldürülmüşken** (`am kill`) bildirimde Ödedim → kuyruk dosyasına
satır yazıldı, o günün üç alarmı dosyadan ve AlarmManager'dan kalktı →
soğuk açılışta kuyruk boşaldı, "Ödediklerin"de satır. Dönem Detayı'nda da
"Ödediklerin" kartın altında.

**Emülatörde yakalanan iki kusur.**
1. Saydam kırmızı lavanta kartın üstünde mor okunuyordu; alfa ve ton
   artırıldı.
2. "Deneme bildirimi gönderildi" bilgisi sonraki işlemlerden sonra da
   duruyordu; kart yenilenince siliniyor.

Bu projede on üçüncü kez yalnız ekranda görülen kusur.

**Bilinen.**
- Dönem Detayı'nın "Ödeme ayrıntıları" ödenen satırı işaretsiz gösteriyor
  (açık iş 19).
- Aynı güne düşen iki ödemeden yalnız biri ödendiyse bildirimden ayrı
  cevaplanamaz; Ertele → kartta tek tek.
- Uygulama hiç açılmazsa kuyruk büyür ama kaybolmaz; profil silinince
  kuyruğu da silinir.
- Kapanmış dönemlerin cevapları tabloda kalır (küçük; temizlik yok).

### v1.18.0 ne getirdi

- **Clean Architecture & Modern MVVM Dönüşümü.** Proje vibecoding kalıntılarından
  arındırıldı, mimari kurallar `ARCHITECTURE_RULES.md` ile kilitlendi.
- **ViewModel UI İzolasyonu.** ViewModels katmanındaki `Shell.Current`, `IServiceProvider`
  ve `Page` bağımlılıkları tamamen sıfırlandı. Navigasyon `INavigationService`
  ve rota sabitleri `NavigationRoutes` üzerinden yönetiliyor; diyalog ve toast
  bildirimleri `UserFeedbackService` ile UI thread garantisine alındı.
- **Segregated Repositories (ISP).** 40+ metotlu monolitik `IMizanStore` arayüzü
  10 adet odaklı repository arayüzüne (`ISalaryRepository`, `ILoanRepository`,
  `ICreditCardRepository` vb.) ayrıştırıldı.
- **Tek Sorumluluk Prensibi (SRP).** 2.113 satırlık monolitik `MizanService`,
  4 odaklı use-case servisine (`FinancialPlanQueryService`, `SimulationWorkflowService`,
  `PeriodWorkflowService`, `ObligationManagementService`) bölündü ve ince bir Facade
  ile geriye dönük tam uyumlu bırakıldı.
- **350 Satır Sınırı (%100 Uyum).** Proje genelindeki tüm büyük ViewModel, Servis,
  Hesaplayıcı ve Store sınıfları mantıksal partial modüllere bölünerek `src/` altındaki
  204 `.cs` dosyasının tamamı $\le 350$ satır sınırına getirildi. 350 satırı aşan dosya sayısı: 0.
- **Testler 576/576 yeşil.** Tüm hesaplama motoru, finansal formüller, projeksiyon
  mekanikleri ve veri tabanı entegrasyon testleri eksiksiz geçiyor.

### v1.18.1 ne getirdi

- **Soğuk Açılış ANR Kilitlenmesi Giderildi.** v1.18.0 mimari refactor'ünden sonra uygulamanın ilk açılışında Android'in "Bu uygulama yanıt vermiyor" (ANR) uyarısı vermesi ve kapanması sorunu kökten çözüldü.
- **`UserFeedbackService` UI Dispatcher İyileştirmesi.** UI thread'i (`MainThread.IsMainThread`) üzerindeyken `DisplayAlert`, `DisplayPromptAsync` ve `DisplayActionSheet` çağrılarının gereksiz yere `MainThread.InvokeOnMainThreadAsync` kuyruğuna sokulması kaldırıldı; doğrudan çağrılması sağlandı. `CurrentPage()` içine `Application.Current.MainPage` geri dönüşü eklenerek `Shell.Current`'ın henüz oluşmadığı profil seçim ekranında güvenli sayfa referansı garantiye alındı.
- **`MauiNavigationService` Asenkron Modal Bekleyişi Düzeltildi.** `OpenOnboardingModalAsync` ve `OpenInitialStrategyModalAsync` metotlarında modalın kapanmasını bekleyen `await page.Completion` ifadesi `InvokeOnMainThreadAsync` kapsamı dışına taşındı; böylece UI mesaj pompalamasının kullanıcı etkileşimi bitene kadar tıkanması engellendi. Tüm navigasyon çağrılarına ana iş parçacığı kontrolü eklendi.
- **`ProfileSelectionPage` Açılış Koruma Kalkanı.** `WhenLoadedAsync` beklemesine 500 ms emniyet zaman aşımı (`Task.WhenAny`) eklendi.
- **Testler 579/579 yeşil.** Açılış ve navigasyon emniyetleri için eklenen xUnit regresyon testleri dahil tüm testler eksiksiz geçiyor; Release APK derlemesi 0 uyarı ve 0 hata ile doğrulandı.

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
   (Anılan `PLAN-IZOLASYON.md` repoda yok; bağlam v1.6.0 bölümünde.)

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
   v1.16.0: kayma hedefi checkpoint günü değil **yeni dönemin ilk günü** (I19);
   önceden borç yeni dönemin planında hiç görünmüyordu. Gecikmenin temsili hâlâ açık.
   Düzeltmek için `Loan`, `TemporaryPaymentInstallment`, `PlannedLargeExpense`
   kayıtlarına gecikme alanı + yeni bir şema sürümü + UI rozeti gerekiyor.
   (Eski notta "şema v12" yazıyordu; v12 geçici planlara gitti, v13 izolasyona
   ayrıldı — bu iş sıradaki boş sürümü alır.)
   İlgili: `src/Mizan.Application/Services/FinancialInstrumentReconciliationService.cs`
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
   v1.17.0: hatırlatıcıdaki "Ödedim" taksiti Ana Sayfa'da ödenmiş sayar ve
   hatırlatmayı durdurur, ama krediyi checkpoint'e kadar değiştirmez (I14);
   12 Dönem ve Dönem Detayı taksiti hâlâ sayar.

9. **Simülatörde ilk maaş öncesi tarihli gider sessizce hesaba girmiyor.**
   v1.0.5'te *gelir* için düzeltilen boşluğun *gider* karşılığı. `CashPurchase`
   varsayılan tarihle (bugün) eklenince `PreFirstSalaryObligations`'a düşüyor ve
   dönem matematiğine hiç girmiyor. Gelirden farkı: giderler en azından Ana
   Sayfa'da listeleniyor, tamamen kaybolmuyor. Simülatörde uyarı yok.
   Faz 3 doğrulamasında canlı görüldü: 07.09.2026 tarihli 120.000 TL
   `CashPurchase` (ilk maaş 10.09) faiz tablosunun her satırını "Değişmiyor"
   bırakıyor ve 12 ay sonu bazla birebir aynı çıkıyor — koşul eklenmiş gibi
   görünüyor ama hiçbir şey değişmiyor.

10. ~~**Doküman kayması.**~~ ✅ 14.09.2026: README ve ARCHITECTURE şema
   sürümünü v15 olarak yazıyor, README'deki bağlantılar `docs/` altına göre
   düzeltildi. README'de test sayısı artık geçmiyor. Kalan bilinen sapma: sürüm
   bölümlerinde anılan yerel planlama belgeleri repoda yok (Devir → Tuzaklar).

11. **Faiz oranıyla oynayamıyorsun.** Kart ve KMH için tek bir varsayılan `%5`
   var (Ayarlar'dan ikisi ayrı ayrı girilebiliyor ama kart başına değil).
   Gerçek oranlar farklı — Garanti kart %3,25, KMH %4,25 — ve model bu
   karşılaştırmayı gösteremediği için "kartı KMH'dan kapatmak ucuz mu"
   sorusu yanıtlanamıyor. (Boşluğu tarif eden `BULGU-KART-FAIZI.md` repoda yok;
   bağlam v1.2.1 bölümünde.)

12. **Gerçek veritabanında kredi anaparaları bozuk olabilir (🔴 kullanıcı eylemi).**
   v1.9.0 öncesi her kapatılan checkpoint anaparadan taksitin tamamını düştü.
   Kurtarılamaz; kredi satırında *"anaparası güncel görünmüyor"* uyarısı
   çıkarsa Finansal Yapı → Düzenle → **bankanın bugünkü erken kapama tutarı**.
   Kullanıcı Burgan için bunu 13.09.2026'da yaptı; Garanti için henüz bilinmiyor.

13. **Artık dallar.** Worktree'ler kalktı (14.09.2026'da `git worktree list`
   yalnız `main`). Yerel dallar `claude/kind-tharp-5d47f1`,
   `claude/nervous-elbakyan-48fcd9` ve `feat/card-payment-preference-history`
   (uzakta da var) duruyor; üçünün de `main`'de olmayan commit'i yok.
   Kullanıcı onayıyla silinebilir.

14. **Öneri motorunun bilinen sadeleştirmeleri.** Ufuk sonrası KMH faizi
   sayılmaz; pozitif bakiyeye mevduat getirisi verilmez (kapatmak mı, mevduatta
   tutmak mı sorusu cevaplanmaz); değişken faizli kredinin gelecek faizi
   bugünkü sabit varsayılır; ara ödemede ödenen tutar plandakinden farklı
   girilse de olay plandaki tutarla işlenir.

15. **v1.15.0'dan kalan küçükler.**
   - Ortak formdan girilen kayıtta eski formdaki "Not" alanı yok; açıklama plan
     adı.
   - ~~Finansal Yapı formunda "Vazgeç" `SoftSky` zeminde zayıf görünüyor.~~ ✅
     v1.16.0.
   - Eski "Tek seferlik ödeme" (`FutureOneTimePayment`) ile uygulanmış
     kayıtlar Finansal Yapı'da "Düzenli Ödemeler" altında "Planlı ödeme"
     rozetiyle duruyor. Yeni kayıt bu türde oluşmuyor; eskileri taşınmadı.

16. **Hatırlatıcı yalnız uygulama açılınca yenilenir.** Takvim 35 gün ileriyi
   kurar ve Ana Sayfa her yüklenişte eşitlenir. Uygulama 35 günden uzun
   açılmazsa yeni bildirim kurulmaz. Çözüm adayı: gece yedek görevi
   (`NightlyBackupJob`) her profil için takvimi de yenilesin — profil
   veritabanını arka planda açmak gerekiyor, bu yüzden bu tura alınmadı.

17. **Dönem sihirbazı gözlem defterini okumuyor (🟠 koddan, ekranda
   ölçülmedi).** `MizanService.GetObservedReviewDraftAsync` var ve testli,
   ama App tarafında hiçbir yerden çağrılmıyor: `PeriodReviewWizardViewModel`
   taslağı yalnız donmuş plandan kuruyor. I15'in "gözlem checkpoint'te
   review'ı doldurur" cümlesi servis düzeyinde doğru, ekranda değil; Ana
   Sayfa'da işaretlenen ödeme sihirbazda yeniden işaretlenmek zorunda. Karar
   kullanıcının: sihirbaz gözlemle açılsın mı (önerilen). v1.17.0: sihirbaz
   hatırlatıcı defterini okuyor (ertelenen "Ödenmedi" açılır); gözlem
   defteri hâlâ okunmuyor.

18. **Gözlem defteri plan revizyonunda eşleşmesini kaybeder (🟠 koddan,
   ekranda ölçülmedi).** `ObservePaymentAsync` ödemeyi orijinal planın satır
   kimliğine yazar; `PeriodProgressService` ve `GetUpcomingPaymentDuesAsync`
   son revizyonun satırlarıyla eşler. Revizyon satırlara yeni kimlik
   verdiği için revizyondan sonra gözlem işareti hiçbir satıra oturmaz.
   Uygulama bugün gözlem ödemesi yazmadığı için (yalnız testler) kullanıcıya
   görünmüyor; biri o yolu ekrana bağlarsa görünür. Çözüm adayı: hatırlatıcı
   defteri gibi kaynak + vadeyle eşlemek.

19. **Dönem Detayı ödenen satırı işaretsiz gösteriyor.** Hatırlatıcıdan ödendi
   denen ödeme "Ödeme ayrıntıları"nda aynen duruyor (rakamı projeksiyondan).
   "Ödediklerin" hemen üstünde, bilgi kaybolmuyor. Aday: satıra küçük
   "Ödendi" rozeti (rakam değişmeden).

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
| I19 | Ödenmeyen yükümlülük dönem kapanışında yeni dönemin **ilk gününe** devreder; donmuş plan checkpoint gününü dışarıda bıraktığı için checkpoint gününe taşınamaz |
| I20 | Hatırlatıcı cevabı ("Ödedim" / "Ertele") ödemenin kaynağı + vadesiyle tutulur, plan satırı kimliğiyle değil; snapshot zincirine ve projeksiyona girmez, yalnız Ana Sayfa'nın açık dönem hesabına ve review'ın başlangıç değerine girer |

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

### v1.18.2 ne getirdi

- **Soğuk Açılışta Anında Kapanma (Crash on Launch) Giderildi:**
  - `MainActivity`: Bazı Android sürümlerinde pencere kontrolcüsü (`GetInsetsController`) null dönebiliyordu. `controller is not null` kontrolü ve pencere stillerini saran koruyucu try-catch eklendi.
  - `ProfileSelectionPage`: `OnAppearing` yaşam döngüsü (`async void`) koruyucu try-catch ile sarıldı. İzin veya yükleme akışındaki hiçbir hatanın uygulamanın açılışını düşürmemesi garanti edildi.
  - `UserFeedbackService`: `ConfirmAsync`, `PromptAsync`, `ChooseAsync` ve `ShowAlertAsync` metotlarına koruyucu hata yakalama eklendi; pencere hazır değilken veya diyalog gösterilemezken sessizce güvenli fallback dönmesi sağlandı.
  - `MainApplication`: `AppDomain.CurrentDomain.UnhandledException`, `AndroidEnvironment.UnhandledExceptionRaiser` ve `TaskScheduler.UnobservedTaskException` dinleyicileri eklenerek fatal çökmeler loglandı ve engellendi.
  - `MauiProgram`: `MizanService` DI kaydı açık delegeyle doğrudan modern 6 parametreli yapıcıya bağlandı.
- **CoinFlow -> Mizan İsimlendirme Refactorü:**
  - Tüm projeler, isim alanları, çözümler ve mimari dokümantasyon Mizan olarak güncellendi.
- **Doğrulama:** 583/583 test yeşil; Release APK derlemesi 0 hata, 0 uyarı ile tamamlandı.

### v1.18.3 ne getirdi

- **Hatırlatıcı Cevapları ve Gözlem Bakiye Zaman Uyumu (Yaşam Gideri Hesaplama Düzeltmesi):**
  - **Sorun:** Ana sayfada bakiye gözlemi ("Gözlemi Kaydet") yapıldıktan sonra ertelenen bir ödeme bildirimi ödedim veya geri al olarak işaretlendiğinde, `PeriodProgressService` geçmişte kalan ama henüz işaretlenmemiş son vadeli borcu ödenmiş sayarak hesaptan düşüyordu. Bu durum gözlenen bakiyeye dayalı yaşam gideri hesabını yanlış düşürüyordu (örneğin 15.000 TL yerine 5.000 TL).
  - **Çözüm:** `PeriodProgressService.cs` içerisinde bakiye gözlemi anında düşülecek ödenmiş hatırlatıcı cevapları için `response.AnsweredAt <= observation.UpdatedAtUtc` koşulu eklendi. Gözlem tarihinden *sonra* verilen ödeme kararları, gözlem anındaki banka bakiyesinde para henüz mevcut olduğundan o geçmiş gözlemin `settledTotal` hesabına retroaktif düşülmez.
  - **Workflow Servis Güncellemeleri:** `PeriodWorkflowService` üzerindeki `RecordPaymentReminderAnswerAsync` ve `UndoPaymentReminderAnswerAsync` servis çağrıları zaman damgası ve bakiye tutarlılığı açısından pekiştirildi.
  - **Regresyon Testi:** `PaymentReminderAnswerTests.cs` içerisine `UserBugReproduction_ObserveBalance_Snooze_Paid_Undo_PreservesLivingExpenseCalculation` testi eklenerek Bakiye Kaydı -> Ertele -> Ödedim -> Geri Al döngüsü ve yaşam gideri tutarlılığı doğrulandı.
- **Doğrulama:** 584/584 unit test yeşil (`dotnet test`); Release notları `.github/workflows/release.yml` dosyasına eklendi.

## Rol promptları

Eski oturumlarda `agents/MENTOR.md`, `agents/DEV.md`, `agents/BA.md` rol promptları
kullanıldı; bu dosyalar repoda yok. Onlardan korunmaya değer tek kural: kod ile
doküman çelişince "kod esastır" deme, dört yollu triyaj yap — kod bug'ı /
doküman eski / kural değişti / karar eksik. Güncel çalışma biçimi Devir
bölümünde.
