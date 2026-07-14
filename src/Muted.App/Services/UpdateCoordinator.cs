using System.Windows;

namespace Muted.App.Services;

internal sealed class UpdateCoordinator
{
    private readonly UpdateService _updateService;
    private int _isChecking;

    public UpdateCoordinator(UpdateService updateService)
    {
        _updateService = updateService;
    }

    public event EventHandler? InstallStarted;

    public async Task<UpdatePromptResult> CheckAndPromptAsync(
        bool showNoUpdateMessage,
        Window? owner = null)
    {
        if (Interlocked.Exchange(ref _isChecking, 1) != 0)
        {
            return new UpdatePromptResult("An update check is already running.", false);
        }

        try
        {
            var result = await _updateService.CheckAsync();
            if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Update is null)
            {
                if (showNoUpdateMessage)
                {
                    ShowMessage(owner, result.Message, MessageBoxButton.OK,
                        result.Status == UpdateCheckStatus.UpToDate
                            ? MessageBoxImage.Information
                            : MessageBoxImage.Warning);
                }

                return new UpdatePromptResult(result.Message, false);
            }

            var version = result.Update.AvailableVersion.ToString(3);
            var answer = ShowMessage(
                owner,
                $"Muted {version} is available.{Environment.NewLine}{Environment.NewLine}" +
                "Do you want to download and install it now? Muted will restart automatically.",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
            {
                return new UpdatePromptResult($"Muted {version} is available. Update postponed.", false);
            }

            var started = await _updateService.DownloadAndStartAsync(result.Update);
            if (!started)
            {
                const string message = "The update could not be downloaded or started. Please try again later.";
                ShowMessage(owner, message, MessageBoxButton.OK, MessageBoxImage.Error);
                return new UpdatePromptResult(message, false);
            }

            InstallStarted?.Invoke(this, EventArgs.Empty);
            return new UpdatePromptResult($"Installing Muted {version}…", true);
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    private static MessageBoxResult ShowMessage(
        Window? owner,
        string message,
        MessageBoxButton buttons,
        MessageBoxImage image) =>
        owner?.IsVisible == true
            ? System.Windows.MessageBox.Show(owner, message, "Muted update", buttons, image)
            : System.Windows.MessageBox.Show(message, "Muted update", buttons, image);
}

internal sealed record UpdatePromptResult(string Message, bool InstallStarted);
