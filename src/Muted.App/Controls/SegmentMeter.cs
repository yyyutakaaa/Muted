using System.Windows;
using System.Windows.Media;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Rect = System.Windows.Rect;

namespace Muted.App.Controls;

/// <summary>
/// A segmented dBFS meter: thin rounded ticks that light up in the accent up to the
/// current level. The scale runs from -60 dB to 0 dB, like <see cref="LevelMeter"/>,
/// so quiet speech still moves it.
/// </summary>
public sealed class SegmentMeter : FrameworkElement
{
    private const double FloorDecibels = -60d;

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DangerBrushProperty = DependencyProperty.Register(
        nameof(DangerBrush), typeof(Brush), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(Brushes.IndianRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentWidthProperty = DependencyProperty.Register(
        nameof(SegmentWidth), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(4d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentSpacingProperty = DependencyProperty.Register(
        nameof(SegmentSpacing), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(7d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush DangerBrush
    {
        get => (Brush)GetValue(DangerBrushProperty);
        set => SetValue(DangerBrushProperty, value);
    }

    public double SegmentWidth
    {
        get => (double)GetValue(SegmentWidthProperty);
        set => SetValue(SegmentWidthProperty, value);
    }

    public double SegmentSpacing
    {
        get => (double)GetValue(SegmentSpacingProperty);
        set => SetValue(SegmentSpacingProperty, value);
    }

    /// <summary>Maps a linear peak (0..1) to its place on the -60..0 dB scale.</summary>
    internal static double Position(double level)
    {
        if (level <= 0.001d)
        {
            return 0d;
        }

        var decibels = 20d * Math.Log10(Math.Clamp(level, 0d, 1d));
        return Math.Clamp((decibels - FloorDecibels) / -FloorDecibels, 0d, 1d);
    }

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var pitch = Math.Max(2d, SegmentSpacing);
        if (width < pitch || height < 2)
        {
            return;
        }

        var segment = Math.Clamp(SegmentWidth, 1d, pitch);
        var count = Math.Max(1, (int)((width + (pitch - segment)) / pitch));
        var lit = (int)Math.Round(Position(Level) * count);
        var radius = Math.Min(1.5d, segment / 2d);
        for (var index = 0; index < count; index++)
        {
            // The last two segments only light at the very top, where it clips.
            var brush = index >= lit
                ? TrackBrush
                : index >= count - 2 ? DangerBrush : FillBrush;
            context.DrawRoundedRectangle(brush, null, new Rect(index * pitch, 0, segment, height), radius, radius);
        }
    }
}
