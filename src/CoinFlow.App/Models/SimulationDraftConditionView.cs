using CommunityToolkit.Mvvm.ComponentModel;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.Models;

/// <summary>
/// Simülasyon planındaki tek koşul. Kapalı koşul hesaba girmez ve plan
/// uygulanırken kaydedilmez.
/// </summary>
/// <remarks>
/// Bu tip bilinçli olarak <c>record</c> değil: <see cref="IsEnabled"/>
/// ekrandaki <c>Switch</c> ile iki yönlü bağlanıyor, bunun için
/// <c>INotifyPropertyChanged</c> gerekiyor. Kimlik de referansa döndü —
/// aynı özet metnini taşıyan iki koşulu artık listeden ayırt edebiliyoruz.
/// </remarks>
public sealed partial class SimulationDraftConditionView(
    Guid id,
    SimulationRequest request,
    string dateText,
    string typeText,
    string summaryText) : ObservableObject
{
    public Guid Id { get; } = id;
    public SimulationRequest Request { get; } = request;
    public string DateText { get; } = dateText;
    public string TypeText { get; } = typeText;
    public string SummaryText { get; } = summaryText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    private bool isEnabled = true;

    /// <summary>Kapalı koşul listede sönük durur; silinmiş gibi görünmemeli.</summary>
    public double RowOpacity => IsEnabled ? 1d : 0.45d;

    /// <summary>
    /// XAML'de değer çevirici yok; olumsuz hâli görünürlük bağlaması için
    /// burada açık bir özellik olarak duruyor.
    /// </summary>
    public bool IsDisabled => !IsEnabled;
}
