using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Muted.App.Services;
using Muted.App.Views;
using Muted.Core.Settings;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// The three dots next to the profile picker draw with an icon font. The menu that opens
/// from them must still be readable text, not the empty boxes that font shows for letters.
/// Called from the one WPF test that owns the Application.
/// </summary>
internal static class ProfileMenuTests
{
    public static void Verify(Muted.App.App app, ThemeService theme)
    {
        theme.Apply(AppTheme.Dark, false);
        var panel = new ProfilesView();
        var window = new Window
        {
            Content = panel,
            Left = -20_000,
            Top = 0,
            Width = 600,
            Height = 900,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };

        try
        {
            window.Show();
            panel.UpdateLayout();

            var button = Assert.IsType<Button>(panel.FindName("ProfileMenuButton"));
            var glyph = Assert.IsType<TextBlock>(button.Content);
            Assert.Contains("MDL2", glyph.FontFamily.Source);
            Assert.DoesNotContain("MDL2", button.FontFamily.Source);

            var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
            menu.PlacementTarget = button;
            menu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.NotEmpty(items);
            Assert.All(items, item =>
            {
                Assert.DoesNotContain("MDL2", item.FontFamily.Source);
                Assert.Contains("Segoe UI", item.FontFamily.Source);
            });

            // Even a button drawn entirely in the icon font cannot pass it on to a menu.
            var iconButton = new Button { FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), Content = "x" };
            var iconMenu = new ContextMenu();
            iconMenu.Items.Add(new MenuItem { Header = "Apply again" });
            iconButton.ContextMenu = iconMenu;
            iconMenu.PlacementTarget = iconButton;
            window.Content = iconButton;
            window.UpdateLayout();
            iconMenu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.Contains("Segoe UI", ((MenuItem)iconMenu.Items[0]).FontFamily.Source);
            iconMenu.IsOpen = false;
            menu.IsOpen = false;
        }
        finally
        {
            window.Close();
        }
    }
}
