using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SimulationViewModel
{
    private void Populate(SimulationResult result)
    {
        var projectionSummary = simulatorInsightService.Build(result.Scenario);
        AssignmentModeText = AssignmentModeLabel(
            result.Scenario[0].PaymentAssignmentMode);
        InterestComparison.Clear();
        foreach (var row in SimulatorInsightService.BuildInterestComparison(
                     result.BaselineInterest,
                     result.ScenarioInterest,
                     result.Risk.FinancingCost))
        {
            InterestComparison.Add(row);
        }
        PopulateLoanImpacts(result.LoanImpacts);
        _lastBaselineProjection = result.Baseline;
        _lastScenarioProjection = result.Scenario;

        NarrativeInsights.Clear();
        foreach (var insight in projectionSummary.NarrativeInsights)
        {
            NarrativeInsights.Add(insight);
        }

        SummaryMetrics.Clear();
        foreach (var metric in projectionSummary.KeyMetrics)
        {
            SummaryMetrics.Add(metric);
        }

        Results.Clear();
        foreach (var row in projectionSummary.Periods)
        {
            Results.Add(row);
        }
    }

    private void PopulateBaseline(
        IReadOnlyList<SalaryPeriodProjection> baseline)
    {
        var projectionSummary = simulatorInsightService.Build(baseline);
        AssignmentModeText = AssignmentModeLabel(
            baseline[0].PaymentAssignmentMode);
        LoanImpacts.Clear();
        HasLoanImpacts = false;
        InterestComparison.Clear();

        NarrativeInsights.Clear();
        foreach (var insight in projectionSummary.NarrativeInsights)
        {
            NarrativeInsights.Add(insight);
        }

        SummaryMetrics.Clear();
        foreach (var metric in projectionSummary.KeyMetrics)
        {
            SummaryMetrics.Add(metric);
        }

        Results.Clear();
        foreach (var row in projectionSummary.Periods)
        {
            Results.Add(row);
        }
    }

    private void PopulateLoanImpacts(
        IReadOnlyList<LoanPrepaymentImpact> impacts)
    {
        LoanImpacts.Clear();
        foreach (var impact in impacts)
        {
            var end = impact.ScenarioEndDate is { } scenarioEnd
                ? scenarioEnd.ToString("MMMM yyyy", TurkishCulture)
                : "—";
            var baselineEnd = impact.BaselineEndDate is { } baseline
                ? baseline.ToString("MMMM yyyy", TurkishCulture)
                : "—";
            var installment = impact.ScenarioMonthlyPayment is decimal payment &&
                              payment != impact.BaselineMonthlyPayment
                ? $" · taksit {Money(impact.BaselineMonthlyPayment)} → {Money(payment)}"
                : string.Empty;
            LoanImpacts.Add(new LoanImpactLine(
                impact.LoanName,
                $"Ödenecek {Money(impact.PrepaidAmount)} · son ödeme {baselineEnd} → {end}{installment}",
                Money(impact.InterestSaving)));
        }

        HasLoanImpacts = LoanImpacts.Count > 0;
    }

    private string BuildApplyConfirmation(IReadOnlyList<SimulationRequest> requests)
    {
        if (requests.Count == 1)
        {
            return BuildApplyConfirmation(requests[0]);
        }

        var preview = string.Join(
            Environment.NewLine,
            EnabledConditions()
                .Take(6)
                .Select(x => $"• {x.DateText} — {x.SummaryText}"));
        return
            $"Bu simülasyon planındaki {requests.Count} koşul gerçek finans planına birlikte eklenecek.\n\n{preview}\n\nHer şey tek seferde kaydedilir; bir koşul kaydedilemezse hiçbir değişiklik yapılmaz.";
    }

    private string BuildApplyConfirmation(SimulationRequest request)
    {
        var summary = request.Type is
            SimulationScenarioType.PaymentStrategyChange or
            SimulationScenarioType.CreditCardPaymentMode or
            SimulationScenarioType.LoanEarlyClosure
                ? request.Name.Trim()
                : $"{Money(request.Amount)} {request.Name.Trim()}";
        var detail = request.Type switch
        {
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"Kart: {Form.CardLabel(request.CreditCardId)}\n{request.PaymentCount} taksit\nİşlem: {LongDate(request.StartDate)}",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"Kart: {Form.CardLabel(request.CreditCardId)}\nİşlem: {LongDate(request.StartDate)}",
            SimulationScenarioType.CreditCardPaymentMode =>
                $"Kart: {Form.CardLabel(request.CreditCardId)}\n{CardModeLabel(request.CardPaymentType)} · {CardScopeLabel(request.AppliesToAllStatements)}\n{LongDate(request.StartDate)}",
            SimulationScenarioType.FinancingLoan =>
                $"{request.PaymentCount} taksit • toplam {Money(request.TotalRepaymentAmount.GetValueOrDefault())}\nİlk ödeme: {LongDate(request.FirstPaymentDate)}",
            SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment =>
                $"{request.PaymentCount} ödeme\nİlk ödeme: {LongDate(request.FirstPaymentDate)}",
            SimulationScenarioType.PaymentStrategyChange =>
                $"Başlangıç dönemi: {LongDate(request.EffectiveSalaryDate)}",
            SimulationScenarioType.LoanEarlyClosure =>
                $"Kredi: {Form.LoanLabel(request.LoanId)}\nKapatma: {LongDate(request.StartDate)}\nTutar o günkü kalan anapara ve işleyen faizden hesaplanır.",
            SimulationScenarioType.LoanPartialPrepayment =>
                $"Kredi: {Form.LoanLabel(request.LoanId)}\nAnaparadan düşecek: {Money(request.Amount)} · {PrepaymentModeLabel(request.PrepaymentMode)}\nTarih: {LongDate(request.StartDate)}",
            _ => $"Tarih: {LongDate(request.StartDate)}"
        };
        return $"Bu plan gerçek finans planına eklenecek.\n\n{summary}\n{detail}";
    }

    private string ConditionSummaryText(SimulationRequest request)
    {
        var amount = Money(request.Amount);
        return request.Type switch
        {
            SimulationScenarioType.CashPurchase =>
                $"{amount} • Nakit ödeme",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"{Form.CardLabel(request.CreditCardId)} • {amount} • Tek çekim",
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"{Form.CardLabel(request.CreditCardId)} • {amount} • {request.PaymentCount} taksit",
            SimulationScenarioType.FinancingLoan =>
                $"{amount} • {request.PaymentCount} taksit • toplam {Money(request.TotalRepaymentAmount.GetValueOrDefault())}",
            SimulationScenarioType.CashDebt =>
                $"{amount} • {request.PaymentCount} ödeme",
            SimulationScenarioType.FutureOneTimePayment =>
                $"{amount} • Tek seferlik ödeme",
            SimulationScenarioType.RecurringPayment =>
                $"{amount}/ay • {request.PaymentCount} ay",
            SimulationScenarioType.FutureIncome =>
                $"{amount} • Tek seferlik gelir",
            SimulationScenarioType.SalaryChange =>
                $"{amount} • Yeni gelir",
            SimulationScenarioType.PaymentStrategyChange =>
                $"{StrategyModeLabel(request.NewPaymentAssignmentMode)} • {LongDate(request.EffectiveSalaryDate)} dönemi",
            SimulationScenarioType.CreditCardPaymentMode =>
                $"{Form.CardLabel(request.CreditCardId)} • {CardModeLabel(request.CardPaymentType)}",
            SimulationScenarioType.LoanEarlyClosure =>
                $"{Form.LoanLabel(request.LoanId)} • Erken kapama",
            SimulationScenarioType.LoanPartialPrepayment =>
                $"{Form.LoanLabel(request.LoanId)} • {amount} ara ödeme • {PrepaymentModeLabel(request.PrepaymentMode)}",
            _ => request.Name
        };
    }

    private static string LongDate(DateOnly? date) =>
        date?.ToString("dd MMMM yyyy", TurkishCulture) ?? string.Empty;

    private static string PrepaymentModeLabel(LoanPrepaymentMode? mode) =>
        mode == LoanPrepaymentMode.ReduceInstallment
            ? "taksit azalır"
            : "vade kısalır";

    private static string StrategyModeLabel(PaymentAssignmentMode? mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";

    private static string CardModeLabel(CreditCardPaymentType? paymentType) =>
        paymentType == CreditCardPaymentType.Minimum
            ? "Asgari öde"
            : "Tamamını öde";

    private static string CardScopeLabel(bool appliesToAllStatements) =>
        appliesToAllStatements
            ? "Bundan sonraki tüm ekstreler"
            : "Yalnızca bu ekstre";

    private static string TargetPeriodText(SalaryPeriod period) =>
        period.Start.ToString("MMMM yyyy", TurkishCulture);

    private static string AssignmentModeLabel(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Gelir kullanımı: Geçmiş dönemi kapatırım"
            : "Gelir kullanımı: Gelecek dönemi karşılarım";

    private void ClearResults()
    {
        Results.Clear();
        LoanImpacts.Clear();
        HasLoanImpacts = false;
        NarrativeInsights.Clear();
        SummaryMetrics.Clear();
        InterestComparison.Clear();
        _lastBaselineProjection = [];
        HasResults = false;
        IsResultStale = false;
        IsBaselineOnly = false;
        ResetApplyState(clearRequest: true);
        _lastScenarioProjection = [];
        ClearTargetResult();
    }

    [RelayCommand]
    private void FindTarget()
    {
        try
        {
            var target = ParsePositiveMoney(TargetAmount, "Hedef tutar");
            UpdateTargetResult(target);
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            ClearTargetResult();
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private void RefreshTargetResultAfterSimulation()
    {
        if (string.IsNullOrWhiteSpace(TargetAmount))
        {
            ClearTargetResult();
            return;
        }

        try
        {
            var target = ParsePositiveMoney(TargetAmount, "Hedef tutar");
            UpdateTargetResult(target);
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            ClearTargetResult();
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private void UpdateTargetResult(decimal target)
    {
        var projection = _lastScenarioProjection.Count > 0
            ? _lastScenarioProjection
            : _lastBaselineProjection;
        if (!HasCurrentResults || projection.Count == 0)
        {
            throw new InvalidOperationException(
                "Önce simülasyonu hesaplamalısın.");
        }

        var result = service.FindTargetReachability(
            projection,
            target);
        TargetResult = result switch
        {
            { IsAlreadyReached: true } =>
                "Bu seviyenin zaten üzerindesin.",
            { FirstReachedPeriod: { } reached } =>
                $"Bu planla {Money(target)} seviyesine ilk kez {TargetPeriodText(reached.Period)} döneminde ulaşıyorsun.",
            _ =>
                $"Bu planla {Money(target)} seviyesine 12 dönemlik görünüm içinde ulaşılamıyor."
        };
        HasTargetResult = true;
    }

    private void ClearTargetResult()
    {
        TargetResult = string.Empty;
        HasTargetResult = false;
    }
}
