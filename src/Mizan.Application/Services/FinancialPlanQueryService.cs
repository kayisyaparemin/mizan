using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;


public sealed class FinancialPlanQueryService(
    IMizanStore store,
    IClock clock,
    FinancialProjectionService projectionService,
    SimulationCalculator simulationCalculator,
    TargetAmountCalculator targetAmountCalculator,
    CashFlowPeriodCalculator CashFlowPeriodCalculator,
    ProjectionBoundaryResolver projectionBoundaryResolver,
    PaymentAllocationStrategyResolver allocationResolver,
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

    public async Task<IReadOnlyList<CashFlowPeriodProjection>> GetFuturePeriodsAsync(
        DateOnly? asOf = null,
        int periodCount = 12,
        decimal? MonthlyVariableExpenseAllowanceOverride = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var query = await GetProjectionPlanAsync(date, cancellationToken);
        if (!CanBuildProjection(query.Plan))
        {
            return [];
        }

        return projectionService.BuildFuturePeriods(
            ApplyVariableExpenseAllowanceOverride(
                query.Plan,
                MonthlyVariableExpenseAllowanceOverride),
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

    public async Task<CashFlowPeriodProjection?> FindTargetPeriodAsync(
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
        IReadOnlyList<CashFlowPeriodProjection> periods,
        decimal targetAmount) =>
        targetAmountCalculator.FindFirstReachable(periods, targetAmount);

    public async Task<CashFlowAllocationStrategyOverview>
        GetCashFlowAllocationStrategyOverviewAsync(
            CancellationToken cancellationToken = default)
    {
        var query = await GetProjectionPlanAsync(clock.Today, cancellationToken);
        var plan = query.Plan;
        var history = plan.PaymentAssignmentStrategies
            .OrderBy(x => x.EffectiveFromPeriodDate)
            .ThenBy(x => x.CreatedAt)
            .ToArray();
        var anchor = plan.Settings.ProjectionAnchorDate == default
            ? clock.Today
            : plan.Settings.ProjectionAnchorDate;
        var firstProjectionPeriod =
            query.Boundary?.FirstUnrealizedSalaryDate ??
            CashFlowPeriodCalculator.GetFirstPeriodStartOnOrAfter(
                anchor,
                plan.Settings.IncomeDay);
        var referenceSalary = CashFlowPeriodCalculator
            .GetPeriod(clock.Today, plan.Settings.IncomeDay)
            .Start;
        var current = history
            .Where(x => x.EffectiveFromPeriodDate <= referenceSalary)
            .LastOrDefault() ?? history.FirstOrDefault();
        var currentThreshold = current is null
            ? referenceSalary
            : DateOnly.FromDayNumber(Math.Max(
                referenceSalary.DayNumber,
                current.EffectiveFromPeriodDate.DayNumber));
        var pending = history.FirstOrDefault(x =>
            x.EffectiveFromPeriodDate > currentThreshold);
        var firstChoice = CashFlowPeriodCalculator.GetFirstPeriodStartOnOrAfter(
            clock.Today,
            plan.Settings.IncomeDay);
        if (firstChoice <= clock.Today)
        {
            firstChoice = CalendarRules.AddMonthsKeepingDay(
                firstChoice,
                1,
                plan.Settings.IncomeDay);
        }
        if (firstChoice < firstProjectionPeriod)
        {
            firstChoice = firstProjectionPeriod;
        }

        var choices = Enumerable.Range(0, 12)
            .Select(index => CalendarRules.AddMonthsKeepingDay(
                firstChoice,
                index,
                plan.Settings.IncomeDay))
            .ToArray();
        return new CashFlowAllocationStrategyOverview(
            current,
            pending,
            history,
            choices);
    }

    public async Task<PaymentAllocationChangePreview>
        PreviewCashFlowAllocationStrategyAsync(
            CashFlowAllocationMode newMode,
            DateOnly EffectivePeriodDate,
            CancellationToken cancellationToken = default)
    {
        var query = await GetProjectionPlanAsync(clock.Today, cancellationToken);
        var plan = query.Plan;
        ValidateStrategyDate(plan, EffectivePeriodDate);
        var currentMode = ResolveModeBeforeChange(plan, EffectivePeriodDate);
        var request = CreateStrategySimulationRequest(
            newMode,
            EffectivePeriodDate,
            "Gelir kullanım düzeni önizlemesi");
        var firstPeriodStart =
            query.Boundary?.FirstUnrealizedSalaryDate ??
            CashFlowPeriodCalculator.GetFirstPeriodStartOnOrAfter(
                plan.Settings.ProjectionAnchorDate,
                plan.Settings.IncomeDay);
        var effectiveIndex = Math.Max(
            0,
            ((EffectivePeriodDate.Year - firstPeriodStart.Year) * 12) +
            EffectivePeriodDate.Month - firstPeriodStart.Month);
        var result = simulationCalculator.Calculate(
            plan,
            clock.Today,
            request,
            Math.Min(60, Math.Max(12, effectiveIndex + 1)),
            firstPeriodStart);
        var row = result.Rows.Single(x =>
            x.Scenario.PeriodStart == EffectivePeriodDate);
        return new PaymentAllocationChangePreview(
            EffectivePeriodDate,
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

    public static FinancialPlan ApplyVariableExpenseAllowanceOverride(
        FinancialPlan plan,
        decimal? MonthlyVariableExpenseAllowance) =>
        MonthlyVariableExpenseAllowance is not { } budget ||
        budget == plan.Settings.MonthlyVariableExpenseAllowance
            ? plan
            : plan with
            {
                Settings = plan.Settings with
                {
                    MonthlyVariableExpenseAllowance = budget
                }
            };

    private static FinancialPlan ApplyProjectionBoundary(
        FinancialPlan plan,
        ProjectionBoundary boundary) => plan with
    {
        Settings = plan.Settings with
        {
            ProjectionOpeningBalance = boundary.StartingSavings,
            ProjectionAnchorDate = boundary.ProjectionAnchorDate
        }
    };

    private void ValidateStrategyDate(
        FinancialPlan plan,
        DateOnly EffectivePeriodDate)
    {
        if (!allocationResolver.IsPeriodStartDate(
                EffectivePeriodDate,
                plan.Settings.IncomeDay))
        {
            throw new InvalidOperationException(
                "Düzen değişikliği yalnızca bir dönem tarihinde başlayabilir.");
        }
    }

    private static CashFlowAllocationMode ResolveModeBeforeChange(
        FinancialPlan plan,
        DateOnly EffectivePeriodDate)
    {
        var previousSalary = CalendarRules.AddMonthsKeepingDay(
            EffectivePeriodDate,
            -1,
            plan.Settings.IncomeDay);
        return plan.PaymentAssignmentStrategies
            .Where(x => x.EffectiveFromPeriodDate <= previousSalary)
            .OrderBy(x => x.EffectiveFromPeriodDate)
            .LastOrDefault()?.Mode ??
               plan.PaymentAssignmentStrategies
                   .OrderBy(x => x.EffectiveFromPeriodDate)
                   .First().Mode;
    }

    private static SimulationRequest CreateStrategySimulationRequest(
        CashFlowAllocationMode mode,
        DateOnly EffectivePeriodDate,
        string note) => new(
            SimulationScenarioType.PaymentStrategyChange,
            note,
            0m,
            EffectivePeriodDate,
            NewCashFlowAllocationMode: mode,
            EffectivePeriodDate: EffectivePeriodDate);
}
