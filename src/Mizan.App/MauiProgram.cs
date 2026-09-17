using Mizan.App.Backup;
using Mizan.App.Reminders;
using Mizan.App.Pages;
using Mizan.App.Services;
using Mizan.App.ViewModels;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Infrastructure.Imports;
using Mizan.Domain.Calculations;
using Mizan.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Mizan.App;

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
#if MIZAN_DEV_BUILD
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
            services => new ProfileScopedMizanStore(
                profileId => new SqliteMizanStore(
                    profileRepository.GetDatabasePath(profileId),
                    developmentFeaturesEnabled,
                    services.GetRequiredService<IClock>().Today)));
        builder.Services.AddSingleton<IMizanStore>(
            services => services.GetRequiredService<ProfileScopedMizanStore>());
        builder.Services.AddSingleton<ISettingsRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<ISalaryRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<IIncomeRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<ILoanRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<IPaymentPlanRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<ICreditCardRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<IExpenseRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<IObservationRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<ISimulationRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<IFinancialSnapshotRepository>(s => s.GetRequiredService<IMizanStore>());
        builder.Services.AddSingleton<IProfileStoreSwitch>(
            services => services.GetRequiredService<ProfileScopedMizanStore>());
        builder.Services.AddSingleton<ProfileService>();
        builder.Services.AddSingleton<ProfileNavigator>();
        // Yedek: bütün profiller tek dosyada, uygulamanın dışında — depolamanın
        // en üstündeki Mizan klasöründe. Uygulama kaldırılınca silinmez.
        builder.Services.AddSingleton<IProfileBackupArchive>(
            services => new ProfileBackupArchive(
                profileRepository,
                services.GetRequiredService<IClock>()));
        builder.Services.AddSingleton<IBackupStorage>(
            new FolderBackupStorage(
                AndroidStorageAccess.FolderPath,
                $"Dahili depolama › {AndroidStorageAccess.FolderName}",
                new AndroidStorageAccess()));
        builder.Services.AddSingleton(new BackupOptions(
            Path.Combine(FileSystem.CacheDirectory, "backup")));
        builder.Services.AddSingleton<BackupService>();
        builder.Services.AddSingleton<IBackupFilePicker, AndroidBackupFilePicker>();
        // Ödeme günü hatırlatıcısı: alarm + bildirim. Kurulan bildirimler telefon
        // yeniden başlayınca dosyadan geri kurulur.
        builder.Services.AddSingleton<IPaymentReminderScheduler, AndroidPaymentReminderScheduler>();
        builder.Services.AddSingleton<PaymentReminderCoordinator>();
        builder.Services.AddSingleton<CashFlowPeriodCalculator>();
        builder.Services.AddSingleton<PaymentAllocationStrategyResolver>();
        builder.Services.AddSingleton<CreditCardPaymentPreferenceResolver>();
        builder.Services.AddSingleton<CashFlowAllocationPlanner>();
        builder.Services.AddSingleton<IncomeResolver>();
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
#if MIZAN_DEV_BUILD
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
        builder.Services.AddSingleton<CashFlowPeriodDetailPresenter>();
        builder.Services.AddSingleton<SimulatorInsightService>();
        builder.Services.AddSingleton<SimulationCalculator>();
        builder.Services.AddSingleton<TargetAmountCalculator>();
        builder.Services.AddSingleton<CreditCardObligationService>();
        builder.Services.AddSingleton<IFinancialPlanQueryService, FinancialPlanQueryService>();
        builder.Services.AddSingleton<FinancialPlanQueryService>();
        builder.Services.AddSingleton<ISimulationWorkflowService, SimulationWorkflowService>();
        builder.Services.AddSingleton<SimulationWorkflowService>();
        builder.Services.AddSingleton<IPeriodWorkflowService, PeriodWorkflowService>();
        builder.Services.AddSingleton<PeriodWorkflowService>();
        builder.Services.AddSingleton<IObligationManagementService, ObligationManagementService>();
        builder.Services.AddSingleton(services => new MizanService(
            services.GetRequiredService<IMizanStore>(),
            services.GetRequiredService<IFinancialPlanQueryService>(),
            services.GetRequiredService<ISimulationWorkflowService>(),
            services.GetRequiredService<IPeriodWorkflowService>(),
            services.GetRequiredService<IObligationManagementService>(),
            services.GetRequiredService<HistoryQueryService>()));
        builder.Services.AddSingleton<IUserFeedbackService, UserFeedbackService>();
        builder.Services.AddSingleton<INavigationService, MauiNavigationService>();

        builder.Services.AddTransient<ProfileSelectionViewModel>();
        builder.Services.AddTransient<ProfileSelectionPage>();
        builder.Services.AddTransient<PaymentReminderCardViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<OnboardingViewModel>();
        builder.Services.AddTransient<CommitmentsViewModel>();
        builder.Services.AddTransient<CardControlViewModel>();
        builder.Services.AddTransient<FutureMonthsViewModel>();
        builder.Services.AddTransient<SimulationViewModel>();
        builder.Services.AddTransient<CashFlowPeriodDetailViewModel>();
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
        builder.Services.AddTransient<CashFlowPeriodDetailPage>();
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
