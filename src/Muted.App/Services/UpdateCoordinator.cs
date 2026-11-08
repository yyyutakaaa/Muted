using System.Windows;
using Muted.Core.Settings;

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
        Window? owner = null,
        string? skippedVersion = null,
        UpdateChannel channel = UpdateChannel.Stable)
    {
        if (Interlocked.Exchange(ref _isChecking, 1) != 0)
        {
            return new UpdatePromptResult("An update check is already running.", false);
        }

        try
        {
            var result = await _updateService.CheckAsync(channel);
            if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Update is null)
            {
                if (showNoUpdateMessage)
                {
                    var upToDate = result.Status == UpdateCheckStatus.UpToDate;
                    UpdateDialog.ShowNotice(
                        owner,
                        upToDate ? "You're up to date" : "No update right now",
                        result.Message,
                        upToDate ? UpdateNoticeKind.UpToDate : UpdateNoticeKind.Warning);
                }

                return new UpdatePromptResult(result.Message, false);
            }

            var version = result.Update.AvailableVersion.ToString(3);

            // A version the user skipped stays quiet in the background, but an explicit
            // "check for updates" always shows what is out there.
            if (!showNoUpdateMessage &&
                string.Equals(version, skippedVersion, StringComparison.Ordinal))
            {
                return new UpdatePromptResult($"Muted {version} is available but skipped.", false);
            }

            var update = result.Update;
            var choice = UpdateDialog.ShowUpdate(
                owner,
                update,
                progress => _updateService.DownloadAndStartAsync(update, progress));

            switch (choice)
            {
                case UpdateDialogResult.Skip:
                    return new UpdatePromptResult($"Muted {version} was skipped.", false, version);
                case UpdateDialogResult.Failed:
                    return new UpdatePromptResult(
                        "The update could not be downloaded or started. Please try again later.",
                        false);
                case UpdateDialogResult.Installing:
                    InstallStarted?.Invoke(this, EventArgs.Empty);
                    return new UpdatePromptResult($"Installing Muted {version}…", true);
                default:
                    return new UpdatePromptResult($"Muted {version} is available. Update postponed.", false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }
}

internal sealed record UpdatePromptResult(
    string Message,
    bool InstallStarted,
    string? SkippedVersion = null);
