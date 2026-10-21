using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHouse.App.ViewModels;

namespace MiHouse.App.Views;

public sealed partial class DetailTemplateSelector : DataTemplateSelector
{
    public DataTemplate PropertyTemplate { get; set; } = null!;
    public DataTemplate ActionTemplate { get; set; } = null!;
    public DataTemplate HeaderTemplate { get; set; } = null!;
    protected override DataTemplate SelectTemplateCore(object item) => item switch
    { PropertyRowViewModel => PropertyTemplate, ActionServiceViewModel => ActionTemplate, _ => HeaderTemplate };
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
