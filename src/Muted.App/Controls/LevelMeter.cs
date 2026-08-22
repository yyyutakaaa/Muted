using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Muted.App.Controls;

/// <summary>
/// A dBFS level meter with peak hold, scale ticks and a clip indicator. A plain
/// progress bar hides everything that matters between -60 dB and -6 dB.
/// </summary>
/// <remarks>
/// Everything that does not change per frame is cached: the scale is a single
/// drawing, the gradient and pens are rebuilt only when their inputs change, and
/// the decay of the peak marker runs on a timer that stops once it is done.
/// </remarks>
public sealed class LevelMeter : FrameworkElement
{
    private const double FloorDecibels = -60d;
    private const double PeakHoldMilliseconds = 900d;
    private const double PeakDecayPerSecond = 0.55d;
    private const double ClipFlashMilliseconds = 1200d;

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level),
        typeof(double),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarningBrushProperty = DependencyProperty.Register(
        nameof(WarningBrush),
        typeof(Brush),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.Goldenrod, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DangerBrushProperty = DependencyProperty.Register(
        nameof(DangerBrush),
        typeof(Brush),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.IndianRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(
        nameof(TickBrush),
        typeof(Brush),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowScaleProperty = DependencyProperty.Register(
        nameof(ShowScale),
        typeof(bool),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly double[] TickDecibels = [-48, -36, -24, -18, -12, -6, -3];

    private readonly DispatcherTimer _decayTimer;
    private DrawingGroup? _scale;
    private double _scaleWidth;
    private double _scaleBarHeight;
    private Color _scaleColor;
    private Brush? _gradient;
    private double _gradientWidth;
    private Color _gradientFill;
    private Color _gradientWarning;
    private Color _gradientDanger;
    private Pen? _peakPen;
    private Color _peakPenColor;
    private double _peak;
    private long _peakTimestamp;
    private long _clipTimestamp;
    private long _lastRenderTimestamp = Environment.TickCount64;

    public LevelMeter()
    {
        // Only runs while the peak marker or the clip dot still has somewhere to go.
        _decayTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _decayTimer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not true)
            {
                _decayTimer.Stop();
            }
        };
        Unloaded += (_, _) => _decayTimer.Stop();
    }

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush WarningBrush
    {
        get => (Brush)GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    public Brush DangerBrush
    {
        get => (Brush)GetValue(DangerBrushProperty);
        set => SetValue(DangerBrushProperty, value);
    }

    public Brush TickBrush
    {
        get => (Brush)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public bool ShowScale
    {
        get => (bool)GetValue(ShowScaleProperty);
        set => SetValue(ShowScaleProperty, value);
    }

    /// <summary>Maps an amplitude to a 0-1 position on a dB scale.</summary>
    public static double Position(double amplitude)
    {
        if (amplitude <= 0.0005d)
        {
            return 0d;
        }

        var decibels = 20d * Math.Log10(Math.Clamp(amplitude, 0d, 1d));
        return Math.Clamp((decibels - FloorDecibels) / -FloorDecibels, 0d, 1d);
    }

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var now = Environment.TickCount64;
        var elapsedSeconds = Math.Clamp((now - _lastRenderTimestamp) / 1000d, 0d, 0.5d);
        _lastRenderTimestamp = now;

        var level = Math.Clamp(Level, 0d, 1d);
        if (level >= _peak)
        {
            _peak = level;
            _peakTimestamp = now;
        }
        else if (now - _peakTimestamp > PeakHoldMilliseconds)
        {
            _peak = Math.Max(level, _peak - (PeakDecayPerSecond * elapsedSeconds));
        }

        if (level >= 0.985d)
        {
            _clipTimestamp = now;
        }

        var barHeight = ShowScale ? Math.Max(6d, height - 12d) : height;
        var radius = Math.Min(4d, barHeight / 2d);
        var track = new Rect(0, 0, width, barHeight);
        context.DrawRoundedRectangle(TrackBrush, null, track, radius, radius);

        var filled = Position(level) * width;
        if (filled > 1)
        {
            context.PushClip(new RectangleGeometry(track, radius, radius));
            context.DrawRectangle(GetGradient(width), null, new Rect(0, 0, filled, barHeight));
            context.Pop();
        }

        if (ShowScale)
        {
            context.DrawDrawing(GetScale(width, barHeight));
        }

        if (_peak > 0.002d)
        {
            var peakColor = ColorOf(
                _peak >= 0.9d ? DangerBrush : _peak >= 0.7d ? WarningBrush : FillBrush,
                Colors.MediumAquamarine);
            var peakX = Math.Clamp(Position(_peak) * width, 1.5d, width - 1.5d);
            context.DrawLine(GetPeakPen(peakColor), new Point(peakX, 0), new Point(peakX, barHeight));
        }

        var clipping = now - _clipTimestamp < ClipFlashMilliseconds;
        if (clipping)
        {
            context.DrawEllipse(
                DangerBrush,
                null,
                new Point(width - (barHeight / 2d), barHeight / 2d),
                barHeight / 3d,
                barHeight / 3d);
        }

        // The level itself repaints on every change; this only covers the fade-out
        // after the signal stops moving, and stops as soon as there is nothing left.
        var needsDecay = _peak > level + 0.001d || clipping;
        if (needsDecay && IsVisible)
        {
            _decayTimer.Start();
        }
        else
        {
            _decayTimer.Stop();
        }
    }

    /// <summary>
    /// Absolute mapping across the full track, so the colour at a given position always
    /// means the same level no matter how far the bar is filled.
    /// </summary>
    private Brush GetGradient(double width)
    {
        var fill = ColorOf(FillBrush, Colors.MediumAquamarine);
        var warning = ColorOf(WarningBrush, Colors.Goldenrod);
        var danger = ColorOf(DangerBrush, Colors.IndianRed);

        if (_gradient is not null &&
            Math.Abs(_gradientWidth - width) < 0.5 &&
            _gradientFill == fill &&
            _gradientWarning == warning &&
            _gradientDanger == danger)
        {
            return _gradient;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(width, 0),
            MappingMode = BrushMappingMode.Absolute,
            GradientStops =
            [
                new GradientStop(fill, 0d),
                new GradientStop(fill, (-12d - FloorDecibels) / -FloorDecibels),
                new GradientStop(warning, (-6d - FloorDecibels) / -FloorDecibels),
                new GradientStop(danger, 1d)
            ]
        };
        brush.Freeze();

        _gradient = brush;
        _gradientWidth = width;
        _gradientFill = fill;
        _gradientWarning = warning;
        _gradientDanger = danger;
        return brush;
    }

    /// <summary>The ticks and their labels never move, so they are drawn once.</summary>
    /// <remarks>
    /// Keyed on the tick colour rather than the brush instance: the theme repaints the
    /// shared brushes in place, so the reference stays the same while the colour changes.
    /// The drawing gets its own frozen copy, because freezing anything that holds a
    /// shared brush would freeze that brush too and break every later theme switch.
    /// </remarks>
    private Drawing GetScale(double width, double barHeight)
    {
        var tickColor = ColorOf(TickBrush, Colors.Gray);
        if (_scale is not null &&
            Math.Abs(_scaleWidth - width) < 0.5 &&
            Math.Abs(_scaleBarHeight - barHeight) < 0.5 &&
            _scaleColor == tickColor)
        {
            return _scale;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        var brush = new SolidColorBrush(tickColor);
        brush.Freeze();
        var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dot };
        pen.Freeze();

        var drawing = new DrawingGroup();
        using (var scaleContext = drawing.Open())
        {
            foreach (var decibels in TickDecibels)
            {
                var x = Math.Round(((decibels - FloorDecibels) / -FloorDecibels) * width) + 0.5;
                scaleContext.DrawLine(pen, new Point(x, barHeight + 1), new Point(x, barHeight + 3));
                var label = new FormattedText(
                    decibels.ToString("0", CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    typeface,
                    8,
                    brush,
                    pixelsPerDip);
                scaleContext.DrawText(label, new Point(x - (label.Width / 2d), barHeight + 2));
            }
        }

        drawing.Freeze();
        _scale = drawing;
        _scaleWidth = width;
        _scaleBarHeight = barHeight;
        _scaleColor = tickColor;
        return drawing;
    }

    private Pen GetPeakPen(Color color)
    {
        if (_peakPen is not null && _peakPenColor == color)
        {
            return _peakPen;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, 2);
        pen.Freeze();
        _peakPen = pen;
        _peakPenColor = color;
        return pen;
    }

    private static Color ColorOf(Brush brush, Color fallback) =>
        (brush as SolidColorBrush)?.Color ?? fallback;
}
