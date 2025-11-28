using System.Formats.Tar;
using System.Runtime.Versioning;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Helpers;

[SupportedOSPlatform("linux")]
public static class NfpmTool
{
    private static string? _nfpmPath;

    /// <summary>
    /// Get nfpm binary path. Extract from embedded resources if needed.
    /// </summary>
    public static async Task<string> GetNfpmPathAsync(string arch, string destDir)
    {
        if (_nfpmPath != null && File.Exists(_nfpmPath))
            return _nfpmPath;

        Logger.LogInfo("Extracting nfpm tool...");

        // Determine the embedded resource name based on current architecture
        if (!arch.Equals("arm-64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("nfpm tool is only available for arm-64 architecture");

        if (Directory.Exists(destDir))
            Directory.Delete(destDir, true);

        Directory.CreateDirectory(destDir);

        var nfpmPath = Path.Combine(destDir, "nfpm");

        var tarFilePath = Path.Combine(Constants.NetloyAppDir, "Assets", "nfpm-tool.tar.gz");

        // Decompression taf file to dest directory
        await using var tarFile = File.OpenRead(tarFilePath);
        await TarFile.ExtractToDirectoryAsync(tarFile, destDir, true);

        // Set executable permission on Linux
        File.SetUnixFileMode(nfpmPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _nfpmPath = nfpmPath;
        Logger.LogSuccess("nfpm tool extracted successfully");
        return nfpmPath;
    }

    /// <summary>
    /// Check if nfpm is available
    /// </summary>
    public static async Task<bool> IsAvailableAsync(string arch, string destDir)
    {
        try
        {
            await GetNfpmPathAsync(arch, destDir);
            return true;
        }
        catch
        {
            return false;
        }
    }
}