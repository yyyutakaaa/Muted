namespace Muted.Core.Settings;

/// <summary>Switches to a profile while a given program runs, such as a game or a call app.</summary>
public sealed record AutoProfileRule
{
    public const int MaximumRules = 20;
    public const int MaximumProcessNameLength = 64;

    /// <summary>The executable name, with or without ".exe", like "valorant" or "Teams.exe".</summary>
    public string ProcessName { get; init; } = string.Empty;

    public string ProfileId { get; init; } = string.Empty;

    /// <summary>Lower case, without path or ".exe", so "C:\Games\Valorant.EXE" and "valorant" match.</summary>
    public static string NormalizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim().Trim('"');
        var slash = trimmed.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
        {
            trimmed = trimmed[(slash + 1)..];
        }

        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        trimmed = trimmed.Trim().ToLowerInvariant();
        return trimmed.Length > MaximumProcessNameLength ? trimmed[..MaximumProcessNameLength] : trimmed;
    }

    public AutoProfileRule Normalize() => this with
    {
        ProcessName = NormalizeProcessName(ProcessName),
        ProfileId = ProfileId?.Trim() ?? string.Empty
    };

    /// <summary>
    /// The profile the first matching rule asks for, or null when none of the
    /// programs is running. Rules are checked in order, so the list is a priority list.
    /// </summary>
    public static string? Match(IEnumerable<AutoProfileRule> rules, IEnumerable<string> runningProcesses)
    {
        var running = new HashSet<string>(
            runningProcesses.Select(NormalizeProcessName).Where(name => name.Length > 0),
            StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var name = NormalizeProcessName(rule.ProcessName);
            if (name.Length > 0 && rule.ProfileId.Length > 0 && running.Contains(name))
            {
                return rule.ProfileId;
            }
        }

        return null;
    }
}
