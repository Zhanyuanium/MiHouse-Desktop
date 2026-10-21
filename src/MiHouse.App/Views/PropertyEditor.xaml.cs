using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using MiHouse.App.ViewModels;
using MiHouse.Core.Spec;
using Windows.System;

namespace MiHouse.App.Views;

public sealed partial class PropertyEditor : UserControl
{
    public PropertyEditor() => InitializeComponent();
    private static bool IsArmed(FrameworkElement editor, out PropertyRowViewModel row)
    {
        row = editor.DataContext as PropertyRowViewModel ?? null!;
        return editor.IsLoaded && row is { CanEdit: true, IsDetailActive: true } && ReferenceEquals(editor.Tag, row);
    }
    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        var editor = (ToggleSwitch)sender;
        if (IsArmed(editor, out var row) && row.BoolValue != editor.IsOn)
        { row.BoolValue = editor.IsOn; await CommitAsync(row); }
    }
    private async void OnChoice(object sender, SelectionChangedEventArgs e)
    {
        var editor = (ComboBox)sender;
        if (IsArmed(editor, out var row) && editor.SelectedItem is SpecValueList option && !Equals(row.SelectedOption, option))
        { row.SelectedOption = option; await CommitAsync(row); }
    }
    private void OnRange(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (IsArmed((FrameworkElement)sender, out var row) && row.NumericValue != e.NewValue) { row.NumericValue = e.NewValue; }
    }
    private async void OnRangeReleased(object sender, PointerRoutedEventArgs e) { if (IsArmed((FrameworkElement)sender, out var row)) { await CommitAsync(row); } }
    private async void OnRangeKeyUp(object sender, KeyRoutedEventArgs e) { if (IsArmed((FrameworkElement)sender, out var row)) { await CommitAsync(row); } }
    private async void OnRangeLostFocus(object sender, RoutedEventArgs e) { if (IsArmed((FrameworkElement)sender, out var row)) { await CommitAsync(row); } }
    private async void OnTextCommit(object sender, RoutedEventArgs e)
    {
        var editor = (TextBox)sender;
        if (IsArmed(editor, out var row) && editor.Text != row.EditorText) { await row.CommitTextAsync(editor.Text); }
    }
    private void OnTextKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { OnTextCommit(sender, e); e.Handled = true; }
    }
    private static async Task CommitAsync(PropertyRowViewModel row)
    {
        if (row.HasPendingChanges && row.ApplyCommand.CanExecute(null)) { await row.ApplyCommand.ExecuteAsync(null); }
    }
    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        var editor = (FrameworkElement)sender;
        if (editor is Slider slider)
        {
            slider.RemoveHandler(PointerReleasedEvent, new PointerEventHandler(OnRangeReleased));
            slider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnRangeReleased), true);
        }
        Prepare(editor);
    }
    private void OnEditorUnloaded(object sender, RoutedEventArgs e) => ((FrameworkElement)sender).Tag = null;
    private void OnEditorContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => Prepare(sender);
    private void Prepare(FrameworkElement editor)
    {
        editor.Tag = null;
        object context = editor.DataContext;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        { if (editor.IsLoaded && ReferenceEquals(editor.DataContext, context)) { editor.Tag = context; } });
    }
}
