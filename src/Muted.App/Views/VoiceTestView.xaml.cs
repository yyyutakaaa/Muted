using System.Windows;

namespace Muted.App.Views;

public partial class VoiceTestView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle),
        typeof(bool),
        typeof(VoiceTestView),
        new PropertyMetadata(true, (sender, args) =>
        {
            // On its own page the details are the point, so they start open there.
            var view = (VoiceTestView)sender;
            var show = (bool)args.NewValue;
            view.TitleText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            view.Details.IsExpanded = !show;
        }));

    public VoiceTestView() => InitializeComponent();

    /// <summary>False where the surrounding page already names the section.</summary>
    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }
}
