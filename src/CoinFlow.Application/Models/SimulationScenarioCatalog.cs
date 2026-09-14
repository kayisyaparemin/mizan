using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public enum ScenarioGroup
{
    Spending,
    Debt,
    Income,
    Setting
}

/// <summary>
/// Bir türün simülatör dışında nereden girildiği. Simüle edilebilen her şey
/// doğrudan da girilebilmeli; ortak form girmiyorsa türün kendi ekranı var.
/// </summary>
public enum ScenarioEntryHome
{
    /// <summary>Finansal Yapı'da simülatörle aynı formdan doğrudan kaydedilir.</summary>
    SharedForm,
    /// <summary>Finansal Yapı'nın gelir formu; ilk kaydı düzen kurulumunu başlatır.</summary>
    SalaryForm,
    /// <summary>Kart kontrol ekranındaki ödeme kararları.</summary>
    CardControl,
    /// <summary>Ayarlar'daki gelir kullanım düzeni.</summary>
    Settings
}

/// <summary>
/// Kullanıcının seçtiği plan türü. Birden fazla motor türünü kapsayabilir:
/// tek çekim ile taksitli kart harcaması aynı hesaptır, farkı taksit
/// sayısıdır — bunu kullanıcıya iki ayrı tür olarak sormak listeyi şişiriyordu.
/// </summary>
public sealed record ScenarioOption(
    string Key,
    ScenarioGroup Group,
    string Title,
    string Summary,
    string Description,
    IReadOnlyList<SimulationScenarioType> Types,
    ScenarioEntryHome EntryHome)
{
    public SimulationScenarioType DefaultType => Types[0];
}

/// <summary>
/// Simülatör ve Finansal Yapı'nın paylaştığı tek tür listesi. Enum değişmez;
/// kayıtlı geçici planlar enum değerini sakladığı için katalog yalnız sunumu
/// gruplar.
/// </summary>
public static class SimulationScenarioCatalog
{
    public const int MaxOptionsPerGroup = 3;

    public static IReadOnlyList<(ScenarioGroup Group, string Label)> Groups { get; } =
    [
        (ScenarioGroup.Spending, "Harcama"),
        (ScenarioGroup.Debt, "Borç / Kredi"),
        (ScenarioGroup.Income, "Gelir"),
        (ScenarioGroup.Setting, "Ayar")
    ];

    public static ScenarioOption CashPayment { get; } = new(
        "cash",
        ScenarioGroup.Spending,
        "Nakit ödeme",
        "Tutar seçtiğin gün hesabından düşer",
        "Tutar, seçtiğin tarihte finansal durumundan düşer.",
        // Eski "Tek seferlik ödeme" koşulları da bu seçenekte görünür; yeni
        // koşullar planlı büyük gider olarak yazılır.
        [SimulationScenarioType.CashPurchase, SimulationScenarioType.FutureOneTimePayment],
        ScenarioEntryHome.SharedForm);

    public static ScenarioOption CardSpending { get; } = new(
        "card",
        ScenarioGroup.Spending,
        "Kartla harcama",
        "Taksit sayısı 1 ise tek çekim",
        "Harcama, kartının ekstre kesim ve son ödeme tarihlerine göre hesaplanır; taksitler ilgili ekstrelere yansıtılır.",
        [SimulationScenarioType.CreditCardSinglePayment, SimulationScenarioType.CreditCardInstallmentPurchase],
        ScenarioEntryHome.SharedForm);

    public static ScenarioOption RecurringPayment { get; } = new(
        "recurring",
        ScenarioGroup.Spending,
        "Düzenli ödeme",
        "Her ay aynı tutar, belirlediğin ay sayısı kadar",
        "Girilen tutar, belirtilen dönem sayısı boyunca aylık tekrarlanır.",
        [SimulationScenarioType.RecurringPayment],
        ScenarioEntryHome.SharedForm);

    public static ScenarioOption Financing { get; } = new(
        "financing",
        ScenarioGroup.Debt,
        "Kredi / finansman çek",
        "Para bugün gelir, geri ödeme taksitle çıkar",
        "Kredi tutarı işlem tarihinde gelir olarak eklenir; toplam geri ödeme, ilk ödeme tarihinden başlayarak taksitlere bölünür.",
        [SimulationScenarioType.FinancingLoan],
        ScenarioEntryHome.SharedForm);

    public static ScenarioOption CashDebt { get; } = new(
        "cash-debt",
        ScenarioGroup.Debt,
        "Taksitli nakit borç",
        "Borcu eşit ödemelere böl",
        "Borç tutarı, seçtiğin ödeme sayısına kuruş farkı bırakmadan bölünür.",
        [SimulationScenarioType.CashDebt],
        ScenarioEntryHome.SharedForm);

    public static ScenarioOption LoanPrepayment { get; } = new(
        "loan-prepayment",
        ScenarioGroup.Debt,
        "Krediye erken ödeme",
        "Tamamen kapat ya da ara ödeme yap",
        "Tamamen kapatırsan kalan anapara ve son taksitten bu yana işleyen faiz tek seferde ödenir; sonraki taksitler kalkar. Ara ödemede girdiğin tutar anaparadan düşer: vadeyi kısaltırsan taksit aynı kalır, taksiti azaltırsan kredi aynı tarihte biter. Taksit gününde ödersen işleyen faiz olmaz; tüketici kredisinde erken ödeme ücreti alınamaz.",
        [SimulationScenarioType.LoanEarlyClosure, SimulationScenarioType.LoanPartialPrepayment],
        ScenarioEntryHome.SharedForm);

    public static ScenarioOption OneTimeIncome { get; } = new(
        "income",
        ScenarioGroup.Income,
        "Tek seferlik gelir",
        "Prim, satış, iade gibi bir kerelik para",
        "Gelir, seçtiğin tarihin dahil olduğu döneme eklenir.",
        [SimulationScenarioType.FutureIncome],
        ScenarioEntryHome.SharedForm);

    public static ScenarioOption SalaryChange { get; } = new(
        "salary",
        ScenarioGroup.Income,
        "Gelir değişikliği",
        "Maaşın bir tarihten itibaren değişir",
        "Yeni gelir, seçtiğin tarihten itibaren kullanılır.",
        [SimulationScenarioType.SalaryChange],
        ScenarioEntryHome.SalaryForm);

    public static ScenarioOption CardPaymentMode { get; } = new(
        "card-payment-mode",
        ScenarioGroup.Setting,
        "Kart ödeme şekli",
        "Ekstreyi asgari ya da tamamen öde",
        "Kartın ödeme şeklini değiştirir. Kart faizi ile finansman açığı faizi ters yönde hareket edebilir; Faiz Karşılaştırması ikisini ayrı gösterir.",
        [SimulationScenarioType.CreditCardPaymentMode],
        ScenarioEntryHome.CardControl);

    public static ScenarioOption PaymentStrategy { get; } = new(
        "payment-strategy",
        ScenarioGroup.Setting,
        "Gelir kullanım düzeni",
        "Maaşın hangi ödemeleri karşılayacağı",
        "Yeni düzen yalnızca seçtiğin dönemden itibaren hesaplanır; Simülasyon Yap finans kayıtlarını değiştirmez.",
        [SimulationScenarioType.PaymentStrategyChange],
        ScenarioEntryHome.Settings);

    public static IReadOnlyList<ScenarioOption> Options { get; } =
    [
        CashPayment,
        CardSpending,
        RecurringPayment,
        Financing,
        CashDebt,
        LoanPrepayment,
        OneTimeIncome,
        SalaryChange,
        CardPaymentMode,
        PaymentStrategy
    ];

    public static string GroupLabel(ScenarioGroup group) =>
        Groups.Single(x => x.Group == group).Label;

    public static IReadOnlyList<ScenarioOption> OptionsIn(
        ScenarioGroup group,
        bool directEntryOnly = false) =>
        Options
            .Where(x => x.Group == group)
            .Where(x => !directEntryOnly ||
                        x.EntryHome == ScenarioEntryHome.SharedForm)
            .ToArray();

    public static ScenarioOption For(SimulationScenarioType type) =>
        Options.Single(x => x.Types.Contains(type));

    public static bool IsDirectEntry(SimulationScenarioType type) =>
        For(type).EntryHome == ScenarioEntryHome.SharedForm;

    /// <summary>
    /// Seçenek ve formdaki değerlerden motor türünü çözer.
    /// </summary>
    /// <param name="editingType">
    /// Düzenlenen koşulun kayıtlı türü. Eski bir "Tek seferlik ödeme" koşulu
    /// türünü korur: uygulanmış bir koşulun kimliği başka türe geçerse uygulama
    /// onu tanıyamaz ve ikinci kez kaydeder.
    /// </param>
    public static SimulationScenarioType Resolve(
        ScenarioOption option,
        int paymentCount,
        LoanPrepaymentMode? prepaymentMode,
        SimulationScenarioType? editingType = null)
    {
        if (option.Key == CardSpending.Key)
        {
            return paymentCount > 1
                ? SimulationScenarioType.CreditCardInstallmentPurchase
                : SimulationScenarioType.CreditCardSinglePayment;
        }

        if (option.Key == LoanPrepayment.Key)
        {
            return prepaymentMode is null or LoanPrepaymentMode.FullClosure
                ? SimulationScenarioType.LoanEarlyClosure
                : SimulationScenarioType.LoanPartialPrepayment;
        }

        if (option.Key == CashPayment.Key &&
            editingType == SimulationScenarioType.FutureOneTimePayment)
        {
            return SimulationScenarioType.FutureOneTimePayment;
        }

        return option.DefaultType;
    }

    /// <summary>Koşul listesinde türü anlatan kısa etiket.</summary>
    public static string TypeText(SimulationScenarioType type) =>
        type switch
        {
            SimulationScenarioType.CashPurchase => "Nakit ödeme",
            SimulationScenarioType.CreditCardSinglePayment => "Karttan tek çekim",
            SimulationScenarioType.CreditCardInstallmentPurchase => "Kart taksitli harcama",
            SimulationScenarioType.FinancingLoan => "Finansman / kredi",
            SimulationScenarioType.CashDebt => "Taksitli nakit borç",
            SimulationScenarioType.FutureOneTimePayment => "Tek seferlik ödeme",
            SimulationScenarioType.RecurringPayment => "Düzenli ödeme",
            SimulationScenarioType.FutureIncome => "Tek seferlik gelir",
            SimulationScenarioType.SalaryChange => "Gelir değişikliği",
            SimulationScenarioType.PaymentStrategyChange => "Gelir kullanım düzeni",
            SimulationScenarioType.CreditCardPaymentMode => "Kart ödeme şekli",
            SimulationScenarioType.LoanEarlyClosure => "Kredi erken kapama",
            SimulationScenarioType.LoanPartialPrepayment => "Kredi ara ödeme",
            _ => "Koşul"
        };
}
