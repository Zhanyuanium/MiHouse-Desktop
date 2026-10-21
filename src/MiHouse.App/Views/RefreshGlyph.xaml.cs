using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace MiHouse.App.Views;

public sealed partial class RefreshGlyph : UserControl
{
    private readonly UISettings _settings = new();
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(RefreshGlyph),
        new PropertyMetadata(false, (sender, _) => ((RefreshGlyph)sender).UpdateRotation()));
    public RefreshGlyph() => InitializeComponent();
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    private void UpdateRotation()
    {
        if (Glyph is null) { return; }
        var visual = ElementCompositionPreview.GetElementVisual(Glyph);
        visual.StopAnimation("RotationAngleInDegrees");
        visual.RotationAngleInDegrees = 0;
        if (!IsLoaded || !IsBusy || !_settings.AnimationsEnabled) { return; }
        visual.CenterPoint = new Vector3(8, 8, 0);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromSeconds(1);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0, 0);
        animation.InsertKeyFrame(1, 360, visual.Compositor.CreateLinearEasingFunction());
        visual.StartAnimation("RotationAngleInDegrees", animation);
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings.AnimationsEnabledChanged += OnAnimationsChanged;
        UpdateRotation();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _settings.AnimationsEnabledChanged -= OnAnimationsChanged;
        var visual = ElementCompositionPreview.GetElementVisual(Glyph);
        visual.StopAnimation("RotationAngleInDegrees");
        visual.RotationAngleInDegrees = 0;
    }
    private void OnAnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(UpdateRotation);
}
