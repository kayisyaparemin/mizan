using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mizan.App.Services;
using Mizan.App.Models;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Calculations;

namespace Mizan.App.ViewModels;

public partial class FutureMonthsViewModel(
    MizanService service,
    INavigationService navigation) : ViewModelBase
{
    public ObservableCollection<ProjectionLine> Periods { get; } = [];
    public ObservableCollection<LoanAdviceLine> LoanAdvice { get; } = [];
    [ObservableProperty] private bool hasLoanAdvice;
    private CancellationTokenSource? _loanAdviceCancellation;
    private bool _preserveOnNextAppearance;

    [ObservableProperty] private string targetAmount = string.Empty;
    [ObservableProperty] private string targetResult = string.Empty;
    [ObservableProperty] private bool hasTargetResult;
    [ObservableProperty] private bool hasProjection;
    [ObservableProperty] private bool hasNoProjection = true;
    [ObservableProperty] private string emptyStateMessage =
        "12 dönemlik planı oluşturmak için önce gelir bilgisi ekle.";
    [ObservableProperty] private string totalCreditCardInterest = "—";
    [ObservableProperty] private string totalDeficitInterest = "—";
    [ObservableProperty] private string totalInterestCost = "—";
    [ObservableProperty] private bool hasInterestSummary;

    /// <summary>
    /// 12 Dönem checkpoint planını gösterir ve kaymaz (I16). Ama mevcut
    /// dönemde sapma gözlendiyse bunu söylemek zorunda: kullanıcı yanlış
    /// olduğunu bildiği bir rakama bakıp ekranın susmasıyla karşılaşmamalı.
    /// Gözlem projeksiyona GİRMEZ, yalnız bu uyarıyı üretir.
    /// </summary>
    [ObservableProperty] private bool hasDeviationNotice;
    [ObservableProperty] private string deviationNoticeText = string.Empty;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var rows = await service.GetFuturePeriodsAsync(
                periodCount: 12);
            var plan = await service.GetFinancialPlanAsync();
            Periods.Clear();
            foreach (var row in rows)
            {
                var beforeSalaryCount = row.MandatoryItems.Count(x =>
                                            x.PaymentBeforePeriodStart) +
                                        row.CardPaymentStatuses.Count(x =>
                                            x.Payment is null &&
                                            x.PaymentBeforePeriodStart);
                Periods.Add(new ProjectionLine(
                    row,
                    PeriodTitle(row),
                    AssignmentText(row),
                    Money(row.AvailableAfterMandatory),
                    Money(-row.CarryOverDeficit),
                    row.HasCarryOverDeficit,
                    Money(row.TotalInterestGenerated),
                    row.TotalInterestGenerated > 0m,
                    Money(row.EndingProjectedBalance),
                    beforeSalaryCount == 0
                        ? string.Empty
                        : $"Dönem gelirinden önce vadesi gelen {beforeSalaryCount} ödeme",
                    beforeSalaryCount > 0,
                    row.IsEstimatedCardPayment,
                    row.HasUndeterminedCardPayment,
                    Money(row.TotalIncome),
                    Money(row.MandatoryOutflow),
                    Money(row.VariableExpenseAllowance)));
            }

            HasProjection = Periods.Count > 0;
            HasNoProjection = !HasProjection;
            var interest = ProjectionInterestSummary.From(rows);
            // Ana Sayfa ile aynı sebep: üç satır alt alta duruyor, tam sayıya
            // yuvarlanınca toplam gözle tutmuyor.
            TotalCreditCardInterest = Money(interest.CreditCardInterest, 2);
            TotalDeficitInterest = Money(
                interest.DeficitFinancingInterest, 2);
            TotalInterestCost = Money(interest.TotalInterestCost, 2);
            HasInterestSummary = interest.TotalInterestCost > 0m;
            await RefreshDeviationNoticeAsync();
            _ = RefreshLoanAdviceAsync();
            HasTargetResult = false;
            EmptyStateMessage = plan.Salaries.Count == 0
                ? "12 dönemlik planı oluşturmak için önce gelir bilgisi ekle."
                : "12 dönemlik plan için gelir kullanım düzenini seç.";
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.Commitments);

    [RelayCommand]
    private async Task OpenPeriodDetailAsync(ProjectionLine? line)
    {
        if (line is null)
        {
            return;
        }

        await navigation.NavigateToAsync(
            NavigationRoutes.SalaryPeriodDetail,
            new Dictionary<string, object>
            {
                [CashFlowPeriodDetailViewModel.DetailQueryKey] =
                    new SalaryPeriodDetailRequest(line.Projection)
            });
        _preserveOnNextAppearance = true;
    }

    public bool ConsumeDetailReturn()
    {
        if (!_preserveOnNextAppearance)
        {
            return false;
        }

        _preserveOnNextAppearance = false;
        return true;
    }

    [RelayCommand]
    private async Task FindTargetAsync()
    {
        try
        {
            var target = ParsePositiveMoney(TargetAmount, "Hedef tutar");
            var result = await service.FindTargetReachabilityAsync(target);
            TargetResult = result switch
            {
                { IsAlreadyReached: true } =>
                    "Bu seviyenin zaten üzerindesin.",
                { FirstReachedPeriod: { } reached } =>
                    $"Mevcut planla {Money(target)} seviyesine ilk kez {PeriodText(reached.Period)} döneminde ulaşıyorsun.",
                _ =>
                    $"Mevcut planla {Money(target)} seviyesine 12 dönemlik görünüm içinde ulaşılamıyor."
            };
            HasTargetResult = true;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            HasTargetResult = false;
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private static string PeriodText(CashFlowPeriod period) =>
        $"{period.Start.ToString("dd MMM", TurkishCulture)} → {period.End.ToString("dd MMM yyyy", TurkishCulture)}";

    [RelayCommand]
    private Task OpenCurrentPeriodAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.Dashboard);

    private async Task RefreshDeviationNoticeAsync()
    {
        var progress = await service.GetPeriodProgressAsync();
        if (progress?.EndingDeviation is not { } deviation || deviation == 0m)
        {
            HasDeviationNotice = false;
            DeviationNoticeText = string.Empty;
            return;
        }

        HasDeviationNotice = true;
        DeviationNoticeText =
            $"Bu projeksiyon {progress.PeriodStart.ToString("d MMMM", TurkishCulture)} checkpoint'ine dayanıyor. " +
            $"Mevcut dönemde {Money(deviation)} sapma gözlendi. " +
            $"Dönem {progress.PeriodEnd.ToString("d MMMM", TurkishCulture)} tarihinde kapandığında projeksiyon yenilenecek.";
    }

    /// <summary>
    /// Her kredi için ufuktaki taksit günlerini dener; birkaç düzine
    /// projeksiyon demek. Dönem listesi beklemesin diye arka planda çalışır,
    /// sayfa yeniden açılınca öncekini iptal eder.
    /// </summary>
    private async Task RefreshLoanAdviceAsync()
    {
        _loanAdviceCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loanAdviceCancellation = cancellation;
        try
        {
            var advice = await Task.Run(
                () => service.GetLoanPayoffAdviceAsync(cancellation.Token),
                cancellation.Token);
            LoanAdvice.Clear();
            foreach (var item in advice)
            {
                LoanAdvice.Add(ToLine(item));
            }

            HasLoanAdvice = LoanAdvice.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LoanAdvice.Clear();
            HasLoanAdvice = false;
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private static LoanAdviceLine ToLine(LoanPayoffAdvice advice)
    {
        var (headline, detail) = advice.Status switch
        {
            LoanPayoffAdviceStatus.Recommended => (
                $"{LongDate(advice.Date)} tarihinde {Money(advice.PayoffAmount ?? 0m)} ile kapatabilirsin",
                RecommendedDetail(advice)),
            LoanPayoffAdviceStatus.NoSafeMonth => (
                "12 dönem içinde açık oluşturmadan kapatılabilecek ay yok",
                "Kapatmak kredi faizinden kazandırırdı ama bir dönemde finansman açığına girmen ya da açığını büyütmen gerekiyor."),
            LoanPayoffAdviceStatus.NotWorthIt => (
                "12 dönem içinde kapatmak kazandırmıyor",
                "Kapatmak için gereken para finansman açığı faizine mal oluyor; bu, kredinin kendi faizinden pahalı."),
            LoanPayoffAdviceStatus.NeedsPrincipal => (
                "Kapatma hesabı için kalan anaparayı gir",
                "Finansal Yapı → Krediler'den krediyi düzenle; en güvenilirini bankanın kapatma tutarı."),
            _ => (
                $"{LongDate(advice.Date)} tarihinde erken kapama planlandı",
                "Finansal Yapı → Krediler'den geri alabilirsin.")
        };
        return new LoanAdviceLine(
            advice.LoanId,
            advice.LoanName,
            headline,
            detail,
            advice.Status == LoanPayoffAdviceStatus.Recommended,
            advice.Status == LoanPayoffAdviceStatus.NeedsPrincipal,
            advice.Date);
    }

    private static string RecommendedDetail(LoanPayoffAdvice advice)
    {
        var saving = advice.InterestSaving ?? 0m;
        var net = advice.NetGain ?? 0m;
        var detail =
            $"Kredinin ömrü boyunca {Money(saving)} faiz ödemezsin · hiçbir dönemde açık oluşmuyor";
        if (net < saving - 1m)
        {
            detail += $" · açık faizi düşülünce net {Money(net)}";
        }

        if (advice.BestDate is { } best && advice.BestNetGain is { } bestNet)
        {
            detail += $"\nEn kârlı gün: {LongDate(best)} (net {Money(bestNet)})";
        }

        return detail;
    }

    private static string LongDate(DateOnly? date) =>
        date?.ToString("d MMMM yyyy", TurkishCulture) ?? "—";

    [RelayCommand]
    private Task TryLoanClosureAsync(LoanAdviceLine? line) =>
        line is { IsRecommended: true, Date: { } date }
            ? navigation.NavigateToAsync(
                $"{NavigationRoutes.Simulation}?closeLoan={line.LoanId:D}&date={date:yyyy-MM-dd}")
            : Task.CompletedTask;

    [RelayCommand]
    private Task EditLoansAsync() =>
        navigation.NavigateToAsync($"{NavigationRoutes.Commitments}?section=payment");

    private static string PeriodTitle(CashFlowPeriodProjection row) =>
        $"{row.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Dönemi";

    private static string AssignmentText(CashFlowPeriodProjection row)
    {
        if (row.IsStrategyTransition)
        {
            return $"Düzen değişikliği dönemi • " +
                   $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
                   $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)}";
        }

        var action = row.CashFlowAllocationMode ==
                     Mizan.Domain.Models.CashFlowAllocationMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
               $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} {action}";
    }

}
