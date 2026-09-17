using CommunityToolkit.Mvvm.Input;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class SettingsViewModel
{
    public async Task RequestBackupAccessAsync()
    {
        await backup.RequestAccessAsync();
        await LoadBackupStateAsync();
    }

    public async Task BackUpNowAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var result = await backup.BackUpNowAsync();
            await LoadBackupStateAsync();
            if (result.Outcome == BackupOutcome.Created)
            {
                await feedback.ShowSuccessAsync(
                    $"Bütün profiller {backup.LocationDescription} klasörüne yedeklendi: {result.State!.FileName}",
                    title: "Yedeklendi");
            }
            else if (result.Outcome == BackupOutcome.NoAccess)
            {
                await feedback.ShowErrorAsync(
                    "Yedek klasörüne erişim izni yok. \"İzin Ver\" ile açabilirsin.",
                    title: "Yedeklenemedi");
            }
        }
        catch (Exception exception)
        {
            await feedback.ShowErrorAsync(
                UserFacingMessages.FromException(exception),
                title: "Yedeklenemedi");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadBackupStateAsync()
    {
        HasBackupAccess = backup.HasAccess;
        var state = await backup.GetLastBackupAsync();
        LastBackupText = state is null
            ? "Henüz yedek alınmadı."
            : $"Son yedek: {state.BackedUpAt.ToLocalTime().ToString("d MMMM yyyy HH:mm", TurkishCulture)} · {state.FileName}";
    }

    public void PrepareStrategyEditor()
    {
        if (!CanManageStrategy)
        {
            SetStatus("Önce gelirini ekleyip ilk gelir kullanım düzenini seçmelisin.");
            return;
        }

        HasPreview = false;
        SetStatus(string.Empty);
    }

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.Commitments);

    [RelayCommand]
    private async Task PreviewStrategyAsync()
    {
        try
        {
            var preview = await service.PreviewCashFlowAllocationStrategyAsync(
                SelectedStrategyMode?.Value ?? throw new InvalidOperationException("Yeni düzen seçilmelidir."),
                SelectedEffectiveSalary?.Value ?? throw new InvalidOperationException("Geçerli dönem tarihi seçilmelidir."));
            PreviewText = string.Join(Environment.NewLine,
                $"Başlangıç dönemi: {preview.EffectivePeriodDate:dd.MM.yyyy}",
                $"Mevcut düzen: {ModeText(preview.CurrentMode)}",
                $"Yeni düzen: {ModeText(preview.NewMode)}",
                $"Normal zorunlu ödemeler: {Money(preview.Baseline.MandatoryOutflow)}",
                $"Geçmiş düzenden kapanacak: {Money(preview.Scenario.TransitionCatchUpAmount)}",
                $"Yeni dönem için ayrılacak: {Money(preview.Scenario.ForwardFundedAmount)}",
                $"Toplam geçiş yükü: {Money(preview.TotalTransitionBurden)}",
                $"Dönem neti: {Money(preview.Scenario.EstimatedSurplus)}",
                $"Dönem sonu durumu: {Money(preview.Scenario.EndingProjectedBalance)}",
                preview.FinancingGap < 0m
                    ? $"Finansman açığı: {Money(preview.FinancingGap)}"
                    : "Finansman açığı oluşmuyor.");
            HasPreview = true;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            HasPreview = false;
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public async Task<bool> ApplyStrategyAsync()
    {
        try
        {
            var date = SelectedEffectiveSalary?.Value ??
                       throw new InvalidOperationException("Geçerli dönem tarihi seçilmelidir.");
            var mode = SelectedStrategyMode?.Value ??
                       throw new InvalidOperationException("Yeni düzen seçilmelidir.");
            await service.SaveCashFlowAllocationStrategyAsync(
                new CashFlowAllocationStrategy
                {
                    Id = _pendingStrategy?.Id ?? Guid.NewGuid(),
                    Mode = mode,
                    EffectiveFromPeriodDate = date,
                    Note = StrategyNote.Trim()
                });
            await LoadAsync();
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync("Gelir kullanım düzeni planlandı.");
            return true;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
            return false;
        }
    }

    public async Task<bool> DeletePendingStrategyAsync()
    {
        if (_pendingStrategy is null) return false;

        try
        {
            await service.DeleteCashFlowAllocationStrategyAsync(_pendingStrategy.Id);
            await LoadAsync();
            SetStatus(string.Empty);
            return true;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
            return false;
        }
    }

    public async Task<bool> ClearDevelopmentDataAsync()
    {
        if (!IsDevelopment)
        {
            SetStatus("Bu işlem yalnızca geliştirme sürümünde kullanılabilir.");
            return false;
        }

        try
        {
            await service.ClearDevelopmentDataAsync();
            await LoadAsync();
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync("Tüm veriler silindi.", title: "Tamamlandı");
            return true;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
            return false;
        }
    }

    public async Task<bool> LoadCanonicalSeedAsync()
    {
        if (!IsDevelopment)
        {
            SetStatus("Bu işlem yalnızca geliştirme sürümünde kullanılabilir.");
            return false;
        }

        try
        {
            await service.LoadCanonicalDevelopmentDataAsync();
            await LoadAsync();
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync("Test verisi yüklendi.", title: "Tamamlandı");
            return true;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
            return false;
        }
    }

    private static CashFlowAllocationMode Opposite(CashFlowAllocationMode mode) =>
        mode == CashFlowAllocationMode.PreviousPeriod
            ? CashFlowAllocationMode.UpcomingPeriod
            : CashFlowAllocationMode.PreviousPeriod;

    private static string ModeText(CashFlowAllocationMode mode) =>
        mode == CashFlowAllocationMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";
}
