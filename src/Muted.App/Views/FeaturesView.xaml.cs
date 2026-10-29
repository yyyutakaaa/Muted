using Muted.App.ViewModels;

namespace Muted.App.Views;

public partial class FeaturesView : System.Windows.Controls.UserControl
{
    public FeaturesView() => InitializeComponent();

    /// <summary>The list of running programs is only read when someone opens it.</summary>
    private void RunningPrograms_OnDropDownOpened(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel && viewModel.RefreshRunningProgramsCommand.CanExecute(null))
        {
            viewModel.RefreshRunningProgramsCommand.Execute(null);
        }
    }
}
