using Muted.Core.Support;

namespace Muted.App.ViewModels;

/// <summary>Commands passed in by a second Muted.exe, such as --toggle-mute from a Stream Deck.</summary>
internal sealed partial class MainViewModel
{
    /// <summary>Runs a command sent by a second launch, like "Muted.exe --toggle-mute".</summary>
    public async Task HandleRemoteAsync(RemoteRequest request)
    {
        switch (request.Command)
        {
            case RemoteCommand.Show:
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case RemoteCommand.ToggleMute:
                IsMuted = !IsMuted;
                break;
            case RemoteCommand.Mute:
                IsMuted = true;
                break;
            case RemoteCommand.Unmute:
                IsMuted = false;
                break;
            case RemoteCommand.ToggleSuppression:
                SuppressionEnabled = !SuppressionEnabled;
                break;
            case RemoteCommand.ToggleEngine:
                if (ToggleCommand.CanExecute(null))
                {
                    await ToggleAsync();
                }

                break;
            case RemoteCommand.Start:
                if (!IsRunning && ToggleCommand.CanExecute(null))
                {
                    await ToggleAsync();
                }

                break;
            case RemoteCommand.Stop:
                if (IsRunning && ToggleCommand.CanExecute(null))
                {
                    await ToggleAsync();
                }

                break;
            case RemoteCommand.ToggleOverlay:
                OverlayEnabled = !OverlayEnabled;
                break;
            case RemoteCommand.ToggleListening:
                ListeningEnabled = !ListeningEnabled;
                break;
            case RemoteCommand.Profile:
                var profile = Profiles.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, request.Argument, StringComparison.OrdinalIgnoreCase));
                if (profile is null)
                {
                    NotificationRequested?.Invoke(this, $"No profile called \"{request.Argument}\".");
                }
                else
                {
                    await ApplyProfileAsync(profile.Id);
                }

                break;
        }
    }
}
