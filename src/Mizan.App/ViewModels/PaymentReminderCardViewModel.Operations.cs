using CommunityToolkit.Mvvm.Input;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Application.Services;

namespace Mizan.App.ViewModels;

public sealed partial class PaymentReminderCardViewModel
{
    /// <summary>Ertelenen satıra dokunuldu: "Bu ödeme yapıldı mı?"</summary>
    [RelayCommand]
    private async Task ResolveSnoozedAsync(ReminderAnswerLine? line)
    {
        if (line is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var paid = await feedback.ConfirmAsync(
                "Bu ödeme yapıldı mı?",
                $"{line.What}\n{line.When}",
                "Evet, ödedim",
                "Henüz değil");
            if (!paid)
            {
                return;
            }

            var response = line.Response;
            await coordinator.AnswerAsync(
                PaymentReminderAnswerKind.Paid,
                [new PaymentDue(response.DueKey, response.Name, response.DueDate, response.Amount)]);
            await feedback.ShowSuccessAsync(
                Congratulations[Random.Shared.Next(Congratulations.Length)],
                "Tebrikler! 🎉",
                "Süper");
            Present(await coordinator.RefreshAsync());
            SetStatus(string.Empty);
            AnswersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Ödeme kaydedilemedi."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>"Ödediklerin" satırına dokunuldu: yanlışlıkla basıldıysa geri al.</summary>
    [RelayCommand]
    private async Task UndoPaidAsync(ReminderAnswerLine? line)
    {
        if (line is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var undo = await feedback.ConfirmAsync(
                "Ödendi işareti kaldırılsın mı?",
                $"{line.What}\nÖdeme yeniden kalan ödemelerine döner ve vadesinde hatırlatılır.",
                "Geri al",
                "Vazgeç");
            if (!undo)
            {
                return;
            }

            if (line.Response.DueDate <= DateOnly.FromDateTime(DateTime.Now))
            {
                await coordinator.AnswerAsync(
                    PaymentReminderAnswerKind.Snoozed,
                    [new PaymentDue(line.Response.DueKey, line.Response.Name, line.Response.DueDate, line.Response.Amount)]);
            }
            else
            {
                await coordinator.UndoAsync(line.Response.DueKey);
            }

            Present(await coordinator.RefreshAsync());
            SetStatus(string.Empty);
            AnswersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Ödendi işareti kaldırılamadı."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// "Deneme bildirimi gönder": sıradaki ödeme gününün bildirimi hemen düşer,
    /// düğmeleri denenebilir. Ödeme gününü beklemeden akışı görmek için.
    /// </summary>
    [RelayCommand]
    private async Task SendSampleAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetInfo(string.Empty);
            if (!scheduler.AreNotificationsAllowed &&
                !await scheduler.RequestPermissionAsync())
            {
                ShowPermissionWarning = true;
                return;
            }

            var sample = await coordinator.SendSampleAsync();
            SetInfo(sample is null
                ? "Hatırlatılacak ödeme yok; deneme bildirimi gönderilmedi."
                : "Deneme bildirimi gönderildi; bildirim çubuğuna bak. \"Ödedim\" gerçekten ödendi işaretler, Ödediklerin listesinden geri alabilirsin.");
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Deneme bildirimi gönderilemedi."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenNotificationSettings() =>
        scheduler.OpenNotificationSettings();

    private void OnNotificationAnswered() =>
        _ = HandleNotificationAnsweredAsync();

    private async Task HandleNotificationAnsweredAsync()
    {
        try
        {
            await LoadAsync();
            AnswersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Bildirim yanıtı işlenemedi."));
        }
    }
}
