using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MiHouse.App.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class DetailSectionHeader(string name, bool isAdvanced, ICommand? command)
{
    public string Name { get; } = name;
    public bool IsAdvanced { get; } = isAdvanced;
    public ICommand? Command { get; } = command;
}

public sealed partial class DevicesViewModel
{
    public ObservableCollection<object> DetailRows { get; } = [];
    public bool HasDetailStatus => IsDetailBusy || !string.IsNullOrWhiteSpace(DetailMessage) || !string.IsNullOrWhiteSpace(DetailError);
    [ObservableProperty]
    public partial bool IsAdvancedExpanded { get; set; }
    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedExpanded = !IsAdvancedExpanded;
    partial void OnIsAdvancedExpandedChanged(bool value) => RebuildDetailRows();
    private void OnDetailPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Rows must appear as soon as definitions are known, before values are read.
        if (Properties.Count == 0) { DetailRows.Clear(); return; }
        RebuildDetailRows();
    }
    private void RebuildDetailRows()
    {
        DetailRows.Clear();
        DetailRows.Add(new DetailSectionHeader("常规", false, null));
        AddSection(false);
        DetailRows.Add(new DetailSectionHeader(IsAdvancedExpanded ? "高级 · 收起" : "高级 · 展开", true, ToggleAdvancedCommand));
        if (IsAdvancedExpanded) { AddSection(true); }
    }
    private void AddSection(bool advanced)
    {
        foreach (var service in Properties.Where(row => row.IsAdvanced == advanced).GroupBy(row => row.Key.Siid))
        {
            bool first = true;
            foreach (PropertyRowViewModel row in service.OrderByDescending(row => row.IsEditable))
            { row.IsSectionStart = first; first = false; DetailRows.Add(row); }
        }
        foreach (ActionServiceViewModel service in ActionServices.Where(service => service.IsAdvanced == advanced)) { DetailRows.Add(service); }
    }
    partial void OnIsDetailBusyChanged(bool value) => OnPropertyChanged(nameof(HasDetailStatus));
}
