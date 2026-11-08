using System.Diagnostics;
using System.IO;
using Muted.Audio.Windows.Dsp;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// Measures the real per-frame cost of every Studio Voice model, so the thread setting
/// and the real-time budget rest on numbers. Writes a report when MUTED_BENCHMARK names
/// a file, and only then checks the 10 ms budget with the default settings.
/// </summary>
public sealed class DenoiserBenchmark
{
    private const int Frame = 480;

    public static TheoryData<string> Models => new() { "deepfilternet3", "dpdfnet2_48khz_hr", "dpdfnet8_48khz_hr" };

    private static IDenoiser Create(string name, int threads) => name == "deepfilternet3"
        ? new DeepFilterProcessor(Path.Combine(AppContext.BaseDirectory, "models", "deepfilternet3.onnx"), threads)
        : new DpdfNetProcessor(Path.Combine(AppContext.BaseDirectory, "models", name + ".onnx"), name, threads);

    private static (double Mean, double P99, double Cpu) Measure(string name, int threads)
    {
        using var denoiser = Create(name, threads);
        var signal = SpeechFixture.Load();
        var output = new float[Frame];
        var frames = signal.Length / Frame;
        for (var frame = 0; frame < 30; frame++)
        {
            denoiser.Process(signal.AsSpan(frame * Frame, Frame), output);
        }

        var times = new List<double>();
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        for (var round = 0; round < 2; round++)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var started = Stopwatch.GetTimestamp();
                denoiser.Process(signal.AsSpan(frame * Frame, Frame), output);
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

                // Real time: one frame every 10 ms, like the audio thread.
                var due = (times.Count) * 10.0;
                while (wall.Elapsed.TotalMilliseconds < due)
                {
                    Thread.Sleep(1);
                }
            }
        }

        process.Refresh();
        var cores = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / wall.Elapsed.TotalMilliseconds;
        times.Sort();
        return (times.Average(), times[(int)(times.Count * 0.99)], cores);
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void FitsTheTenMillisecondBudget(string name)
    {
        // Timing depends on the machine, so this only runs when benchmarking on purpose.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MUTED_BENCHMARK")))
        {
            return;
        }

        var (mean, p99, _) = Measure(name, DenoiserSession.DefaultThreads);
        Assert.True(mean < 8.0, $"{name}: {mean:0.00} ms per 10 ms frame on average.");
        Assert.True(p99 < 10.0, $"{name}: {p99:0.00} ms for the slowest 1% of frames.");
    }

    [Fact]
    public void Report()
    {
        var path = Environment.GetEnvironmentVariable("MUTED_BENCHMARK");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var lines = new List<string> { "model\tthreads\tmean ms\tp99 ms\tcores busy" };
        foreach (var name in new[] { "deepfilternet3", "dpdfnet2_48khz_hr", "dpdfnet8_48khz_hr" })
        {
            foreach (var threads in new[] { 1, 2, 4, 6 })
            {
                var (mean, p99, cores) = Measure(name, threads);
                lines.Add($"{name}\t{threads}\t{mean:0.00}\t{p99:0.00}\t{cores:0.00}");
            }
        }

        File.WriteAllLines(path, lines);
    }
}
