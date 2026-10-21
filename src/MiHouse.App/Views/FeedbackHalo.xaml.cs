using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace MiHouse.App.Views;

public sealed partial class FeedbackHalo : UserControl
{
    private const float _steadyOpacity = 0.13f;
    private readonly UISettings _settings = new();
    private bool _animateChanges = true;
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(FeedbackHalo),
        new PropertyMetadata(0d, (sender, _) => { var halo = (FeedbackHalo)sender; halo.ApplyLevel(halo._animateChanges && halo.IsLoaded); }));
    public static readonly DependencyProperty BurstTokenProperty = DependencyProperty.Register(nameof(BurstToken), typeof(long), typeof(FeedbackHalo),
        new PropertyMetadata(0L, (sender, _) => { var halo = (FeedbackHalo)sender; if (halo.IsLoaded) { halo.Burst(); } }));
    public FeedbackHalo()
    {
        InitializeComponent();
        ElementCompositionPreview.GetElementVisual(Tint).Opacity = 0;
    }
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public long BurstToken { get => (long)GetValue(BurstTokenProperty); set => SetValue(BurstTokenProperty, value); }
    public void SetLevel(double level, bool animate)
    {
        _animateChanges = animate;
        try { if (Level == level) { ApplyLevel(animate && IsLoaded); } else { Level = level; } }
        finally { _animateChanges = true; }
    }
    private void ApplyLevel(bool animate)
    {
        if (Tint is null) { return; }
        var visual = ElementCompositionPreview.GetElementVisual(Tint);
        float target = (float)Math.Clamp(Level, 0, 1) * _steadyOpacity;
        visual.StopAnimation("Opacity");
        if (!animate || !_settings.AnimationsEnabled) { visual.Opacity = target; return; }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(120);
        animation.InsertKeyFrame(1, target, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0, 0), new Vector2(0.2f, 1)));
        visual.StartAnimation("Opacity", animation);
    }
    private void Burst()
    {
        if (!_settings.AnimationsEnabled) { ApplyLevel(false); return; }
        var visual = ElementCompositionPreview.GetElementVisual(Tint);
        visual.StopAnimation("Opacity");
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(1460);
        animation.InsertKeyFrame(0, visual.Opacity);
        animation.InsertKeyFrame(60f / 1460, 0.30f);
        animation.InsertKeyFrame(1, (float)Math.Clamp(Level, 0, 1) * _steadyOpacity,
            visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.8f), new Vector2(0.2f, 1)));
        visual.StartAnimation("Opacity", animation);
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) { _settings.AnimationsEnabledChanged += OnAnimationsChanged; }
        ApplyLevel(false);
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) { _settings.AnimationsEnabledChanged -= OnAnimationsChanged; }
        var visual = ElementCompositionPreview.GetElementVisual(Tint);
        visual.StopAnimation("Opacity");
        visual.Opacity = 0;
    }
    private void OnAnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (IsLoaded) { ApplyLevel(false); } });
}
