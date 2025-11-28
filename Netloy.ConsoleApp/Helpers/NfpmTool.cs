using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Helpers;

public static class NfpmTool
{
    private static string? _nfpmPath;

    /// <summary>
    /// Get nfpm binary path. Extract from embedded resources if needed.
    /// </summary>
    public static async Task<string> GetNfpmPathAsync(string arch, string destDir)
    {
        try
        {
            if (!_nfpmPath.IsStringNullOrEmpty() && File.Exists(_nfpmPath))
                return _nfpmPath;

            Logger.LogInfo("Extracting nfpm tool...");

            // Determine the embedded resource name based on current architecture
            if (!arch.Equals("linux-arm64", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("nfpm tool is only available for linux-arm64 architecture");

            if (Directory.Exists(destDir))
                Directory.Delete(destDir, true);

            Directory.CreateDirectory(destDir);

            var nfpmPath = Path.Combine(destDir, "nfpm");
            var tarFilePath = Path.Combine(Constants.NetloyAppDir, "Assets", "nfpm-tool.tar.gz");

            await using var fileStream = File.OpenRead(tarFilePath);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzipStream, destDir, overwriteFiles: true);

            await SetNfpmExecutableAsync(nfpmPath);

            _nfpmPath = nfpmPath;
            Logger.LogSuccess("nfpm tool extracted successfully");
            return nfpmPath;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            throw;
        }
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

    private static async Task SetNfpmExecutableAsync(string nfpmPath)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{nfpmPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Couldn't make nfpm executable.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
            return;

        var message = "Couldn't make nfpm executable.";
        if (!error.IsStringNullOrEmpty() || !output.IsStringNullOrEmpty())
        {
            var errorMessage = !error.IsStringNullOrEmpty() ? error : output;
            message += $" Error message: {errorMessage}";
        }

        throw new InvalidOperationException(message);
    }
}