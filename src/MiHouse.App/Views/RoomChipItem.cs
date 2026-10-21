using CommunityToolkit.Mvvm.ComponentModel;
using MiHouse.App.ViewModels;

namespace MiHouse.App.Views;

// Navigation state is a plain observable item, not a collection's current item.
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class RoomChipItem(RoomGroup room) : ObservableObject
{
    private bool _isActive;
    public RoomGroup Room { get; } = room;
    public string Name => Room.Name;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value)) { OnPropertyChanged(nameof(SelectionStatus)); }
        }
    }
    public string SelectionStatus => IsActive ? "当前房间" : string.Empty;
}
