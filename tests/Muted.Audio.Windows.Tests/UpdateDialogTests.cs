using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Muted.App;
using Muted.App.Services;
using Muted.Core.Settings;

namespace Muted.Audio.Windows.Tests;

internal static class UpdateDialogTests
{
    private static readonly AvailableUpdate Update = new(
        new Version(0, 8, 0),
        new Version(0, 8, 1),
        "Muted-Setup-0.8.1.exe",
        "https://example.invalid/Muted-Setup-0.8.1.exe",
        1024,
        "https://example.invalid/Muted-Setup-0.8.1.exe.sha256",
        "https://example.invalid/releases/v0.8.1");

    /// <summary>
    /// Renders the update window in both themes and walks through a failed install.
    /// Only one WPF Application can exist per test process, so the feature render
    /// test calls this on its UI thread.
    /// </summary>
    internal static void Verify(ThemeService theme)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        foreach (var mode in new[] { AppTheme.Dark, AppTheme.Light })
        {
            theme.Apply(mode, false);
            Render(UpdateDialog.CreateUpdate(null, Update, _ => Task.FromResult(true)), $"update-{mode}");
            Render(UpdateDialog.CreateNotice(null, "You're up to date",
                "Muted 0.8.1 is up to date.", UpdateNoticeKind.UpToDate), $"notice-{mode}");
        }

        theme.Apply(AppTheme.Dark, false);
        var dialog = UpdateDialog.CreateUpdate(null, Update, async progress =>
        {
            progress.Report(0.5);
            await Task.Delay(50);
            return false;
        });
        dialog.Show();
        Pump();
        var primary = (Button)dialog.FindName("PrimaryButton");
        primary.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        // Wait for the install to report back instead of for a fixed time: on a busy build
        // machine the continuation can take far longer than the 50 ms the fake install waits.
        PumpUntil(() => dialog.Result == UpdateDialogResult.Failed);
        Assert.Equal(UpdateDialogResult.Failed, dialog.Result);
        Assert.Equal("Update failed", ((TextBlock)dialog.FindName("TitleText")).Text);
        Save(dialog, "failed-Dark");
        primary.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PumpUntil(() => !dialog.IsVisible);
        Assert.False(dialog.IsVisible);
    }

    private static void Render(UpdateDialog dialog, string name)
    {
        dialog.Show();
        Pump();
        Assert.InRange(dialog.ActualHeight, 150, 600);
        Save(dialog, name);
        dialog.Close();
    }

    private static void Save(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"update-dialog-{name}.png"));
        encoder.Save(output);
    }

    /// <summary>Runs the dispatcher until the condition holds, for at most 20 seconds.</summary>
    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!done() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
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
}
