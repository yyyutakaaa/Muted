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
/// by the engine's frame peaks, and eases to a resting curve when Muted is stopped.
/// </summary>
/// <remarks>
/// This draws while audio flows, so it is kept cheap: thirty frames a second, one
/// closed <see cref="StreamGeometry"/> per frame instead of hundreds of bezier
/// segments, and brushes rebuilt only when their colour really changes. A stopped or
/// hidden waveform asks for no frames at all.
/// </remarks>
public sealed class LiveWaveform : FrameworkElement
{
    private const int PointCount = 64;
    private const double ActiveFrameMilliseconds = 1000d / 30d;
    private const double IdleFrameMilliseconds = 1000d / 12d;

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
    private readonly Point[] _outline = new Point[PointCount * 2];
    private readonly Point[] _outlineTail = new Point[(PointCount * 2) - 1];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastRenderMilliseconds;
    private bool _subscribed;
    private bool _settled;
    private Color _cachedColor;
    private Brush? _fill;
    private Pen? _glowPen;
    private Pen? _linePen;
    private Pen? _baselinePen;
    private bool _cachedActive;

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
        if (_subscribed || !IsVisible)
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
        var active = IsActive;

        // Stopped means still: the shape eases to its resting curve and then this
        // element stops asking for frames altogether until audio comes back.
        if (_settled && !active)
        {
            return;
        }

        var interval = active ? ActiveFrameMilliseconds : IdleFrameMilliseconds;
        var now = _clock.Elapsed.TotalMilliseconds;
        if (now - _lastRenderMilliseconds < interval)
        {
            return;
        }

        _lastRenderMilliseconds = now;
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
        var moved = false;

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
                // A fixed resting curve, so an idle Muted costs nothing to draw.
                target = 0.06 + (0.04 * Math.Sin(index * 0.42));
            }

            var next = _smoothed[index] + ((target - _smoothed[index]) * (active ? 0.45 : 0.18));
            if (Math.Abs(next - _smoothed[index]) > 0.0008)
            {
                moved = true;
            }

            _smoothed[index] = next;
        }

        _settled = !moved;

        var step = width / (PointCount - 1);
        for (var index = 0; index < PointCount; index++)
        {
            var x = index * step;
            var offset = _smoothed[index] * half;
            _outline[index] = new Point(x, centre - offset);
            _outline[^(index + 1)] = new Point(x, centre + offset);
        }

        Array.Copy(_outline, 1, _outlineTail, 0, _outlineTail.Length);

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(_outline[0], isFilled: true, isClosed: true);
            stream.PolyLineTo(_outlineTail, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();

        UpdateBrushes(active);
        context.DrawGeometry(_fill, _glowPen, geometry);
        context.DrawGeometry(null, _linePen, geometry);
        context.DrawLine(_baselinePen, new Point(0, centre), new Point(width, centre));
    }

    /// <summary>Rebuilds the pens only when the blended colour actually changes.</summary>
    private void UpdateBrushes(bool active)
    {
        var accent = (Stroke as SolidColorBrush)?.Color ?? Colors.MediumPurple;
        var secondary = (SecondaryStroke as SolidColorBrush)?.Color ?? Colors.MediumAquamarine;

        // Quantised, so ordinary speech does not rebuild four brushes every frame.
        var voice = active ? Math.Round(Math.Clamp(VoiceProbability, 0d, 1d) * 8d) / 8d : 0d;
        var blended = Color.FromRgb(
            (byte)(accent.R + ((secondary.R - accent.R) * voice)),
            (byte)(accent.G + ((secondary.G - accent.G) * voice)),
            (byte)(accent.B + ((secondary.B - accent.B) * voice)));

        if (_fill is not null && _cachedColor == blended && _cachedActive == active)
        {
            return;
        }

        _cachedColor = blended;
        _cachedActive = active;

        var fill = new LinearGradientBrush(
            Color.FromArgb(70, blended.R, blended.G, blended.B),
            Color.FromArgb(10, blended.R, blended.G, blended.B),
            new Point(0, 0),
            new Point(0, 1));
        fill.Freeze();
        _fill = fill;

        // Two passes stand in for a blur: cheap, and it survives software rendering.
        var glow = new Pen(new SolidColorBrush(Color.FromArgb(55, blended.R, blended.G, blended.B)), 6);
        glow.Freeze();
        _glowPen = glow;

        var line = new Pen(new SolidColorBrush(blended), 2.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        line.Freeze();
        _linePen = line;

        var baseline = new Pen(
            new SolidColorBrush(Color.FromArgb(active ? (byte)90 : (byte)45, blended.R, blended.G, blended.B)),
            1);
        baseline.Freeze();
        _baselinePen = baseline;
    }
}
