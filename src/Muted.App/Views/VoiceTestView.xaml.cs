using System.Windows;

namespace Muted.App.Views;

public partial class VoiceTestView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle),
        typeof(bool),
        typeof(VoiceTestView),
        new PropertyMetadata(true, (sender, args) =>
            ((VoiceTestView)sender).TitleText.Visibility = (bool)args.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public VoiceTestView() => InitializeComponent();

    /// <summary>False where the surrounding page already names the section.</summary>
    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }
}
