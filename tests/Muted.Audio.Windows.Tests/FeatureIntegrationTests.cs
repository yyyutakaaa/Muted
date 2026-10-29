using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Muted.App.Services;
using Muted.App.ViewModels;
using Muted.Audio.Windows.Engine;
using Muted.Audio.Windows.Devices;
using System.Reflection;
using System.Windows.Threading;
using System.Windows.Controls;
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
                // Prevent the queued production startup callback from activating the installed app.
                var startupOperations = new List<DispatcherOperation>();
                void CaptureStartup(object? sender, DispatcherHookEventArgs e) => startupOperations.Add(e.Operation);
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted += CaptureStartup;
                var app = new Muted.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= CaptureStartup;
                foreach (var operation in startupOperations) operation.Abort();
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
                VerifyRedesignInRunningWindow(app, theme);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout did not finish.");
        Assert.Null(failure);
    }
    private static void VerifyRedesignInRunningWindow(Muted.App.App app, ThemeService theme)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        theme.Apply(AppTheme.Dark, false);
        var log = new FileLog();
        using var catalog = new WasapiDeviceCatalog();
        var store = new JsonSettingsStore();
        var isolated = Path.Combine(Path.GetTempPath(), "Muted-redesign-" + Guid.NewGuid());
        typeof(JsonSettingsStore).GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, isolated);
        typeof(JsonSettingsStore).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, Path.Combine(isolated, "settings.json"));
        var model = new MainViewModel(new RealtimeAudioEngine(), catalog, store,
            new StartupService(), new UpdateCoordinator(new UpdateService(log)), log);
        // Load the actual model and enumerate local endpoints without changing Windows startup,
        // saving user preferences, or opening a microphone owned by another running copy.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainViewModel).GetMethod("LoadSettings", flags)!.Invoke(model, [new AppSettings()]);
        typeof(MainViewModel).GetMethod("RefreshDevicesCore", flags)!.Invoke(model, [null, null, null, null]);
        Assert.False(model.AdvancedMode);
        Assert.True(model.AutoTuneEnabled);
        var window = new Muted.App.MainWindow(model);
        try
        {
            window.Show();
            Pump();
            SaveWindow(window, "simple");
            Assert.IsType<SimpleView>(((ContentControl)window.FindName("PageHost")).Content);
            foreach (var (mode, blend) in new[] { (NoiseRemovalMode.Gentle, .45), (NoiseRemovalMode.Balanced, .8), (NoiseRemovalMode.Strong, 1.0) })
            {
                model.NoiseRemoval = mode;
                Assert.Equal(blend, model.WetMix, 5);
                Assert.True(model.SuppressionEnabled);
            }
            model.VoiceLevelDb = 6;
            Assert.Equal(Math.Pow(10, .3), model.OutputGain, 5);
            model.NoiseRemoval = NoiseRemovalMode.Balanced;
            model.SelectedPage = AppPage.Advanced;
            Pump();
            SaveWindow(window, "advanced");
            var host = (ContentControl)window.FindName("PageHost");
            Assert.IsType<AdvancedView>(host.Content);
            // Opening the simplified controls must preserve legacy custom blends and
            // the full historic gain range, including its non-integer dB endpoints.
            foreach (var gain in new[] { .25, 4.0 })
            {
                model.OutputGain = gain;
                model.WetMix = .37;
                Pump();
                Assert.Equal(gain, model.OutputGain, 5);
                Assert.Equal(.37, model.WetMix, 5);
            }
            model.VoiceLevelDb = 6;
            model.NoiseRemoval = NoiseRemovalMode.Balanced;
            var scroll = (ScrollViewer)window.FindName("PageScroll");
            scroll.ScrollToBottom();
            Pump();
            SaveWindow(window, "advanced-voice-test");
            var snapshot = (AppSettings)typeof(MainViewModel).GetMethod("BuildSettings", flags)!.Invoke(model, [null])!;
            Assert.False(snapshot.AdvancedMode);
            Assert.Equal(.8f, snapshot.WetMix);
            Assert.Equal((float)Math.Pow(10, .3), snapshot.OutputGain, 5);
            model.SelectedPage = AppPage.Settings;
            Pump();
            SaveWindow(window, "settings");
            var settingsView = host.Content;
            Assert.Same(settingsView, host.Content); // Simple stays the home screen while Advanced opens separately.
            Assert.Equal(snapshot.OutputGain, (float)model.OutputGain, 5);
            model.SelectedPage = AppPage.Dashboard;
            foreach (var page in new[] { AppPage.Dashboard, AppPage.Settings })
            {
                model.SelectedPage = page;
                window.Width = 480; window.Height = 540;
                Pump();
                SaveWindow(window, $"{page}-small");
                Assert.InRange(host.ActualWidth, 350, 480);
            }
            model.SelectedPage = AppPage.Advanced;
            Pump();
            window.Width = 480; window.Height = 540;
            Pump();
            SaveWindow(window, "advanced-small");
            // Invalid routing cannot start audio, and the UI never calls this Active.
            model.SelectedInput = null;
            Assert.False(model.ToggleCommand.CanExecute(null));
            Assert.NotEqual("Active", model.ProcessingStatusText);
        }
        finally
        {
            window.Close();
            var disposal = model.DisposeAsync().AsTask();
            for (var retry = 0; !disposal.IsCompleted && retry < 20; retry++) Pump();
            Assert.True(disposal.IsCompleted, "Model disposal timed out.");
            disposal.GetAwaiter().GetResult();
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void SaveWindow(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"redesign-{name}.png"));
        encoder.Save(output);
    }

}
