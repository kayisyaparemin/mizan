using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CommitmentsViewModel
{
    [RelayCommand]
    private void AddPlanPayment()
    {
        try
        {
            var parsed = RequirePositive(
                ParseMoney(PlanPaymentAmount, "Ödeme tutarı"),
                "Ödeme tutarı");
            PlanInstallments.Add(new DatedAmountLine(
                Guid.NewGuid(),
                DateOnly.FromDateTime(PlanPaymentDate),
                parsed));
            PlanPaymentAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void AddCardCharge()
    {
        try
        {
            var parsed = RequirePositive(
                ParseMoney(CardChargeAmount, "Kart harcaması tutarı"),
                "Kart harcaması tutarı");
            var id = Guid.NewGuid();
            CardFutureCharges.Add(new DatedAmountLine(
                id,
                DateOnly.FromDateTime(CardChargeDate),
                parsed,
                "Gelecek taksit"));
            _cardChargeDescriptions[id] = CardFutureCharges[^1].Description;
            CardChargeAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public void RemovePlanPayment(DatedAmountLine line) =>
        PlanInstallments.Remove(line);

    public void RemoveCardCharge(DatedAmountLine line)
    {
        CardFutureCharges.Remove(line);
        _cardChargeDescriptions.Remove(line.Id);
    }

    private TemporaryPaymentPlan BuildPlan()
    {
        if (PlanInstallments.Count == 0)
        {
            throw new InvalidOperationException("En az bir ödeme eklemelisin.");
        }

        var id = Guid.NewGuid();
        return new TemporaryPaymentPlan
        {
            Id = id,
            Name = RequireName(),
            Kind = PaymentPlanKind.Temporary,
            Installments = PlanInstallments
                .OrderBy(x => x.Date)
                .Select(x => new TemporaryPaymentInstallment
                {
                    Id = x.Id,
                    PlanId = id,
                    DueDate = x.Date,
                    Amount = x.Amount
                })
                .ToArray()
        };
    }
}
