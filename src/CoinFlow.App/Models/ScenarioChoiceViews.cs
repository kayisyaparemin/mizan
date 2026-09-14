using CommunityToolkit.Mvvm.ComponentModel;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Models;

/// <summary>Plan türü grubunun çipi. Seçili hâli ekranda ayrı stil alır.</summary>
public sealed partial class ScenarioGroupView(ScenarioGroup group, string label)
    : ObservableObject
{
    public ScenarioGroup Group { get; } = group;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>
/// Grup içindeki tek plan türü kartı. Açıklama seçimden önce görünür;
/// Picker'da tür ancak seçildikten sonra anlatılıyordu.
/// </summary>
public sealed partial class ScenarioOptionView(ScenarioOption option)
    : ObservableObject
{
    public ScenarioOption Option { get; } = option;
    public string Title => Option.Title;
    public string Summary => Option.Summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Marker))]
    private bool isSelected;

    public string Marker => IsSelected ? "●" : "○";
}
