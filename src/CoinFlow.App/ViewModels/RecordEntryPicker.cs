using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;

namespace CoinFlow.App.ViewModels;

/// <summary>
/// Finansal Yapı'nın kayıt türü seçimi. <c>EntryTypePickerView</c>'u
/// simülatörün koşul formuyla aynı biçimde besler: grup çipleri ve seçili
/// grubun açıklamalı kartları.
/// </summary>
public sealed partial class RecordEntryPicker : ObservableObject
{
    public RecordEntryPicker()
    {
        foreach (var (group, label) in FinancialRecordEntryCatalog.Groups)
        {
            Groups.Add(new RecordEntryGroupView(group, label));
        }
    }

    public event Action<RecordEntryOption>? OptionSelected;

    public ObservableCollection<RecordEntryGroupView> Groups { get; } = [];
    public ObservableCollection<RecordEntryOptionView> VisibleOptions { get; } = [];

    public RecordEntryOption? Selected { get; private set; }

    [RelayCommand]
    private void SelectGroup(RecordEntryGroupView? group)
    {
        if (group is null || group.IsSelected)
        {
            return;
        }

        Select(FinancialRecordEntryCatalog.OptionsIn(group.Group)[0]);
    }

    [RelayCommand]
    private void SelectOption(RecordEntryOptionView? option)
    {
        if (option is not null && !option.IsSelected)
        {
            Select(option.Option);
        }
    }

    public void Select(RecordEntryOption option)
    {
        if (VisibleOptions.Count == 0 ||
            VisibleOptions[0].Option.Group != option.Group)
        {
            VisibleOptions.Clear();
            foreach (var item in FinancialRecordEntryCatalog.OptionsIn(option.Group))
            {
                VisibleOptions.Add(new RecordEntryOptionView(item));
            }
        }

        foreach (var group in Groups)
        {
            group.IsSelected = group.Group == option.Group;
        }

        foreach (var item in VisibleOptions)
        {
            item.IsSelected = item.Option.Key == option.Key;
        }

        Selected = option;
        OptionSelected?.Invoke(option);
    }
}
