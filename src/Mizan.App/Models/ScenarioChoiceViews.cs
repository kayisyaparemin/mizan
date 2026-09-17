using CommunityToolkit.Mvvm.ComponentModel;
using Mizan.Application.Models;

namespace Mizan.App.Models;

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

/// <summary>Finansal Yapı ekleme alanında kayıt türü grubunun çipi.</summary>
public sealed partial class RecordEntryGroupView(RecordEntryGroup group, string label)
    : ObservableObject
{
    public RecordEntryGroup Group { get; } = group;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>Finansal Yapı ekleme alanında tek kayıt türü kartı.</summary>
public sealed partial class RecordEntryOptionView(RecordEntryOption option)
    : ObservableObject
{
    public RecordEntryOption Option { get; } = option;
    public string Title => Option.Title;
    public string Summary => Option.Summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Marker))]
    private bool isSelected;

    public string Marker => IsSelected ? "●" : "○";
}
