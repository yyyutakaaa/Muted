using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Muted.Core.Dsp;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Muted.App.Controls;

/// <summary>
/// Draws the microphone signal itself instead of a decorative loop: the shape is fed
/// by the engine's frame peaks, and it falls back to a slow idle wave when stopped.
/// </summary>
public sealed class LiveWaveform : FrameworkElement
{
    private const int PointCount = 96;
    private const double FrameIntervalMilliseconds = 1000d / 45d;

    public static readonly DependencyProperty ScopeProperty = DependencyProperty.Register(
        nameof(Scope),
        typeof(WaveformScope),
        typeof(LiveWaveform),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(LiveWaveform),
        new PropertyMetadata(false));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(LiveWaveform),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryStrokeProperty = DependencyProperty.Register(
        nameof(SecondaryStroke),
        typeof(Brush),
        typeof(LiveWaveform),
        new FrameworkPropertyMetadata(Brushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VoiceProbabilityProperty = DependencyProperty.Register(
        nameof(VoiceProbability),
        typeof(double),
        typeof(LiveWaveform),
        new PropertyMetadata(0d));

    private readonly float[] _values = new float[PointCount];
    private readonly double[] _smoothed = new double[PointCount];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastRenderMilliseconds;
    private double _idlePhase;
    private bool _subscribed;

    public LiveWaveform()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();

        // Hidden in the tray means no frames at all, not frames nobody sees.
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true)
            {
                Subscribe();
            }
            else
            {
                Unsubscribe();
            }
        };
    }

    public WaveformScope? Scope
    {
        get => (WaveformScope?)GetValue(ScopeProperty);
        set => SetValue(ScopeProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush SecondaryStroke
    {
        get => (Brush)GetValue(SecondaryStrokeProperty);
        set => SetValue(SecondaryStrokeProperty, value);
    }

    public double VoiceProbability
    {
        get => (double)GetValue(VoiceProbabilityProperty);
        set => SetValue(VoiceProbabilityProperty, value);
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs eventArgs)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (now - _lastRenderMilliseconds < FrameIntervalMilliseconds)
        {
            return;
        }

        _lastRenderMilliseconds = now;
        _idlePhase += 0.06;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 4 || height <= 4)
        {
            return;
        }

        var scope = Scope;
        var active = IsActive && scope is not null;
        if (active)
        {
            scope!.CopyTo(_values);
        }

        var centre = height / 2d;
        var half = (height / 2d) - 4d;

        for (var index = 0; index < PointCount; index++)
        {
            double target;
            if (active)
            {
                // A touch of curvature at the edges keeps the shape from looking clipped.
                var window = Math.Sin(Math.PI * (index + 0.5) / PointCount);
                target = Math.Clamp(_values[index], 0f, 1f) * (0.35 + (0.65 * window));
                target = Math.Min(1d, Math.Pow(target, 0.7) * 1.15);
            }
            else
            {
                target = 0.06 + (0.04 * Math.Sin(_idlePhase + (index * 0.28)));
            }

            _smoothed[index] += (target - _smoothed[index]) * (active ? 0.45 : 0.12);
        }

        var top = BuildCurve(width, centre, half, upwards: true);
        var bottom = BuildCurve(width, centre, half, upwards: false);

        var accent = (Stroke as SolidColorBrush)?.Color ?? Colors.MediumPurple;
        var secondary = (SecondaryStroke as SolidColorBrush)?.Color ?? Colors.MediumAquamarine;
        var voice = Math.Clamp(VoiceProbability, 0d, 1d);
        var blended = active
            ? Color.FromRgb(
                (byte)(accent.R + ((secondary.R - accent.R) * voice)),
                (byte)(accent.G + ((secondary.G - accent.G) * voice)),
                (byte)(accent.B + ((secondary.B - accent.B) * voice)))
            : accent;

        var fill = new LinearGradientBrush(
            Color.FromArgb(70, blended.R, blended.G, blended.B),
            Color.FromArgb(10, blended.R, blended.G, blended.B),
            new Point(0, 0),
            new Point(0, 1));
        fill.Freeze();

        var body = new GeometryGroup { Children = { top, bottom } };
        context.DrawGeometry(fill, null, body);

        // Two passes stand in for a blur: cheap, and it survives software rendering.
        var glow = new Pen(new SolidColorBrush(Color.FromArgb(55, blended.R, blended.G, blended.B)), 6);
        glow.Freeze();
        var line = new Pen(new SolidColorBrush(blended), 2.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        line.Freeze();

        context.DrawGeometry(null, glow, top);
        context.DrawGeometry(null, glow, bottom);
        context.DrawGeometry(null, line, top);
        context.DrawGeometry(null, line, bottom);

        var baseline = new Pen(
            new SolidColorBrush(Color.FromArgb(active ? (byte)90 : (byte)45, blended.R, blended.G, blended.B)),
            1);
        baseline.Freeze();
        context.DrawLine(baseline, new Point(0, centre), new Point(width, centre));
    }

    private StreamGeometry BuildCurve(double width, double centre, double half, bool upwards)
    {
        var geometry = new StreamGeometry();
        var step = width / (PointCount - 1);
        var direction = upwards ? -1d : 1d;

        using (var stream = geometry.Open())
        {
            var start = new Point(0, centre + (direction * _smoothed[0] * half));
            stream.BeginFigure(start, false, false);

            for (var index = 1; index < PointCount; index++)
            {
                var previous = new Point((index - 1) * step, centre + (direction * _smoothed[index - 1] * half));
                var current = new Point(index * step, centre + (direction * _smoothed[index] * half));
                var control = new Point((previous.X + current.X) / 2d, previous.Y);
                var midpoint = new Point((previous.X + current.X) / 2d, (previous.Y + current.Y) / 2d);
                stream.QuadraticBezierTo(control, midpoint, true, false);
                stream.QuadraticBezierTo(
                    new Point((previous.X + current.X) / 2d, current.Y),
                    current,
                    true,
                    false);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
