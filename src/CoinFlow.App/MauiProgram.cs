using CoinFlow.App.Pages;
using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Infrastructure.Imports;
using CoinFlow.Domain.Calculations;
using CoinFlow.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace CoinFlow.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Her profil açılışında yeniden kurulur; bkz. ProfileNavigator.
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddSingleton<IClock, SystemClock>();
#if COINFLOW_DEV_BUILD
        const bool developmentFeaturesEnabled = true;
#else
        const bool developmentFeaturesEnabled = false;
#endif
        // Profil = ayrı veritabanı dosyası. Servisler tek bir store görür;
        // o store çağrıları açık profilin veritabanına iletir.
        var profileRepository = new FileSystemProfileRepository(
            FileSystem.AppDataDirectory);
        builder.Services.AddSingleton<IProfileRepository>(profileRepository);
        builder.Services.AddSingleton(
            services => new ProfileScopedCoinFlowStore(
                profileId => new SqliteCoinFlowStore(
                    profileRepository.GetDatabasePath(profileId),
                    developmentFeaturesEnabled,
                    services.GetRequiredService<IClock>().Today)));
        builder.Services.AddSingleton<ICoinFlowStore>(
            services => services.GetRequiredService<ProfileScopedCoinFlowStore>());
        builder.Services.AddSingleton<IProfileStoreSwitch>(
            services => services.GetRequiredService<ProfileScopedCoinFlowStore>());
        builder.Services.AddSingleton<ProfileService>();
        builder.Services.AddSingleton<ProfileNavigator>();
        builder.Services.AddSingleton<SalaryPeriodCalculator>();
        builder.Services.AddSingleton<PaymentAssignmentStrategyResolver>();
        builder.Services.AddSingleton<CreditCardPaymentPreferenceResolver>();
        builder.Services.AddSingleton<SalaryFundingPlanner>();
        builder.Services.AddSingleton<SalaryResolver>();
        builder.Services.AddSingleton<IncomeProjectionCalculator>();
        builder.Services.AddSingleton<LoanScheduleCalculator>();
        builder.Services.AddSingleton<LoanAmortizationCalculator>();
        builder.Services.AddSingleton<LoanPaymentScheduleBuilder>();
        builder.Services.AddSingleton<InstallmentScheduleCalculator>();
        builder.Services.AddSingleton<ScheduledPaymentCalculator>();
        builder.Services.AddSingleton<CreditCardStatementCalculator>();
        builder.Services.AddSingleton<CreditCardActualPaymentReconciler>();
        builder.Services.AddSingleton<MandatoryPaymentCalculator>();
        builder.Services.AddSingleton<FinancialProjectionCalculator>();
        builder.Services.AddSingleton<FinancialProjectionService>();
        builder.Services.AddSingleton<PeriodPlanSnapshotService>();
        builder.Services.AddSingleton<FinancialSnapshotService>();
        builder.Services.AddSingleton<ProjectionBoundaryResolver>();
        builder.Services.AddSingleton<HistoricalPlanRevisionService>();
        builder.Services.AddSingleton<FinancialStateReconciliationService>();
        builder.Services.AddSingleton<FinancialInstrumentReconciliationService>();
        builder.Services.AddSingleton<PlanActualComparisonCalculator>();
        builder.Services.AddSingleton<PeriodReviewService>();
        builder.Services.AddSingleton<PeriodProgressService>();
        builder.Services.AddSingleton<HistoryQueryService>();
        builder.Services.AddSingleton<LoanPayoffService>();
        builder.Services.AddSingleton<LoanPayoffAdvisor>();
        builder.Services.AddSingleton<IPdfTextExtractor, PdfPigPdfTextExtractor>();
        builder.Services.AddSingleton<ICreditCardStatementParser, AkbankAxessStatementParser>();
        builder.Services.AddSingleton<ICreditCardStatementParser, GarantiBonusStatementParser>();
        builder.Services.AddSingleton<ICreditCardStatementPdfPicker, CreditCardStatementPdfPicker>();
#if COINFLOW_DEV_BUILD
        builder.Services.AddSingleton<ICreditCardStatementImportDiagnostics,
            DevelopmentCreditCardStatementImportDiagnostics>();
#else
        builder.Services.AddSingleton<ICreditCardStatementImportDiagnostics,
            NullCreditCardStatementImportDiagnostics>();
#endif
        builder.Services.AddSingleton<ICreditCardStatementImporter, CreditCardStatementImporter>();
        builder.Services.AddSingleton(
            CreditCardStatementImportOptions.Default);
        builder.Services.AddSingleton<CreditCardStatementImportWorkflow>();
        builder.Services.AddSingleton<SalaryPeriodDetailPresenter>();
        builder.Services.AddSingleton<SimulatorInsightService>();
        builder.Services.AddSingleton<SimulationCalculator>();
        builder.Services.AddSingleton<TargetAmountCalculator>();
        builder.Services.AddSingleton<CoinFlowService>();
        builder.Services.AddSingleton<IUserFeedbackService, UserFeedbackService>();

        builder.Services.AddTransient<ProfileSelectionViewModel>();
        builder.Services.AddTransient<ProfileSelectionPage>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<OnboardingViewModel>();
        builder.Services.AddTransient<CommitmentsViewModel>();
        builder.Services.AddTransient<CardControlViewModel>();
        builder.Services.AddTransient<FutureMonthsViewModel>();
        builder.Services.AddTransient<SimulationViewModel>();
        builder.Services.AddTransient<SalaryPeriodDetailViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<PeriodReviewWizardViewModel>();
        builder.Services.AddTransient<HistoryViewModel>();
        builder.Services.AddTransient<HistoryDetailViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<CommitmentsPage>();
        builder.Services.AddTransient<CardControlPage>();
        builder.Services.AddTransient<FutureMonthsPage>();
        builder.Services.AddTransient<SimulationPage>();
        builder.Services.AddTransient<SalaryPeriodDetailPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<PeriodReviewPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<HistoryDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        Controls.SignedNumericEntry.Configure();
        return builder.Build();
    }
}
