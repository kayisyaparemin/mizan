using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;


public sealed class FinancialPlanQueryService(
    ICoinFlowStore store,
    IClock clock,
    FinancialProjectionService projectionService,
    SimulationCalculator simulationCalculator,
    TargetAmountCalculator targetAmountCalculator,
    SalaryPeriodCalculator salaryPeriodCalculator,
    ProjectionBoundaryResolver projectionBoundaryResolver,
    PaymentAssignmentStrategyResolver strategyResolver,
    FinancialSnapshotService snapshotService,
    HistoricalPlanRevisionService historicalPlanRevisionService,
    LoanPayoffAdvisor loanPayoffAdvisor) : IFinancialPlanQueryService
{
    public async Task<FinancialPlan> GetFinancialPlanAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadFinancialPlanCoreAsync(cancellationToken);
        await snapshotService.EnsureInitialSnapshotAsync(
            plan,
            cancellationToken);
        await historicalPlanRevisionService.CaptureOpenPlanRevisionAsync(
            plan,
            "Açık plan otomatik güncellendi",
            cancellationToken);
        return plan;
    }

    public async Task<FinancialPlan> LoadFinancialPlanCoreAsync(
        CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        var settingsTask = store.GetSettingsAsync(cancellationToken);
        var salariesTask = store.GetSalaryScheduleAsync(cancellationToken);
        var incomesTask = store.GetOtherIncomesAsync(cancellationToken);
        var loansTask = store.GetLoansAsync(cancellationToken);
        var prepaymentsTask = store.GetLoanPrepaymentsAsync(cancellationToken);
        var plansTask = store.GetPaymentPlansAsync(cancellationToken);
        var cardsTask = store.GetCreditCardsAsync(cancellationToken);
        var largeExpensesTask =
            store.GetPlannedLargeExpensesAsync(cancellationToken);
        var strategiesTask =
            store.GetPaymentAssignmentStrategiesAsync(cancellationToken);

        await Task.WhenAll(
            settingsTask,
            salariesTask,
            incomesTask,
            loansTask,
            prepaymentsTask,
            plansTask,
            cardsTask,
            largeExpensesTask,
            strategiesTask);

        return new FinancialPlan
        {
            Settings = await settingsTask,
            Salaries = await salariesTask,
            OtherIncomes = await incomesTask,
            Loans = await loansTask,
            LoanPrepayments = await prepaymentsTask,
            PaymentPlans = await plansTask,
            CreditCards = await cardsTask,
            PlannedLargeExpenses = await largeExpensesTask,
            PaymentAssignmentStrategies = await strategiesTask
        };
    }

    public async Task CapturePlanningChangeAsync(
        string trigger,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadFinancialPlanCoreAsync(cancellationToken);
        await snapshotService.EnsureInitialSnapshotAsync(
            plan,
            cancellationToken);
        await historicalPlanRevisionService.CaptureOpenPlanRevisionAsync(
            plan,
            trigger,
            cancellationToken);
    }

    public async Task<DashboardSnapshot?> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var query = await GetProjectionPlanAsync(date, cancellationToken);
        if (!CanBuildProjection(query.Plan))
        {
            return null;
        }

        return projectionService.BuildDashboard(
            query.Plan,
            date,
            query.Boundary?.FirstUnrealizedSalaryDate);
    }

    public async Task<IReadOnlyList<SalaryPeriodProjection>> GetFuturePeriodsAsync(
        DateOnly? asOf = null,
        int periodCount = 12,
        decimal? monthlyLivingBudgetOverride = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var query = await GetProjectionPlanAsync(date, cancellationToken);
        if (!CanBuildProjection(query.Plan))
        {
            return [];
        }

        return projectionService.BuildFuturePeriods(
            ApplyLivingBudgetOverride(
                query.Plan,
                monthlyLivingBudgetOverride),
            date,
            periodCount,
            query.Boundary?.FirstUnrealizedSalaryDate);
    }

    public async Task<IReadOnlyList<LoanPayoffAdvice>> GetLoanPayoffAdviceAsync(
        CancellationToken cancellationToken = default)
    {
        var date = clock.Today;
        var query = await GetProjectionPlanAsync(date, cancellationToken);
        return CanBuildProjection(query.Plan)
            ? loanPayoffAdvisor.Advise(
                query.Plan,
                date,
                query.Boundary?.FirstUnrealizedSalaryDate,
                cancellationToken: cancellationToken)
            : [];
    }

    public async Task<SalaryPeriodProjection?> FindTargetPeriodAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var periods = await GetFuturePeriodsAsync(
            asOf,
            12,
            cancellationToken: cancellationToken);
        return targetAmountCalculator.FindFirstReached(periods, targetAmount);
    }

    public async Task<TargetReachabilityResult> FindTargetReachabilityAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var periods = await GetFuturePeriodsAsync(
            asOf,
            12,
            cancellationToken: cancellationToken);
        return FindTargetReachability(periods, targetAmount);
    }

    public TargetReachabilityResult FindTargetReachability(
        IReadOnlyList<SalaryPeriodProjection> periods,
        decimal targetAmount) =>
        targetAmountCalculator.FindFirstReachable(periods, targetAmount);

    public async Task<PaymentAssignmentStrategyOverview>
        GetPaymentAssignmentStrategyOverviewAsync(
            CancellationToken cancellationToken = default)
    {
        var query = await GetProjectionPlanAsync(clock.Today, cancellationToken);
        var plan = query.Plan;
        var history = plan.PaymentAssignmentStrategies
            .OrderBy(x => x.EffectiveFromSalaryDate)
            .ThenBy(x => x.CreatedAt)
            .ToArray();
        var anchor = plan.Settings.ProjectionAnchorDate == default
            ? clock.Today
            : plan.Settings.ProjectionAnchorDate;
        var firstProjectionSalary =
            query.Boundary?.FirstUnrealizedSalaryDate ??
            salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
                anchor,
                plan.Settings.SalaryDay);
        var referenceSalary = salaryPeriodCalculator
            .GetPeriod(clock.Today, plan.Settings.SalaryDay)
            .Start;
        var current = history
            .Where(x => x.EffectiveFromSalaryDate <= referenceSalary)
            .LastOrDefault() ?? history.FirstOrDefault();
        var currentThreshold = current is null
            ? referenceSalary
            : DateOnly.FromDayNumber(Math.Max(
                referenceSalary.DayNumber,
                current.EffectiveFromSalaryDate.DayNumber));
        var pending = history.FirstOrDefault(x =>
            x.EffectiveFromSalaryDate > currentThreshold);
        var firstChoice = salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
            clock.Today,
            plan.Settings.SalaryDay);
        if (firstChoice <= clock.Today)
        {
            firstChoice = CalendarRules.AddMonthsKeepingDay(
                firstChoice,
                1,
                plan.Settings.SalaryDay);
        }
        if (firstChoice < firstProjectionSalary)
        {
            firstChoice = firstProjectionSalary;
        }

        var choices = Enumerable.Range(0, 12)
            .Select(index => CalendarRules.AddMonthsKeepingDay(
                firstChoice,
                index,
                plan.Settings.SalaryDay))
            .ToArray();
        return new PaymentAssignmentStrategyOverview(
            current,
            pending,
            history,
            choices);
    }

    public async Task<PaymentStrategyChangePreview>
        PreviewPaymentAssignmentStrategyAsync(
            PaymentAssignmentMode newMode,
            DateOnly effectiveSalaryDate,
            CancellationToken cancellationToken = default)
    {
        var query = await GetProjectionPlanAsync(clock.Today, cancellationToken);
        var plan = query.Plan;
        ValidateStrategyDate(plan, effectiveSalaryDate);
        var currentMode = ResolveModeBeforeChange(plan, effectiveSalaryDate);
        var request = CreateStrategySimulationRequest(
            newMode,
            effectiveSalaryDate,
            "Gelir kullanım düzeni önizlemesi");
        var firstSalary =
            query.Boundary?.FirstUnrealizedSalaryDate ??
            salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
                plan.Settings.ProjectionAnchorDate,
                plan.Settings.SalaryDay);
        var effectiveIndex = Math.Max(
            0,
            ((effectiveSalaryDate.Year - firstSalary.Year) * 12) +
            effectiveSalaryDate.Month - firstSalary.Month);
        var result = simulationCalculator.Calculate(
            plan,
            clock.Today,
            request,
            Math.Min(60, Math.Max(12, effectiveIndex + 1)),
            firstSalary);
        var row = result.Rows.Single(x =>
            x.Scenario.PeriodStart == effectiveSalaryDate);
        return new PaymentStrategyChangePreview(
            effectiveSalaryDate,
            currentMode,
            newMode,
            row.Baseline,
            row.Scenario);
    }

    public async Task<ProjectionQueryPlan> GetProjectionPlanAsync(
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        if (!CanBuildProjection(plan))
        {
            return new ProjectionQueryPlan(plan, null);
        }

        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var currentSnapshot = FinancialSnapshotService.LatestCurrent(history);
        if (currentSnapshot is null)
        {
            return new ProjectionQueryPlan(plan, null);
        }

        var boundary = projectionBoundaryResolver.Resolve(
            history,
            currentSnapshot,
            plan.Settings,
            asOf);
        return new ProjectionQueryPlan(
            ApplyProjectionBoundary(plan, boundary),
            boundary);
    }

    public static bool CanBuildProjection(FinancialPlan plan) =>
        plan.Salaries.Count > 0 &&
        plan.PaymentAssignmentStrategies.Count > 0 &&
        plan.Settings.ProjectionAnchorDate != default;

    public static FinancialPlan ApplyLivingBudgetOverride(
        FinancialPlan plan,
        decimal? monthlyLivingBudget) =>
        monthlyLivingBudget is not { } budget ||
        budget == plan.Settings.MonthlyLivingBudget
            ? plan
            : plan with
            {
                Settings = plan.Settings with
                {
                    MonthlyLivingBudget = budget
                }
            };

    private static FinancialPlan ApplyProjectionBoundary(
        FinancialPlan plan,
        ProjectionBoundary boundary) => plan with
    {
        Settings = plan.Settings with
        {
            ProjectionStartingSavings = boundary.StartingSavings,
            ProjectionAnchorDate = boundary.ProjectionAnchorDate
        }
    };

    private void ValidateStrategyDate(
        FinancialPlan plan,
        DateOnly effectiveSalaryDate)
    {
        if (!strategyResolver.IsSalaryDate(
                effectiveSalaryDate,
                plan.Settings.SalaryDay))
        {
            throw new InvalidOperationException(
                "Düzen değişikliği yalnızca bir dönem tarihinde başlayabilir.");
        }
    }

    private static PaymentAssignmentMode ResolveModeBeforeChange(
        FinancialPlan plan,
        DateOnly effectiveSalaryDate)
    {
        var previousSalary = CalendarRules.AddMonthsKeepingDay(
            effectiveSalaryDate,
            -1,
            plan.Settings.SalaryDay);
        return plan.PaymentAssignmentStrategies
            .Where(x => x.EffectiveFromSalaryDate <= previousSalary)
            .OrderBy(x => x.EffectiveFromSalaryDate)
            .LastOrDefault()?.Mode ??
               plan.PaymentAssignmentStrategies
                   .OrderBy(x => x.EffectiveFromSalaryDate)
                   .First().Mode;
    }

    private static SimulationRequest CreateStrategySimulationRequest(
        PaymentAssignmentMode mode,
        DateOnly effectiveSalaryDate,
        string note) => new(
            SimulationScenarioType.PaymentStrategyChange,
            note,
            0m,
            effectiveSalaryDate,
            NewPaymentAssignmentMode: mode,
            EffectiveSalaryDate: effectiveSalaryDate);
}
