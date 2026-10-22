using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MiHouse.App.ViewModels;
using Windows.System;

namespace MiHouse.App.Views;

public sealed partial class GroupPowerSwitch : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(GroupPowerState), typeof(GroupPowerSwitch), new PropertyMetadata(GroupPowerState.Unknown, OnStateChanged));
    private double? _pressX;
    private bool _dragged;
    public GroupPowerSwitch()
    {
        InitializeComponent();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.35;
    }
    public GroupPowerState State { get => (GroupPowerState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public event EventHandler<bool>? PowerRequested;
    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((GroupPowerSwitch)sender).UpdateState();
    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateState();
    private void UpdateState()
    {
        if (Knob is null) { return; }
        KnobOffset.X = State == GroupPowerState.On ? 20 : State == GroupPowerState.Off ? 0 : 10;
        LitTrack.Width = State == GroupPowerState.Partial ? 24 : 48;
        LitTrack.HorizontalAlignment = HorizontalAlignment.Left;
        LitTrack.Opacity = State == GroupPowerState.On ? 1 : State == GroupPowerState.Partial ? 0.5 : 0;
        Knob.Fill = (Brush)Application.Current.Resources[State == GroupPowerState.On ? "AppOnAccentBrush" : "TextFillColorSecondaryBrush"];
        AutomationProperties.SetItemStatus(this, State switch { GroupPowerState.On => "全部开启", GroupPowerState.Off => "全部关闭", GroupPowerState.Partial => "部分开启", _ => "状态未知" });
        ToolTipService.SetToolTip(this, "向左拖动全部关闭，向右拖动全部开启；离线或不可控设备将跳过并提示");
    }
    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) { return; }
        _pressX = e.GetCurrentPoint(this).Position.X;
        _dragged = false;
        CapturePointer(e.Pointer);
        Focus(FocusState.Pointer);
        e.Handled = true;
    }
    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressX is not { } start) { return; }
        double x = e.GetCurrentPoint(this).Position.X;
        _dragged |= Math.Abs(x - start) > 4;
        if (_dragged) { KnobOffset.X = Math.Clamp(x - 14, 0, 20); }
        e.Handled = true;
    }
    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressX is not { } start) { return; }
        bool desired = _dragged ? e.GetCurrentPoint(this).Position.X >= start : State != GroupPowerState.On;
        _pressX = null;
        ReleasePointerCapture(e.Pointer);
        UpdateState();
        Request(desired);
        e.Handled = true;
    }
    private void OnCanceled(object sender, PointerRoutedEventArgs e) { _pressX = null; UpdateState(); }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Space or VirtualKey.Enter)
        { Request(e.Key == VirtualKey.Right || (e.Key is VirtualKey.Space or VirtualKey.Enter && State != GroupPowerState.On)); e.Handled = true; }
    }
    internal void Request(bool desired) { if (IsEnabled) { PowerRequested?.Invoke(this, desired); } }
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupSwitchPeer(this);
    private sealed class GroupSwitchPeer(GroupPowerSwitch owner) : FrameworkElementAutomationPeer(owner), IToggleProvider
    {
        protected override string GetClassNameCore() => nameof(GroupPowerSwitch);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.Toggle ? this : base.GetPatternCore(patternInterface);
        public ToggleState ToggleState => owner.State == GroupPowerState.On ? ToggleState.On : owner.State == GroupPowerState.Off ? ToggleState.Off : ToggleState.Indeterminate;
        public void Toggle() => owner.Request(owner.State != GroupPowerState.On);
    }
}
