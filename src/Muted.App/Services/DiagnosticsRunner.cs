using System.IO;
using System.Reflection;
using System.Text;
using Muted.App.ViewModels;
using Muted.Audio.Windows.Devices;
using Muted.Core.Audio;

namespace Muted.App.Services;

/// <summary>
/// Builds the individual setup checks. The view model owns starting and stopping the
/// engine for the live signal test; everything that is just an observation lives here.
/// </summary>
internal sealed class DiagnosticsRunner(WasapiDeviceCatalog deviceCatalog)
{
    private readonly WasapiDeviceCatalog _deviceCatalog = deviceCatalog;

    public IEnumerable<DiagnosticCheck> CheckDevices(
        AudioDeviceInfo? input,
        AudioDeviceInfo? output,
        bool isRoutingReady)
    {
        yield return input is null
            ? new DiagnosticCheck("Microphone", "No input device is selected.", DiagnosticSeverity.Failed)
            : new DiagnosticCheck("Microphone", input.Name, DiagnosticSeverity.Passed);

        yield return output is null
            ? new DiagnosticCheck("Virtual cable", "No output device is selected.", DiagnosticSeverity.Failed)
            : isRoutingReady
                ? new DiagnosticCheck("Virtual cable", output.Name, DiagnosticSeverity.Passed)
                : new DiagnosticCheck(
                    "Virtual cable",
                    $"{output.Name} does not look like a virtual cable output.",
                    DiagnosticSeverity.Failed);

        if (input is not null)
        {
            yield return CheckFormat("Microphone format", input);
        }

        if (output is not null)
        {
            yield return CheckFormat("Cable format", output);
        }
    }

    public DiagnosticCheck CheckFormat(string title, AudioDeviceInfo device)
    {
        try
        {
            var format = _deviceCatalog.GetMixFormat(device.Id);
            return format.SampleRate == 48_000
                ? new DiagnosticCheck(title, format.DisplayName, DiagnosticSeverity.Passed)
                : new DiagnosticCheck(
                    title,
                    $"Windows uses {format.DisplayName}. Set the device's default format to 48 kHz.",
                    DiagnosticSeverity.Warning);
        }
        catch (Exception exception)
        {
            return new DiagnosticCheck(title, exception.Message, DiagnosticSeverity.Failed);
        }
    }

    public DiagnosticCheck CheckRuntime()
    {
        var rnnoisePath = Path.Combine(AppContext.BaseDirectory, "rnnoise.dll");
        return File.Exists(rnnoisePath)
            ? new DiagnosticCheck("RNNoise runtime", "rnnoise.dll is present.", DiagnosticSeverity.Passed)
            : new DiagnosticCheck(
                "RNNoise runtime",
                "rnnoise.dll is missing. Repair or reinstall Muted.",
                DiagnosticSeverity.Failed);
    }

    public IEnumerable<DiagnosticCheck> CheckSignal(float peak, float processingLoad, long underruns)
    {
        yield return peak > 0.005f
            ? new DiagnosticCheck(
                "Microphone signal",
                $"Signal received ({20 * Math.Log10(peak):0.0} dB peak).",
                DiagnosticSeverity.Passed)
            : new DiagnosticCheck(
                "Microphone signal",
                "No clear signal was detected. Speak into the selected microphone and run again.",
                DiagnosticSeverity.Warning);

        yield return processingLoad < 0.85f
            ? new DiagnosticCheck(
                "Processing headroom",
                $"RNNoise peak processing load was {processingLoad * 100:0}%.",
                DiagnosticSeverity.Passed)
            : new DiagnosticCheck(
                "Processing headroom",
                $"RNNoise reached {processingLoad * 100:0}% processing load; audio may stutter.",
                DiagnosticSeverity.Warning);

        yield return underruns == 0
            ? new DiagnosticCheck(
                "Output stability",
                "The cable received an uninterrupted stream.",
                DiagnosticSeverity.Passed)
            : new DiagnosticCheck(
                "Output stability",
                $"{underruns} samples were missing from the output. Try a higher latency setting.",
                DiagnosticSeverity.Warning);
    }

    public static string Summarize(IReadOnlyCollection<DiagnosticCheck> checks)
    {
        var failures = checks.Count(check => check.Severity == DiagnosticSeverity.Failed);
        var warnings = checks.Count(check => check.Severity == DiagnosticSeverity.Warning);
        return failures > 0
            ? $"{failures} problem(s) need attention."
            : warnings > 0
                ? $"Setup works, with {warnings} warning(s)."
                : "Everything looks ready.";
    }

    /// <summary>A plain-text version of the report, for pasting into a bug report.</summary>
    public static string BuildReport(
        IEnumerable<DiagnosticCheck> checks,
        AudioDeviceInfo? input,
        AudioDeviceInfo? output,
        AudioMetrics metrics)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
        var builder = new StringBuilder();
        builder.AppendLine("Muted setup check");
        builder.AppendLine($"Version: {version}");
        builder.AppendLine($"Windows: {Environment.OSVersion.VersionString} ({Environment.ProcessorCount} cores)");
        builder.AppendLine($"Microphone: {input?.Name ?? "none"}");
        builder.AppendLine($"Output: {output?.Name ?? "none"}");
        builder.AppendLine(
            $"Buffer: {metrics.BufferedMilliseconds:0.0} ms, load {metrics.ProcessingLoad * 100:0}%, " +
            $"underruns {metrics.OutputUnderrunSamples}");
        builder.AppendLine();

        foreach (var check in checks)
        {
            builder.AppendLine($"[{check.Severity}] {check.Title}: {check.Detail}");
        }

        return builder.ToString();
    }
}
