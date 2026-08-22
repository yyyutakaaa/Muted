using System.Globalization;
using System.Windows;
using System.Windows.Media;

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
public sealed class LevelMeter : FrameworkElement
{
    private const double FloorDecibels = -60d;
    private const double PeakHoldMilliseconds = 900d;
    private const double PeakDecayPerSecond = 0.55d;

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

    private double _peak;
    private long _peakTimestamp;
    private long _clipTimestamp;
    private long _lastRenderTimestamp = Environment.TickCount64;

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
            context.DrawRectangle(BuildGradient(width), null, new Rect(0, 0, filled, barHeight));
            context.Pop();
        }

        if (ShowScale)
        {
            var tickPen = new Pen(TickBrush, 1) { DashStyle = DashStyles.Dot };
            tickPen.Freeze();
            var typeface = new Typeface("Segoe UI");
            foreach (var decibels in TickDecibels)
            {
                var x = Math.Round(((decibels - FloorDecibels) / -FloorDecibels) * width) + 0.5;
                context.DrawLine(tickPen, new Point(x, barHeight + 1), new Point(x, barHeight + 3));
                var label = new FormattedText(
                    decibels.ToString("0", CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    typeface,
                    8,
                    TickBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                context.DrawText(label, new Point(x - (label.Width / 2d), barHeight + 2));
            }
        }

        if (_peak > 0.002d)
        {
            var peakBrush = _peak >= 0.9d ? DangerBrush : _peak >= 0.7d ? WarningBrush : FillBrush;
            var peakX = Math.Clamp(Position(_peak) * width, 1.5d, width - 1.5d);
            var peakPen = new Pen(peakBrush, 2);
            peakPen.Freeze();
            context.DrawLine(peakPen, new Point(peakX, 0), new Point(peakX, barHeight));
        }

        if (now - _clipTimestamp < 1200)
        {
            context.DrawEllipse(
                DangerBrush,
                null,
                new Point(width - (barHeight / 2d), barHeight / 2d),
                barHeight / 3d,
                barHeight / 3d);
        }

        // Peak hold and clip both fade on their own, so keep repainting while they do.
        if (_peak > level || now - _clipTimestamp < 1200)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Render,
                new Action(InvalidateVisual));
        }
    }

    /// <summary>
    /// Absolute mapping across the full track, so the colour at a given position always
    /// means the same level no matter how far the bar is filled.
    /// </summary>
    private Brush BuildGradient(double width)
    {
        var fill = (FillBrush as SolidColorBrush)?.Color ?? Colors.MediumAquamarine;
        var warning = (WarningBrush as SolidColorBrush)?.Color ?? Colors.Goldenrod;
        var danger = (DangerBrush as SolidColorBrush)?.Color ?? Colors.IndianRed;

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
        return brush;
    }
}
