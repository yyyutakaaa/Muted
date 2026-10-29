namespace Muted.Core.Support;

/// <summary>
/// What a second launch of Muted.exe can ask the running copy to do. This is what lets a
/// Stream Deck button, an AutoHotkey script or an OBS hotkey drive Muted without a plugin.
/// </summary>
public enum RemoteCommand
{
    Show,
    ToggleMute,
    Mute,
    Unmute,
    ToggleSuppression,
    ToggleEngine,
    Start,
    Stop,
    ToggleOverlay,
    ToggleListening,
    Profile
}

public readonly record struct RemoteRequest(RemoteCommand Command, string? Argument = null)
{
    /// <summary>The single line sent over the pipe, like "profile Gaming".</summary>
    public string Serialize() => Argument is null
        ? CommandLine.Name(Command)
        : CommandLine.Name(Command) + " " + Argument;

    public static RemoteRequest? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        var space = trimmed.IndexOf(' ');
        var name = space < 0 ? trimmed : trimmed[..space];
        var argument = space < 0 ? null : trimmed[(space + 1)..].Trim();
        var command = CommandLine.FromName(name);
        if (command is null)
        {
            return null;
        }

        if (command == RemoteCommand.Profile)
        {
            return string.IsNullOrWhiteSpace(argument) || argument.Length > 80
                ? null
                : new RemoteRequest(RemoteCommand.Profile, argument);
        }

        return new RemoteRequest(command.Value);
    }
}

public static class CommandLine
{
    private static readonly (string Name, RemoteCommand Command)[] Names =
    [
        ("show", RemoteCommand.Show),
        ("toggle-mute", RemoteCommand.ToggleMute),
        ("mute", RemoteCommand.Mute),
        ("unmute", RemoteCommand.Unmute),
        ("toggle-suppression", RemoteCommand.ToggleSuppression),
        ("toggle", RemoteCommand.ToggleEngine),
        ("start", RemoteCommand.Start),
        ("stop", RemoteCommand.Stop),
        ("toggle-overlay", RemoteCommand.ToggleOverlay),
        ("toggle-listening", RemoteCommand.ToggleListening),
        ("profile", RemoteCommand.Profile)
    ];

    public static string Name(RemoteCommand command) =>
        Names.First(entry => entry.Command == command).Name;

    public static RemoteCommand? FromName(string? name)
    {
        var normalized = name?.Trim().TrimStart('-', '/').ToLowerInvariant();
        foreach (var entry in Names)
        {
            if (entry.Name == normalized)
            {
                return entry.Command;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads "--toggle-mute" or "--profile Gaming" (also "--profile=Gaming") from the
    /// arguments. Unknown arguments, such as "--minimized", are ignored.
    /// </summary>
    public static RemoteRequest? Parse(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith('-') && !argument.StartsWith('/'))
            {
                continue;
            }

            var equals = argument.IndexOf('=');
            var name = equals < 0 ? argument : argument[..equals];
            var command = FromName(name);
            if (command is null)
            {
                continue;
            }

            if (command != RemoteCommand.Profile)
            {
                return new RemoteRequest(command.Value);
            }

            var value = equals >= 0
                ? argument[(equals + 1)..]
                : index + 1 < arguments.Count ? arguments[index + 1] : null;
            return RemoteRequest.Parse("profile " + value);
        }

        return null;
    }

    public const string Usage = """
        Muted.exe [--minimized] [command]

        Commands, sent to the copy of Muted that is already running:
          --show                 bring the window back
          --toggle-mute          mute or unmute the microphone
          --mute, --unmute
          --toggle-suppression   switch noise suppression on or off
          --toggle               start or stop audio
          --start, --stop
          --toggle-overlay       show or hide the in-game mute indicator
          --toggle-listening     switch cleaning what you hear on or off
          --profile <name>       apply a saved profile by name
        """;
}
