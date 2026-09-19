# Mizan — Antigravity & Agent Kuralları (RULES.md)

Mizan, kişisel nakit akışı, bütçe, kredi kartı ve borç yönetimini matematiksel kesinlik ve Clean Architecture prensipleriyle yöneten bir finansal karar destek sistemidir.

Bu dosya **oturum başlangıcında hafif bağlam (low token footprint)** sağlamak üzere tasarlanmıştır. Detaylı kural ve iş akışları için ilgili alt modüllere başvurun.

---

## 1. TEKNOLOJİ YIĞINI & MİMARİ

- **Platform:** .NET 8 (C# 12) & .NET MAUI (`Mizan.App`)
- **Mimari:** Clean Architecture (Sıkı Bağımlılık Yönü):
  ```text
  Mizan.App (MAUI Pages, Controls)
     │
     ▼
  CoinFlow.UI / Presentation (Saf .NET 8 ViewModels, Converters)
     │
     ▼
  Mizan.Application (Use Cases, Servisler, DTO'lar, Repository Arayüzleri)
     │
     ▼
  Mizan.Domain (Sıfır Bağımlılık: Pure Calculators, Modeller, İnvaryantlar)
     ▲
     │
  Mizan.Infrastructure (SQLite Store, PDF Importer, IO Adaptörleri)
  ```

---

## 2. MUTLAK KURALLAR (KIRMIZI ÇİZGİLER)

1. **Pre-flight & Post-flight Test Doğrulaması:** Koda dokunmadan önce ve görevi tamamlamadan önce `dotnet test` koşturulmalıdır. Asla kırık test bırakılamaz.
2. **Invariant I16 (Dondurulmuş Plan Dokunulmazlığı):** Dönem başladığında dondurulan bütçe (`openPlan` / `PLANLANAN`) dönem içi harcamalarla **asla değiştirilemez**. Harcamalar yalnızca `MEVCUT` ve `GİDİŞAT`'a yansır.
3. **ViewModel Boyut Sınırı ($\le$ 350 Satır):** Hiçbir ViewModel tek dosyada 350 satırı aşamaz. Büyük sınıflar `partial` dosyalara (`*.Operations.cs`, `*.Cards.cs` vb.) bölünür.
4. **UI Bağımsızlığı & Yasaklı Kalıplar:**
   - ViewModel içinde `Page`, `Shell`, `NavigationPage` veya MAUI UI kontrol referansı KESİNLİKLE YASAKTIR.
   - `IServiceProvider` enjekte edilemez (Service Locator yasaktır, constructor injection şarttır).
   - `async void` KESİNLİKLE YASAKTIR (`Task` veya `ValueTask` dönülmelidir).
5. **Sahte Test Yasağı (`*SourceTests.cs`):** `File.ReadAllText(...)` ile kaynak kodu metin olarak tarayan testler KESİNLİKLE YASAKTIR. Testler çalışma zamanı davranışını doğrulamalıdır.

---

## 3. MODÜLER KURAL VE İŞ AKIŞI İNDEKSİ

İlgili göreve göre aşağıdaki modülleri bağlamınıza dahil edin:

| Konu / Görev | İlgili Dosya | Açıklama |
| :--- | :--- | :--- |
| **Kodlama Standartları** | [rules/code-style.md](rules/code-style.md) | C# 12 idiomları, katman kuralları, MVVM & partial ViewModel desenleri |
| **Test Standartları** | [rules/testing.md](rules/testing.md) | xUnit standartları, Mocking, Core Business regresyon protokolü |
| **API & Servisler** | [rules/api-conventions.md](rules/api-conventions.md) | Servis sözleşmeleri, pure calculator yapısı, hata yönetimi ve invaryantlar |
| **Kod İnceleme** | [workflows/code-review.md](workflows/code-review.md) | Değişiklikleri doğrulamak için adım adım inceleme kontrol listesi |
| **Hata Giderme** | [workflows/fix-issue.md](workflows/fix-issue.md) | Hata ayıklama, xUnit ile yeniden üretme ve doğrulama akışı |
| **İnceleme Ajanı** | [agents/code-reviewer.md](agents/code-reviewer.md) | Kod incelemesi için özelleşmiş subagent sistem promptu |
| **Güvenlik Ajanı** | [agents/security-auditor.md](agents/security-auditor.md) | Güvenlik ve veri bütünlüğü denetimi subagent sistem promptu |
| **Yerel Ayarlar** | `RULES.local.md` | Geliştiricinin yerel ortam override'ları (.gitignore'da) |
