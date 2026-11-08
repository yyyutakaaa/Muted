using System.Windows;
using Grid = System.Windows.Controls.Grid;
using Border = System.Windows.Controls.Border;
using RadioButton = System.Windows.Controls.RadioButton;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Muted.App.Views;

public partial class AdvancedView : System.Windows.Controls.UserControl
{
    // Reopening Advanced returns to the section you last had open.
    private static string _lastSection = "SoundNav";

    private readonly Dictionary<RadioButton, FrameworkElement> _sections;

    public AdvancedView()
        : this(null)
    {
    }

    /// <param name="section">The menu entry to open, such as "ChainNav"; null reopens the last one.</param>
    public AdvancedView(string? section)
    {
        InitializeComponent();
        _sections = new()
        {
            [SoundNav] = SoundSection,
            [VoiceNav] = VoiceSection,
            [ChainNav] = ChainSection,
            [TestNav] = TestSection,
            [ProfilesNav] = ProfilesSection,
            [HearNav] = HearSection,
            [ShortcutsNav] = ShortcutsSection
        };
        var wanted = section ?? _lastSection;
        var start = _sections.Keys.FirstOrDefault(item => item.Name == wanted) ?? SoundNav;
        start.IsChecked = true;

        SizeChanged += (_, _) => UpdateMenuWidth();
        Loaded += (_, _) =>
        {
            foreach (var track in new[] { NoiseTrack, ModelTrack })
            {
                MoveIndicator(track, animate: false);
            }
        };
    }

    // On a narrow screen the menu folds down to its icons so the section keeps room.
    private bool _compactMenu;
    private void UpdateMenuWidth()
    {
        var compact = ActualWidth is > 0 and < 640;
        if (compact == _compactMenu)
        {
            return;
        }

        _compactMenu = compact;
        MenuColumn.Width = new GridLength(compact ? 64 : 190);
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

        var labels = compact ? Visibility.Collapsed : Visibility.Visible;
        MenuTitle.Visibility = labels;
        MoreTitle.Visibility = labels;
        StatusLine.Visibility = labels;
    }

    /// <summary>Makes the next AdvancedView open on this section, such as "SoundNav".</summary>
    public static void OpenNext(string navName) => _lastSection = navName;

    /// <summary>Opens a section by its menu entry, such as "SoundNav".</summary>
    public void ShowSection(string navName)
    {
        if (_sections.Keys.FirstOrDefault(item => item.Name == navName) is { } item)
        {
            item.IsChecked = true;
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

    // Segmented choices (noise removal, model, tuning) share one sliding highlight:
    // each track holds the highlight first and its buttons second.
    private void Track_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid track)
        {
            MoveIndicator(track, animate: false);
        }
    }

    private void Track_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is Grid track)
        {
            MoveIndicator(track, animate: track.IsLoaded);
        }
    }

    private static void MoveIndicator(Grid track, bool animate)
    {
        if (track.Children.Count < 2 ||
            track.Children[0] is not Border indicator ||
            track.Children[1] is not UniformGrid buttons ||
            indicator.RenderTransform is not TranslateTransform shift)
        {
            return;
        }

        var options = buttons.Children.OfType<RadioButton>().ToList();
        var index = options.FindIndex(option => option.IsChecked == true);
        indicator.Visibility = index < 0 ? Visibility.Hidden : Visibility.Visible;
        if (index < 0 || options.Count == 0 || track.ActualWidth <= 0)
        {
            return;
        }

        var width = track.ActualWidth / options.Count;
        indicator.Width = width;
        var target = index * width;
        if (animate && SystemParameters.ClientAreaAnimation)
        {
            shift.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(target, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.X = target;
        }
    }
}
