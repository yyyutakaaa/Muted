using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Muted.App.ViewModels;
namespace Muted.App.Views;
public partial class AdvancedView : System.Windows.Controls.UserControl
{
    private MainViewModel? _model;
    public AdvancedView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _model = DataContext as MainViewModel; if (_model != null) _model.PropertyChanged += ModelChanged; MoveIndicator(false); };
        Unloaded += (_, _) => { if (_model != null) _model.PropertyChanged -= ModelChanged; _model = null; };
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(MainViewModel.NoiseRemoval)) MoveIndicator(true); }
    private void NoiseTrack_OnSizeChanged(object sender, SizeChangedEventArgs e) => MoveIndicator(false);
    private void MoveIndicator(bool animate)
    {
        if (NoiseIndicator == null || DataContext is not MainViewModel model) return;
        var width = NoiseTrack.ActualWidth / 3;
        NoiseIndicator.Width = width;
        var target = (int)model.NoiseRemoval * width;
        if (animate && SystemParameters.ClientAreaAnimation)
            NoiseShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { NoiseShift.BeginAnimation(TranslateTransform.XProperty, null); NoiseShift.X = target; }
    }
}
