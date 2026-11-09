using System.Globalization;
using System.IO;
using Muted.Core.Speech;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// Scores how deep the closed gate goes under room tone; runs only when MUTED_SWEEP_REPORT
/// names a file. DNSMOS rewards silence, so deeper always scores a little higher: use it
/// to see the cost of an audible room, not to pick the level.
/// </summary>
public sealed class RoomToneSweep
{
    [Fact]
    public void ScoresRoomToneLevels()
    {
        var report = Environment.GetEnvironmentVariable("MUTED_SWEEP_REPORT");
        if (string.IsNullOrWhiteSpace(report))
        {
            return;
        }

        var input = QualityHarnessTests.NoisyRecording();
        using var dnsmos = new Dnsmos();
        var lines = new List<string> { "model,gate_floor_db,sig,bak,ovrl" };
        foreach (var model in new[] { "deepfilternet3", "dpdfnet2_48khz_hr", "dpdfnet8_48khz_hr" })
        {
            // Room tone 20 dB down, with the closed gate at different depths.
            foreach (var db in new[] { 10, 15, 20, 30 })
            {
                var setup = new ChainSetup($"{model}-{db}", model, NewGate: true, RoomTone: true, GateFloor: MathF.Pow(10f, -db / 20f));
                var (sig, bak, ovrl) = dnsmos.Score(Downsampler.Downsample(OfflineChain.ThroughOpus(OfflineChain.Run(input, setup), 24_000)));
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{model},{db},{sig:0.000},{bak:0.000},{ovrl:0.000}"));
            }
        }

        File.WriteAllLines(report, lines);
    }
}
