using System.Runtime.Intrinsics.X86;

namespace RecompSetup;

public sealed record SystemCheckResult(bool Supported, string Summary, IReadOnlyList<string> Warnings);

public static class SystemCheck
{
    public static SystemCheckResult Run()
    {
        var warnings = new List<string>();
        bool supported = true;

        if (!OperatingSystem.IsWindows())
        {
            supported = false;
            warnings.Add("Windows 10/11 64-bit is required.");
        }
        if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
        {
            supported = false;
            warnings.Add("A 64-bit Windows installation is required.");
        }
        if (!Avx2.IsSupported)
        {
            supported = false;
            warnings.Add("Your CPU does not expose AVX2, which the recompilation requires.");
        }

        var os = Environment.OSVersion.Version;
        if (OperatingSystem.IsWindows() && os.Major < 10)
        {
            supported = false;
            warnings.Add("Windows 10 or newer is required for the Direct3D 12 graphics path.");
        }

        string summary = supported
            ? "System check passed: 64-bit Windows and AVX2 are available."
            : "This PC does not meet the minimum requirements.";
        return new SystemCheckResult(supported, summary, warnings);
    }
}
