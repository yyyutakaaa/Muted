using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Muted.App.ViewModels;

namespace Muted.App.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    private static readonly string[] BuiltInPresets = ["Natural", "Gaming", "Podcast", "Meeting"];
    private MainViewModel? _viewModel;
    private bool _syncingPresets;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncPresetPicker();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.ActiveProfileName) or nameof(MainViewModel.ActiveProfileId))
        {
            SyncPresetPicker();
        }
    }

    /// <summary>
    /// The picker lists the four built-in presets and, when a saved profile of the
    /// user's own is active, that one too, so it always shows what is in use.
    /// </summary>
    private void SyncPresetPicker()
    {
        var active = _viewModel?.ActiveProfileName;
        var items = BuiltInPresets.ToList();
        if (!string.IsNullOrEmpty(active) && active != "No profile" && !items.Contains(active))
        {
            items.Insert(0, active);
        }

        _syncingPresets = true;
        try
        {
            if (!(PresetPicker.ItemsSource is List<string> current && current.SequenceEqual(items)))
            {
                PresetPicker.ItemsSource = items;
            }

            PresetPicker.SelectedItem = items.Contains(active ?? string.Empty) ? active : null;
        }
        finally
        {
            _syncingPresets = false;
        }
    }

    // Press and hold is a mouse gesture, not a command, so it lives in code-behind.
    private void BypassButton_OnPressed(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel && BypassButton.IsEnabled)
        {
            viewModel.BeginBypass();
        }
    }

    private void BypassButton_OnReleased(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.EndBypass();
        }
    }

    private void PushToTalk_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedPage = AppPage.Shortcuts;
        }
    }

    /// <summary>
    /// Picking a preset applies it. Selections made by <see cref="SyncPresetPicker"/>
    /// to follow the active profile never run a preset command.
    /// </summary>
    private void PresetPicker_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_syncingPresets ||
            DataContext is not MainViewModel viewModel ||
            PresetPicker.SelectedItem is not string name ||
            name == viewModel.ActiveProfileName ||
            !BuiltInPresets.Contains(name))
        {
            return;
        }

        ICommand command = name switch
        {
            "Gaming" => viewModel.GamingPresetCommand,
            "Podcast" => viewModel.PodcastPresetCommand,
            "Meeting" => viewModel.MeetingPresetCommand,
            _ => viewModel.NaturalPresetCommand
        };

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
        else
        {
            SyncPresetPicker();
        }
    }
}
