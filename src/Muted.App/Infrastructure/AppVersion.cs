using System.Reflection;

namespace Muted.App.Infrastructure;

/// <summary>
/// The version Muted runs, from the build: three numbers, and whether it is a beta.
/// A release build carries its tag, such as 0.10.0-beta1, as the informational version;
/// a suffix after the numbers means a prerelease from the beta channel.
/// </summary>
internal static class AppVersion
{
    private static readonly Assembly? Entry = Assembly.GetEntryAssembly();

    /// <summary>Such as "0.10.0", or "unknown".</summary>
    public static string Number => Entry?.GetName().Version?.ToString(3) ?? "unknown";

    private static string? Informational => Entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>The prerelease suffix, such as "beta1", or empty for a stable release.</summary>
    public static string Suffix => SuffixOf(Informational);

    /// <summary>The build: the first 7 characters of the commit it was built from, or empty.</summary>
    public static string Build => BuildOf(Informational);

    public static bool IsBeta => Suffix.Length > 0;

    /// <summary>"Beta" or "Stable", for the corner of the window.</summary>
    public static string Channel => IsBeta ? "Beta" : "Stable";

    /// <summary>Such as "0.10.0 beta1" or "0.10.0".</summary>
    public static string Display => IsBeta ? $"{Number} {Suffix}" : Number;

    /// <summary>The line under the version in the corner, such as "Beta · c6bd6a6".</summary>
    public static string ChannelAndBuild => Build.Length > 0 ? $"{Channel} · {Build}" : Channel;

    /// <summary>The commit after "+" in the informational version, shortened to 7 characters.</summary>
    internal static string BuildOf(string? informationalVersion)
    {
        var value = informationalVersion ?? string.Empty;
        var metadata = value.IndexOf('+');
        if (metadata < 0)
        {
            return string.Empty;
        }

        var commit = value[(metadata + 1)..].Trim();
        return commit.Length > 7 ? commit[..7] : commit;
    }

    /// <summary>The part between the first "-" and any "+build" metadata.</summary>
    internal static string SuffixOf(string? informationalVersion)
    {
        var value = informationalVersion ?? string.Empty;
        var metadata = value.IndexOf('+');
        if (metadata >= 0)
        {
            value = value[..metadata];
        }

        var dash = value.IndexOf('-');
        return dash > 0 ? value[(dash + 1)..] : string.Empty;
    }
}
