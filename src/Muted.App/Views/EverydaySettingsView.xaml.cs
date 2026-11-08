using System.Windows;
using RadioButton = System.Windows.Controls.RadioButton;
using Muted.App.ViewModels;

namespace Muted.App.Views;

/// <summary>
/// Everything about the app itself, laid out like Advanced: a menu on the left and one
/// section at a time on the right.
/// </summary>
public partial class EverydaySettingsView : System.Windows.Controls.UserControl
{
    // Reopening Settings returns to the section you last had open.
    private static string _lastSection = "DevicesNav";

    private readonly Dictionary<RadioButton, FrameworkElement> _sections;
    private bool _compactMenu;

    public EverydaySettingsView()
        : this(null)
    {
    }

    /// <param name="section">The menu entry to open, such as "AboutNav"; null reopens the last one.</param>
    public EverydaySettingsView(string? section)
    {
        InitializeComponent();
        _sections = new()
        {
            [DevicesNav] = DevicesSection,
            [StartupNav] = StartupSection,
            [IndicatorsNav] = IndicatorsSection,
            [AppearanceNav] = AppearanceSection,
            [UpdatesNav] = UpdatesSection,
            [HelpNav] = HelpSection,
            [AboutNav] = AboutSection
        };
        var wanted = section ?? _lastSection;
        (_sections.Keys.FirstOrDefault(item => item.Name == wanted) ?? DevicesNav).IsChecked = true;

        // The setup guide lives under Help; asking for it opens that section.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel { ShowSetupGuide: true })
            {
                HelpNav.IsChecked = true;
            }
        };
        SizeChanged += (_, _) => UpdateMenuWidth();
    }

    /// <summary>Makes the next Settings page open on this section, such as "DevicesNav".</summary>
    public static void OpenNext(string navName) => _lastSection = navName;

    // On a narrow window the menu folds down to its icons so the section keeps room.
    private void UpdateMenuWidth()
    {
        var compact = ActualWidth is > 0 and < 640;
        if (compact == _compactMenu)
        {
            return;
        }

        _compactMenu = compact;
        MenuColumn.Width = new GridLength(compact ? 64 : 190);
        MenuTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in _sections.Keys)
        {
            if (compact)
            {
                item.ToolTip ??= item.Content;
                item.Content = string.Empty;
            }
            else
            {
                item.Content = item.ToolTip;
                item.ToolTip = null;
            }
        }
    }

    private void Menu_OnChecked(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not RadioButton item || _sections is null)
        {
            return;
        }

        _lastSection = item.Name;
        foreach (var (navItem, section) in _sections)
        {
            section.Visibility = navItem == item ? Visibility.Visible : Visibility.Collapsed;
        }

        SectionScroll.ScrollToTop();
    }

    private void Diagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedPage = AppPage.Diagnostics;
        }
    }
}
