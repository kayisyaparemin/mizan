# Devir Notu (Handoff) — Mizan / CoinFlow

> Bu not, geliştirmenin başka bir makineye taşınması için yazıldı. Yeni Claude
> Code oturumu bu dosyayı okuyarak kaldığımız yerden devam edebilir.
> Son güncelleme: 2026-09-04 · İlgili commit: `de1879a`

## Proje kısa özeti
.NET MAUI (net8.0-android) mobil uygulama. Katmanlar: `CoinFlow.App` (MVVM UI),
`CoinFlow.Application` (workflow/servisler), `CoinFlow.Infrastructure` (PdfPig ile
PDF okuma + banka parser'ları), `CoinFlow.Domain` (hesaplamalar). Android APK'sı
GitHub Actions ile üretiliyor. (CLAUDE.md "backend" der ama gerçekte mobil app.)

## Aktif görev: Ekstre (PDF) import ANR düzeltmesi
Belirti: Ekstre import'ta PDF seçilince uygulama donuyor, Android "yanıt vermiyor"
(ANR) penceresi çıkıp döngüye giriyordu; PDF de yüklenmiyordu. **Kök neden bulundu
ve düzeltildi** (commit `de1879a`, `main`). Cihazda son doğrulama bekliyor.

### Kök neden (ANR trace ile kesinleştirildi)
- **PDF çıkarımı (PdfPig) sorun DEĞİL** — `Task.Run` ile arka planda çalışıp bitiyor.
  ANR anındaki trace: tüm `.NET TP Worker`'lar idle, sadece **ana thread %100 CPU'da
  managed (JIT) kod** çalıştırıyordu → ana thread'de sonsuz döngü.
- **Birincil hata:** `CoinFlow.App/Services/UserFeedbackService.cs → ResolveTopPage`.
  Paylaşımlı `Navigation.ModalStack` üzerinde `while` döngüsü, stack'te modal varken
  hep aynı en üst modal'ı seçip **hiç bitmiyordu**. Import parse'ı başarısız olunca
  (`HasRequiredFields=false`) `ShowManualFallbackAsync` → `feedback.ShowErrorAsync`
  bu yolu tetikliyordu. Düzeltme: modal'ı bir kez oku, `DescendToVisiblePage` ile
  `ModalStack`'i tekrar okumadan in.
- **İkincil / önleyici:** `CoinFlow.App/Pages/CardControlPage.xaml` taslak (draft)
  bölümünde tarih alanları `Grid *,*` yıldız hücrelerine gömülü `VerticalStackLayout`
  içeriyordu → MAUI Android arrange sonsuz döngüsü (dotnet/maui issue #21798 sınıfı).
  Kontroller Grid hücresine doğrudan konacak şekilde düzleştirildi (görsel aynı).
- **Regresyon testi:** `tests/.../StatementImportArchitectureSourceTests.cs` içine
  `ModalStack` üzerinde `while` döngüsünü yasaklayan bir kaynak testi eklendi.

## Sıradaki adımlar
1. **Cihazda doğrula:** `dev-build.yml` yeşil mi? `dev-latest` APK'sını indir, ekstre
   import'u dene → ANR gitti mi? (Gitmezse: aynı yöntemle yeni ANR trace al —
   `adb shell "dumpsys dropbox --print data_app_anr" > trace.txt` — ve layout tarafını
   kesinleştir.)
2. **Ayrı iş — "pdf otomatik dolmuyor":** Bu ANR'den bağımsız. Muhtemel nedenler:
   `PdfPigPdfTextExtractor` sadece **sayfa 1** metnini okuyor + yalnızca **2 parser**
   var (Akbank Axess, Garanti Bonus). İyileştirme seçenekleri: çok sayfa okuma, daha
   çok banka parser'ı, OCR fallback. Değerleri bulamazsa "elle gir" ekranı açılır.

## Derleme / test / dağıtım
- **Testler (çapraz platform, Android SDK gerekmez):**
  `dotnet test tests/CoinFlow.Tests/CoinFlow.Tests.csproj -c Release` → 201 test.
- **Android derleme:** `maui-android` workload + Android SDK gerekir. Yereldeki tam
  derleme doğrulaması Android SDK'sız yapılamaz; CI doğrular.
- **APK:** `.github/workflows/dev-build.yml` — `main`'e push → test → APK →
  `dev-latest` ön-sürümü (`com.coinflow.mobile.dev`, etiket "Mizan Dev").
- Sürümleme: `1.0.1-dev.<GITHUB_RUN_NUMBER>`.

## Notlar / tuzaklar
- `CLAUDE.md` ve `repomix-output.xml` git'te **İZLENMİYOR** (yalnızca eski makinede,
  local). Yeni makinede yoksa proje çalışma kuralları da orada yok demektir — gerekirse
  `CLAUDE.md`'yi commit'lemeyi değerlendir.
- Git push, eski makinede kurumsal proxy nedeniyle SSL hatası veriyordu; çözüm:
  `git -c http.sslBackend=schannel push ...` (Windows sertifika deposu, doğrulama korunur).
  Yeni makinede gerekmeyebilir.
