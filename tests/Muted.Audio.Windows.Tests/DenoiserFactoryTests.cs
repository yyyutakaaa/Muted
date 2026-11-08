using System.IO;
using Muted.Audio.Windows.Dsp;
using Muted.Core.Audio;

namespace Muted.Audio.Windows.Tests;

public sealed class DenoiserFactoryTests
{
    public static TheoryData<StudioModel> Models => new()
    {
        StudioModel.DeepFilterNet3,
        StudioModel.DpdfNet2,
        StudioModel.DpdfNet8
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void EveryModelLoadsAndReportsItself(StudioModel model)
    {
        using var denoiser = DenoiserFactory.Create(model);
        Assert.Equal(DenoiserFactory.Id(model), denoiser.Id);
        Assert.True(File.Exists(DenoiserFactory.ModelPath(model)), DenoiserFactory.ModelPath(model));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void EveryModelDeliversTheSameFrameSizeAndDelay(StudioModel model)
    {
        using var denoiser = DenoiserFactory.Create(model);
        var input = new float[480];
        var output = new float[480];
        denoiser.Process(input, output);
        Assert.All(output, sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void IdsAreStableAndDistinct()
    {
        var models = new[] { StudioModel.DeepFilterNet3, StudioModel.DpdfNet2, StudioModel.DpdfNet8 };
        var ids = models.Select(DenoiserFactory.Id).ToArray();
        Assert.Equal(["deepfilternet3", "dpdfnet2", "dpdfnet8"], ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
