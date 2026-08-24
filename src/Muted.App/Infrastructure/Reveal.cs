using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Muted.App.Infrastructure;

/// <summary>
/// Fades and lifts an element into place whenever it becomes visible. Give the
/// elements on a page increasing delays and the page assembles itself instead of
/// appearing all at once.
/// </summary>
/// <remarks>
/// Keyed on visibility rather than Loaded: every page in this app is built up front
/// and only collapsed, so Loaded fires once, off screen, for all of them. Each
/// animation is a one-shot on opacity and a transform, which the compositor handles
/// and which stops on its own.
/// </remarks>
public static class Reveal
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(260));
    private static readonly Duration LiftDuration = new(TimeSpan.FromMilliseconds(420));
    private const double LiftDistance = 18;

    public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
        "Delay",
        typeof(double),
        typeof(Reveal),
        new PropertyMetadata(double.NaN, OnDelayChanged));

    public static void SetDelay(DependencyObject element, double value) =>
        element.SetValue(DelayProperty, value);

    public static double GetDelay(DependencyObject element) =>
        (double)element.GetValue(DelayProperty);

    private static void OnDelayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnVisibleChanged;
        if (double.IsNaN((double)args.NewValue))
        {
            return;
        }

        element.IsVisibleChanged += OnVisibleChanged;
        if (element.IsVisible)
        {
            Play(element);
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is FrameworkElement element && args.NewValue is true)
        {
            Play(element);
        }
    }

    private static void Play(FrameworkElement element)
    {
        var delay = GetDelay(element);
        if (double.IsNaN(delay))
        {
            return;
        }

        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        // Held at the start values until the delay runs out, so a staggered row of
        // cards does not flash into view before its turn.
        element.Opacity = 0;
        transform.Y = LiftDistance;

        var begin = TimeSpan.FromMilliseconds(Math.Max(0, delay));
        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, FadeDuration) { BeginTime = begin });
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(LiftDistance, 0, LiftDuration)
            {
                BeginTime = begin,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }
}
