using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Muted.App.Services;
using Muted.App.Views;
using Muted.Core.Settings;
using NAudio.Wave;

namespace Muted.Audio.Windows.Tests;

public sealed class FeatureIntegrationTests
{
    [Fact]
    public void DiagnosisWavHasCorrectFormatDurationAndSamples()
    {
        var samples = Enumerable.Range(0, 48_000).Select(i => .5f * MathF.Sin(i * .1f)).ToArray();
        using var reader = new WaveFileReader(new MemoryStream(LocalAudioPlayer.WaveBytes(samples)));
        Assert.Equal(48_000, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1), reader.TotalTime);
        var decoded = reader.ToSampleProvider();
        var actual = new float[samples.Length];
        Assert.Equal(samples.Length, decoded.Read(actual, 0, actual.Length));
        Assert.All(samples.Zip(actual), pair => Assert.InRange(Math.Abs(pair.First - pair.Second), 0, .0001f));
    }

    [Fact]
    public void FeaturesViewLoadsAndRendersAtNarrowAndWideLayouts()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Load app resources without running startup, opening hardware, or showing a window.
                var app = new Muted.App.App();
                app.InitializeComponent();
                using var theme = new ThemeService(app.Resources, System.Windows.Threading.Dispatcher.CurrentDispatcher);
                foreach (var mode in new[] { AppTheme.Dark, AppTheme.Light })
                {
                    theme.Apply(mode, false);
                    foreach (var width in new[] { 620, 860 })
                    {
                    var view = new FeaturesView();
                    view.Measure(new Size(width, double.PositiveInfinity));
                    view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
                    view.UpdateLayout();
                    Assert.InRange(view.ActualHeight, 500, 2000);
                    var bitmap = new RenderTargetBitmap(width, (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, $"features-{mode}-{width}.png"));
                    encoder.Save(stream);
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout did not finish.");
        Assert.Null(failure);
    }
}
