# Mizan: Ürün Manifestosu, Vizyon, Misyon ve Değer Önerisi Belgesi

---

## 1. Giriş ve Manifestonun Amacı

**Mizan**, bireylerin ve düzensiz nakit akışına sahip işletme/ticaret sahiplerinin finansal dengesini sağlamak, belirsizliği ortadan kaldırmak ve ileriye dönük finansal kararları simüle edilebilir hale getirmek amacıyla tasarlanmış modern bir nakit akışı ve finansal projeksiyon platformudur.

Geliştirme sürecinin başında maaşlı çalışanların bir maaş gününden diğerine uzanan taahhütlerini yönetmek üzere kurgulanan sistem; zamanla düzensiz gelirleri, ticari nakit döngülerini, erken borç kapama optimizasyonlarını, çoklu senaryo simülatörünü ve dönem içi akıllı hatırlatıcıları kapsayan kapsamlı bir nakit orkestrasyonuna evrilmiştir.

Bu belge; UI/UX, Google Flow ekran tasarımları, kod tabanı terminolojisi ve pazarlama diline zemin oluşturacak **resmi ürün manifestosudur**.

---

## 2. Vizyon (Nereye Ulaşmak İstiyoruz?)

> **"Kişisel ve ticari finans yönetiminde; geriye dönük muhasebe tutma yorgunluğunu ortadan kaldırarak, herkesin gelecekteki nakit akışını ve finansal kaderini bugünden net biçimde görebildiği, kararlarını güvenle simüle edebildiği standart nakit zekası platformu olmak."**

Mizan, kullanıcılarına *"Geçen ay nereye harcadım?"* sorusunun pişmanlığını değil; *"Önümüzdeki 12 ay boyunca hangi kararı alırsam nakit dengem nasıl etkilenir?"* sorusunun berraklığını ve kontrolünü sunar.

---

## 3. Misyon (Neyi, Nasıl ve Kimin İçin Yapıyoruz?)

> **"Kullanıcıları tek tek fiş ve mikro harcama girme yükünden kurtararak; deterministik matematiksel projeksiyonlar, dönemsel mutabakat noktaları (checkpoints) ve senaryo simülatörleri aracılığıyla, gelir yapısı ne kadar karmaşık olursa olsun herkesin finansal dengesini (mizanını) korumasını sağlamak."**

Mizan bu misyonu şu temel ilkelerle hayata geçirir:

1. **Mikro Harcama Takibini Reddetmek:** Her kahveyi, market fişini tek tek kaydettirmez. Serbest yaşam bütçesini tek bir havuz olarak ele alır.


2. **Deterministik ve Şeffaf Hesaplama:** Banka algoritması karmaşıklığında ancak kullanıcı dostu netlikte; kredi kartı carry faizini, finansman açığı maliyetini ve anapara amortismanlarını kuruşu kuruşuna gösterir.


3. **Senaryo Odaklı Karar Desteği:** Kullanıcı borçlanmadan, yeni bir harcama yapmadan veya bir krediyi erken kapatmadan önce bunun 12 aylık projeksiyondaki net sonucunu canlı simülatörde test eder.


4. **Kişisel Veri Mahremiyeti ve Çevrimdışı Güç:** Kullanıcının en hassas verisi olan finansal durumunu uzak sunuculara bağımlı kılmadan, cihaz üzerinde (offline-first) tam güvenlik ve hızla işletir.



---

## 4. Temel Değer Önerisi (Value Proposition)

### Mikro Takip Değil, Makro Denge (Mizan)

Geleneksel bütçe uygulamaları kullanıcıyı veri giriş memuruna dönüştürür ve birkaç hafta içinde terk edilir. Mizan ise **dönem kapanış mutabakatı (Plan vs. Gerçek)** mantığıyla çalışır:

* Dönem başında plan dondurulur.


* Dönem içinde tek bir operasyonel gözlem yapılır.


* Dönem bittiğinde gerçekleşen zorunlu ödemeler teyit edilir, fiili yaşam gideri tek kalemde yazılır ve yeni dönemin açılışı tek dokunuşla başlatılır.



### Kimler İçin?

* **Maaşlı Profesyoneller:** Kredi kartı ekstreleri, tüketici kredileri ve birikim hedefleri arasında ay sonunu ve gelecek 12 ayı faiz tuzağına düşmeden planlamak isteyenler.


* **Esnaf, Serbest Meslek ve Düzensiz Gelir Sahipleri:** Gelir tarihleri ve tutarları değişken olan, ancak kira, vergi, tedarikçi ve kredi gibi sabit yükümlülükleri düzenli işleyen; likidite açığı riskini önceden öngörmek zorunda olanlar.
* **Finansal Optimizasyon Arayanlar:** Elindeki nakit fazlasıyla hangi krediyi ne zaman kapatırsa ne kadar faiz tasarrufu sağlayacağını hesaplayan analitik kullanıcılar.



---

## 5. Ürünün 5 Temel Taşıyıcı Sütunu

```
                  ┌─────────────────────────────────────────┐
                  │              M I Z A N                  │
                  │   Finansal Projeksiyon & Karar Motoru   │
                  └────────────────────┬────────────────────┘
                                       │
     ┌──────────────────┬──────────────┴─────┬──────────────────┬─────────────────┐
     │                  │                    │                  │                 │
┌────┴─────────┐ ┌──────┴────────┐ ┌─────────┴────────┐ ┌───────┴────────┐ ┌──────┴─────────┐
│ 1. Dönem     │ │ 2. İleriye    │ │ 3. Akıllı        │ │ 4. Gerçekçi    │ │ 5. Operasyonel  │
│ Döngüsü ve   │ │ Dönük 12 Aylık│ │ Senaryo          │ │ Maliyet & Faiz │ │ Takip ve        │
│ Mutabakat    │ │ Projeksiyon   │ │ Simülatörü       │ │ Modellemesi    │ │ Hatırlatıcı     │
│[cite: 1]    │ │[cite: 1]     │ │[cite: 1]        │ │[cite: 1]      │ │[cite: 1]       │
└──────────────┘ └───────────────┘ └──────────────────┘ └────────────────┘ └─────────────────┘

```

1. **Dönem Döngüsü ve Mutabakat (Plan vs. Gerçek):** Finansal hayatı dondurulmuş planlar ve gerçekleşmeler ekseninde disipline eder; sapmaları (Reconciliation Adjustment) net biçimde raporlar.


2. **İleriye Dönük 12 Aylık Projeksiyon:** Anchor snapshot noktasından başlayarak tam 1 yıl boyunca kümülatif likiditeyi ve finansman açıklarını gün gün hesaplar.


3. **Akıllı Senaryo Simülatörü:** Geçici planlar (Temporary Plans) oluşturma, koşulları tek tek açıp kapatarak test etme ve tek tuşla canlı finansal yapıya aktarma ("Planı Uygula") gücü sunar.


4. **Gerçekçi Maliyet ve Faiz Modellemesi:** Kredi kartı ekstre carry faizleri ve açık faizlerini gerçek bankacılık yuvarlama ve kurallarıyla hesaplayarak kullanıcıyı sürpriz açık maliyetlerine karşı uyarır.


5. **Operasyonel Takip ve Hatırlatıcı:** Rahat ve agresif bildirim modları, tek dokunuşla "Ödedim" / "Ertele" aksiyonları ile dönem içindeki taahhütlerin kaçırılmasını engeller.



---

## 6. Dil, Ton ve UX İlkeleri

* **Ciddi, Olgun ve Finansal Dil:** "Cebinde ne kaldı?", "Harcama canavarı" gibi laubali ifadeler yerine; **"Dönem Neti"**, **"Serbest Harcama Limiti"**, **"Finansman Açığı"**, **"Dönem Mutabakatı"** gibi güven veren terminoloji kullanılır.


* **Kalıcı Değer vs. Anlık Gözlem:** Kullanıcının ana ekranda girdiği mevcut bakiye bir projeksiyon çöpü değil; sistemin yönünü doğrulamaya yarayan bir "Gözlem Noktası"dır. Planı bozmaz, sapmayı gösterir.


* **Görsel Tasarım Dili:** Neon ve aşırı doygun renkler yerine; derin lacivert/arduvaz zeminler, net tipografi, güven verici kobalt mavisi ve ölçülü finansal durum renkleri (zümrüt yeşili / koyu kırmızı) hakimdir.

---

Bu belge, repoya `docs/VISION_AND_MISSION.md` olarak eklenebilir veya Google Flow arayüz çizimlerinde ürünün değişmez anayasası olarak referans alınabilir.