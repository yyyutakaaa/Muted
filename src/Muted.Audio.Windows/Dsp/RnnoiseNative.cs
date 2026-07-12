using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Muted.Audio.Windows.Dsp;

internal static class RnnoiseNative
{
    internal const string LibraryName = "rnnoise";

    static RnnoiseNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(RnnoiseNative).Assembly, ResolveLibrary);
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int rnnoise_get_frame_size();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SafeRnnoiseHandle rnnoise_create(IntPtr model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rnnoise_destroy(IntPtr state);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe float rnnoise_process_frame(
        SafeRnnoiseHandle state,
        float* output,
        float* input);

    private static IntPtr ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "rnnoise.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "rnnoise.dll"),
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, "rnnoise.dll")
        ];

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        throw new DllNotFoundException(
            $"The verified RNNoise library is missing next to the application: {candidates[0]}");
    }
}

internal sealed class SafeRnnoiseHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeRnnoiseHandle()
        : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        RnnoiseNative.rnnoise_destroy(handle);
        return true;
    }
}
