using System.Windows;

namespace Muted.App.Views;

public partial class ShortcutsView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader),
        typeof(bool),
        typeof(ShortcutsView),
        new PropertyMetadata(true, (sender, args) =>
            ((ShortcutsView)sender).Header.Visibility = (bool)args.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public ShortcutsView()
    {
        InitializeComponent();
    }

    /// <summary>False where the surrounding page already names the section.</summary>
    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }
}
