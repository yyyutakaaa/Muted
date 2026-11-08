using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Muted.App.Controls;
using Muted.App.Services;
using Muted.Core.Settings;

namespace Muted.Audio.Windows.Tests;

/// <summary>Checks the info card; called from the one WPF test that owns the Application.</summary>
internal static class InfoTipTests
{
    private static void Save(FrameworkElement element, string name, int width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(stream);
    }

    public static void Verify(Muted.App.App app, ThemeService theme)
    {
        {
            foreach (var mode in new[] { AppTheme.Dark, AppTheme.Light })
            {
                theme.Apply(mode, false);
                var tip = new InfoTip
                {
                    Title = "Output gain",
                    What = "Makes your voice louder after the noise removal.",
                    Best = "Use it when people say you are too quiet.",
                    Expect = "The leftover noise gets louder by the same amount."
                };

                // The row it sits in, like in Fine-tune.
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
                row.Children.Add(new TextBlock { Text = "Output gain", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(tip);
                var frame = new Border { Child = row };
                frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                Save(frame, $"infotip-row-{mode}.png", 320);

                var card = Assert.IsType<ToolTip>(tip.ToolTip);
                card.PlacementTarget = tip;
                card.ApplyTemplate();
                var content = Assert.IsType<StackPanel>(card.Content);
                var texts = content.Children.OfType<TextBlock>().Select(block => block.Text).ToList();

                Assert.Contains("Output gain", texts);
                Assert.Contains("WHAT IT DOES", texts);
                Assert.Contains("BEST WHEN", texts);
                Assert.Contains("WHAT TO EXPECT", texts);
                Assert.Contains("Makes your voice louder after the noise removal.", texts);
                Assert.Contains("Use it when people say you are too quiet.", texts);
                Assert.Contains("The leftover noise gets louder by the same amount.", texts);

                // Draw the card on its own, the way it looks once the tooltip has faded in.
                card.Content = null;
                content.DataContext = tip; // it only inherited this from the tooltip it just left
                var surface = new Border { Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), MaxWidth = 320, Child = content };
                surface.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
                surface.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
                System.Windows.Documents.TextElement.SetForeground(surface, (Brush)app.Resources["TextBrush"]);
                var backdrop = new Border { Padding = new Thickness(16), Child = surface };
                backdrop.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                Save(backdrop, $"infotip-card-{mode}.png", 360);

                Assert.Contains("More about Output gain", System.Windows.Automation.AutomationProperties.GetName(tip));
                Assert.Contains("Best when:", System.Windows.Automation.AutomationProperties.GetHelpText(tip));
            }
        }
    }
}
