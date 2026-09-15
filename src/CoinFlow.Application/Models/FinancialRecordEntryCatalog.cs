namespace CoinFlow.Application.Models;

public enum RecordEntryGroup
{
    Spending,
    Debt,
    Income,
    Account
}

/// <summary>Seçilen kayıt türünün hangi formla girildiği.</summary>
public enum RecordEntryForm
{
    /// <summary>Simülatörün koşul formu; kayıt simülasyonla aynı yoldan yazılır.</summary>
    SharedForm,
    Salary,
    Loan,
    CreditCard,
    PaymentPlan
}

/// <summary>
/// Finansal Yapı'da "+ Ekle" ile açılan kayıt türü kartı. Ortak formdan
/// girilen türler simülatördeki seçeneğin kendisini taşır.
/// </summary>
public sealed record RecordEntryOption(
    string Key,
    RecordEntryGroup Group,
    string Title,
    string Summary,
    string Description,
    RecordEntryForm Form,
    ScenarioOption? Scenario = null);

/// <summary>
/// Finansal Yapı'nın ekleme alanı. Simülatördeki plan türü seçimiyle aynı
/// tasarım: önce grup çipi, sonra en fazla üç açıklamalı kart. Eskiden
/// yedi seçenekli bir menüydü ve seçenek ancak seçildikten sonra anlatılıyordu.
/// </summary>
public static class FinancialRecordEntryCatalog
{
    public const int MaxOptionsPerGroup = 3;

    public static IReadOnlyList<(RecordEntryGroup Group, string Label)> Groups { get; } =
    [
        (RecordEntryGroup.Spending, "Harcama"),
        (RecordEntryGroup.Debt, "Borç / Kredi"),
        (RecordEntryGroup.Income, "Gelir"),
        (RecordEntryGroup.Account, "Hesap")
    ];

    public static RecordEntryOption Salary { get; } = new(
        "salary-change",
        RecordEntryGroup.Income,
        "Maaş / gelir değişikliği",
        "Düzenli gelirin ya da yeni tutarı",
        "Düzenli gelirini ya da bir tarihten itibaren değişen tutarını ekle. İlk gelir kaydı gelir kullanım düzeni kurulumunu başlatır.",
        RecordEntryForm.Salary);

    public static RecordEntryOption CreditCard { get; } = new(
        "credit-card",
        RecordEntryGroup.Account,
        "Kredi kartı",
        "Limit, borç ve ekstre günleri",
        "Kartın limiti, borcu ve ekstre günleri. Ödeme kararlarını kaydettikten sonra kart ekranından verirsin.",
        RecordEntryForm.CreditCard);

    public static RecordEntryOption Loan { get; } = new(
        "bank-loan",
        RecordEntryGroup.Account,
        "Bankadaki kredi",
        "Devam eden kredinin kalan taksitleri",
        "Bankada zaten devam eden bir kredinin taksitleri. Yeni kredi çekmeyi denemek için Borç / Kredi grubunu kullan.",
        RecordEntryForm.Loan);

    public static RecordEntryOption PaymentPlan { get; } = new(
        "payment-plan",
        RecordEntryGroup.Account,
        "Değişken ödeme planı",
        "Tutarı ya da tarihi aydan aya değişen ödemeler",
        "Tutarı ya da tarihi aydan aya değişen ödemeler; her ödemeyi tarihiyle ekle. Her ay aynı tutarsa Harcama → Düzenli ödeme daha kısa.",
        RecordEntryForm.PaymentPlan);

    public static IReadOnlyList<RecordEntryOption> Options { get; } = Build();

    public static RecordEntryOption Default => Options[0];

    public static IReadOnlyList<RecordEntryOption> OptionsIn(RecordEntryGroup group) =>
        Options.Where(x => x.Group == group).ToArray();

    public static RecordEntryOption For(string key) =>
        Options.Single(x => x.Key == key);

    private static IReadOnlyList<RecordEntryOption> Build()
    {
        var shared = SimulationScenarioCatalog.Options
            .Where(x => x.EntryHome == ScenarioEntryHome.SharedForm)
            .Select(option => new RecordEntryOption(
                option.Key,
                option.Group switch
                {
                    ScenarioGroup.Spending => RecordEntryGroup.Spending,
                    ScenarioGroup.Debt => RecordEntryGroup.Debt,
                    ScenarioGroup.Income => RecordEntryGroup.Income,
                    _ => throw new InvalidOperationException(
                        $"{option.Title} ortak formdan girilemez.")
                },
                option.Title,
                option.Summary,
                option.Description,
                RecordEntryForm.SharedForm,
                option));
        return shared
            .Append(Salary)
            .Append(CreditCard)
            .Append(Loan)
            .Append(PaymentPlan)
            .OrderBy(x => x.Group)
            .ToArray();
    }
}
