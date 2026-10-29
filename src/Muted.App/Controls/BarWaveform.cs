using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Muted.Core.Dsp;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Muted.App.Controls;

/// <summary>
/// The dashboard waveform: thin vertical bars with a dimmer mirror under the centre
/// line. The bars are the engine's recent frame peaks, so the shape is the real
/// signal; the middle is painted in the accent and the edges fade to grey.
/// </summary>
/// <remarks>
/// Same budget as <see cref="LiveWaveform"/>: thirty frames a second while audio
/// runs, nothing at all once a stopped waveform has eased to its resting shape, and
/// one brush per bar that is rebuilt only when a colour changes.
/// </remarks>
public sealed class BarWaveform : FrameworkElement
{
    private const double ActiveFrameMilliseconds = 1000d / 30d;
    private const double IdleFrameMilliseconds = 1000d / 12d;

    public static readonly DependencyProperty ScopeProperty = DependencyProperty.Register(
        nameof(Scope), typeof(WaveformScope), typeof(BarWaveform), new PropertyMetadata(null));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(BarWaveform), new PropertyMetadata(false));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(BarWaveform),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IdleProperty = DependencyProperty.Register(
        nameof(Idle), typeof(Brush), typeof(BarWaveform),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(BarWaveform),
        new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarSpacingProperty = DependencyProperty.Register(
        nameof(BarSpacing), typeof(double), typeof(BarWaveform),
        new FrameworkPropertyMetadata(9d, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private float[] _values = [];
    private double[] _smoothed = [];
    private Brush[] _upper = [];
    private Brush[] _lower = [];
    private Color _cachedAccent;
    private Color _cachedIdle;
    private double _lastRenderMilliseconds;
    private bool _subscribed;
    private bool _settled;

    public BarWaveform()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
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

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public Brush Idle
    {
        get => (Brush)GetValue(IdleProperty);
        set => SetValue(IdleProperty, value);
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double BarSpacing
    {
        get => (double)GetValue(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    private void Subscribe()
    {
        if (_subscribed || !IsVisible)
        {
            return;
        }

        _subscribed = true;
        _settled = false;
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

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _settled = false;
    }

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var pitch = Math.Max(2d, BarSpacing);
        if (width <= pitch || height <= 8)
        {
            return;
        }

        var count = Math.Max(8, (int)(width / pitch));
        EnsureCapacity(count);

        var scope = Scope;
        var active = IsActive && scope is not null;
        if (active)
        {
            scope!.CopyTo(_values);
        }

        // The upper bars take two thirds of the height, the mirror the rest.
        var centre = height * 0.62;
        var upperRoom = centre - 2d;
        var lowerRoom = height - centre - 2d;
        var moved = false;

        for (var index = 0; index < count; index++)
        {
            // Bell-shaped weighting: the middle carries the signal, the edges stay calm.
            var position = (index + 0.5) / count;
            var bell = Math.Exp(-Math.Pow((position - 0.5) / 0.26, 2));
            double target;
            if (active)
            {
                var value = Math.Clamp(_values[index], 0f, 1f);
                target = Math.Min(1d, Math.Pow(value, 0.65) * (0.35 + (0.85 * bell)));
                target = Math.Max(target, 0.04);
            }
            else
            {
                // A flat resting signal never suggests that stopped audio is flowing.
                target = 0;
            }

            var next = _smoothed[index] + ((target - _smoothed[index]) * (active ? 0.5 : 0.2));
            if (Math.Abs(next - _smoothed[index]) > 0.0008)
            {
                moved = true;
            }

            _smoothed[index] = next;
        }

        _settled = !moved;
        UpdateBrushes(count);

        var barWidth = Math.Clamp(BarWidth, 1d, pitch - 1d);
        var offset = (width - (count * pitch) + (pitch - barWidth)) / 2d;
        var radius = barWidth / 2d;
        for (var index = 0; index < count; index++)
        {
            var x = offset + (index * pitch);
            var up = Math.Max(barWidth, _smoothed[index] * upperRoom);
            var down = Math.Max(barWidth, _smoothed[index] * lowerRoom * 0.75);
            context.DrawRoundedRectangle(_upper[index], null, new Rect(x, centre - up, barWidth, up), radius, radius);
            context.DrawRoundedRectangle(_lower[index], null, new Rect(x, centre + 3d, barWidth, down), radius, radius);
        }
    }

    private void EnsureCapacity(int count)
    {
        if (_values.Length == count)
        {
            return;
        }

        var previous = _smoothed;
        _values = new float[count];
        _smoothed = new double[count];
        for (var index = 0; index < count && previous.Length > 0; index++)
        {
            _smoothed[index] = previous[index * previous.Length / count];
        }

        _upper = new Brush[count];
        _lower = new Brush[count];
        _cachedAccent = default;
    }

    /// <summary>One brush per bar: accent in the middle, grey at the edges.</summary>
    private void UpdateBrushes(int count)
    {
        var accent = (Accent as SolidColorBrush)?.Color ?? Colors.MediumPurple;
        var idle = (Idle as SolidColorBrush)?.Color ?? Colors.DimGray;
        if (_upper.Length == count && _upper[0] is not null && accent == _cachedAccent && idle == _cachedIdle)
        {
            return;
        }

        _cachedAccent = accent;
        _cachedIdle = idle;
        for (var index = 0; index < count; index++)
        {
            var position = (index + 0.5) / count;
            var mix = Math.Clamp(1.25 * Math.Exp(-Math.Pow((position - 0.5) / 0.2, 2)), 0d, 1d);
            var color = Color.FromRgb(
                (byte)(idle.R + ((accent.R - idle.R) * mix)),
                (byte)(idle.G + ((accent.G - idle.G) * mix)),
                (byte)(idle.B + ((accent.B - idle.B) * mix)));

            var upper = new SolidColorBrush(color);
            upper.Freeze();
            _upper[index] = upper;

            var lower = new LinearGradientBrush(
                Color.FromArgb(110, color.R, color.G, color.B),
                Color.FromArgb(0, color.R, color.G, color.B),
                new Point(0, 0),
                new Point(0, 1));
            lower.Freeze();
            _lower[index] = lower;
        }
    }
}
