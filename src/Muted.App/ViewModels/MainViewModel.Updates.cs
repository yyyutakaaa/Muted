using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>Checking for updates and the update channel.</summary>
internal sealed partial class MainViewModel
{
    private UpdateChannel _updateChannel = UpdateChannel.Stable;
    private string _updateStatusText = "Muted checks for updates when it starts.";
    private string? _skippedUpdateVersion;

    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public UpdateChannel UpdateChannel
    {
        get => _updateChannel;
        set
        {
            if (SetProperty(ref _updateChannel, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public string? SkippedUpdateVersion => _skippedUpdateVersion;

    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusText = "Checking for updates…";
        var result = await _updateCoordinator.CheckAndPromptAsync(
            showNoUpdateMessage: true,
            System.Windows.Application.Current.MainWindow,
            _skippedUpdateVersion,
            UpdateChannel);
        ReportUpdateStatus(result);
    }

    public void ReportUpdateStatus(UpdatePromptResult result)
    {
        UpdateStatusText = result.Message;
        if (result.SkippedVersion is not null && result.SkippedVersion != _skippedUpdateVersion)
        {
            _skippedUpdateVersion = result.SkippedVersion;
            QueueSave();
        }
    }
}
