using System.Text;

namespace Muted.Core.Support;

/// <summary>
/// Builds a link to a pre-filled GitHub bug report. Nothing is sent: the browser opens
/// the form and the person decides what to submit.
/// </summary>
public static class IssueLink
{
    public const string Repository = "https://github.com/yyyutakaaa/Muted";

    // Browsers and GitHub accept long URLs, but not endless ones. This keeps the whole
    // link under about 8 KB even when every character needs escaping.
    private const int MaximumEncodedReportLength = 6_000;
    private const string TruncatedNote = "\n[report shortened; use Copy report for the full text]";

    public static string Build(string? report, string? version, string? title = null)
    {
        var builder = new StringBuilder(Repository);
        builder.Append("/issues/new?template=bug_report.yml&labels=bug");
        Append(builder, "title", string.IsNullOrWhiteSpace(title) ? "[Bug] " : title);
        if (!string.IsNullOrWhiteSpace(version))
        {
            Append(builder, "version", version);
        }

        if (!string.IsNullOrWhiteSpace(report))
        {
            Append(builder, "diagnostics", Fit(report.Trim()));
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));

    private static string Fit(string report)
    {
        if (Uri.EscapeDataString(report).Length <= MaximumEncodedReportLength)
        {
            return report;
        }

        // Shrink until it fits; escaping can triple a character, so measure, not guess.
        var length = report.Length;
        string candidate;
        do
        {
            length = (int)(length * 0.85);
            candidate = report[..length] + TruncatedNote;
        }
        while (length > 0 && Uri.EscapeDataString(candidate).Length > MaximumEncodedReportLength);

        return candidate;
    }
}
