using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using Muted.App.ViewModels;
namespace Muted.App.Views;
public partial class SimpleView : System.Windows.Controls.UserControl
{
    private MainViewModel? _model;
    public SimpleView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _model = DataContext as MainViewModel; if (_model != null) _model.PropertyChanged += ModelChanged; };
        Unloaded += (_, _) => { if (_model != null) _model.PropertyChanged -= ModelChanged; _model = null; };
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRunning) && SystemParameters.ClientAreaAnimation)
            PowerButton.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(160)));
    }
    private void Test_OnClick(object sender, RoutedEventArgs e) => TestArea.Visibility = TestArea.IsVisible ? Visibility.Collapsed : Visibility.Visible;
    private void Settings_OnClick(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.SelectedPage = AppPage.Settings; }
}
