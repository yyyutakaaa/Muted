using Muted.Core.Audio;
using Muted.Core.Dsp;

namespace Muted.Audio.Windows.Dsp;

/// <summary>
/// Builds the denoiser for a Studio Voice model. One place knows the file names and how
/// each model is constructed, so the engine and the tests agree.
/// </summary>
public static class DenoiserFactory
{
    public const string DpdfNet2FileName = "dpdfnet2_48khz_hr.onnx";
    public const string DpdfNet8FileName = "dpdfnet8_48khz_hr.onnx";

    /// <summary>Where the installer and the build put the models, next to Muted.exe.</summary>
    public static string ModelsDirectory => Path.Combine(AppContext.BaseDirectory, "models");

    public static string ModelPath(StudioModel model) => model switch
    {
        StudioModel.DpdfNet2 => Path.Combine(ModelsDirectory, DpdfNet2FileName),
        StudioModel.DpdfNet8 => Path.Combine(ModelsDirectory, DpdfNet8FileName),
        _ => Path.Combine(ModelsDirectory, DeepFilterProcessor.ModelFileName)
    };

    /// <summary>The name the settings file and the UI use.</summary>
    public static string Id(StudioModel model) => model switch
    {
        StudioModel.DpdfNet2 => "dpdfnet2",
        StudioModel.DpdfNet8 => "dpdfnet8",
        _ => "deepfilternet3"
    };

    public static string DisplayName(StudioModel model) => model switch
    {
        StudioModel.DpdfNet2 => "Balanced",
        StudioModel.DpdfNet8 => "Maximum quality",
        _ => "Classic"
    };

    public static IDenoiser Create(StudioModel model)
    {
        var path = ModelPath(model);
        return model switch
        {
            StudioModel.DpdfNet2 => new DpdfNetProcessor(path, Id(model)),
            StudioModel.DpdfNet8 => new DpdfNetProcessor(path, Id(model)),
            _ => new DeepFilterProcessor(path)
        };
    }
}
