using System.Diagnostics;
using System.IO.Compression;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Mac;

public class AppPackageBuilder : MacOsPackageBuilderBase, IPackageBuilder
{
    public AppPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
    }

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting macOS App Bundle package build...", forceLog: true);

        // Create App Bundle structure
        Logger.LogInfo("Creating .app bundle structure...");
        CreateAppBundleStructure();

        // Publish project
        await PublishAsync(PublishOutputDir, ".icns");

        // Copy icon to Resources directory
        Logger.LogInfo("Copying application icon...");
        CopyApplicationIcon();

        // Generate or copy Info.plist
        Logger.LogInfo("Processing Info.plist...");
        await ProcessInfoPlistAsync();

        // Generate or copy Entitlements
        Logger.LogInfo("Processing Entitlements...");
        await ProcessEntitlementsAsync();

        // Generate PkgInfo
        Logger.LogInfo("Generating PkgInfo...");
        GeneratePkgInfo();

        // Set executable permissions
        Logger.LogInfo("Setting executable permissions...");
        await SetExecutablePermissionsAsync();

        // Code Signing (optional)
        if (!Arguments.MacOsSigningIdentity.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Code signing app bundle...");
            await SignAppBundleAsync();
        }

        // Notarization (optional)
        if (!Arguments.AppleId.IsStringNullOrEmpty() &&
            !Arguments.AppleTeamId.IsStringNullOrEmpty() &&
            !Arguments.ApplePassword.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Notarizing app bundle...");
            await NotarizeAppBundleAsync();
        }

        // Create zip archive
        Logger.LogInfo("Creating zip archive...");
        await CreateZipArchiveAsync();

        Logger.LogSuccess("macOS App Bundle package build completed successfully!");
    }

    public bool Validate()
    {
        return ValidateMacOsPackage();
    }

    public void Clear()
    {
        try
        {
            Logger.LogInfo("Cleaning up '{0}'...", RootDirectory);
            Directory.Delete(RootDirectory, true);
            Logger.LogSuccess("Cleanup completed successfully!");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            throw;
        }
    }

    #region Zip Archive Creation

    private async Task CreateZipArchiveAsync()
    {
        var zipPath = Path.Combine(OutputDirectory, OutputName);

        // Delete existing zip if exists
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        try
        {
            // Use ditto command for better macOS compatibility
            if (IsDittoAvailable())
            {
                await CreateZipWithDittoAsync(zipPath);
            }
            else
            {
                // Fallback to .NET zip
                CreateZipWithDotNet(zipPath);
            }

            Logger.LogInfo("Zip archive created at: {0}", zipPath);
            var fileInfo = new FileInfo(zipPath);
            Logger.LogInfo("Archive size: {0:F2} MB", fileInfo.Length / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to create zip archive: {0}", forceLog: true, ex.Message);
            throw;
        }
    }

    private static bool IsDittoAvailable()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "ditto",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task CreateZipWithDittoAsync(string zipPath)
    {
        var appBundleName = Path.GetFileName(AppBundlePath);
        var appBundleParent = Path.GetDirectoryName(AppBundlePath);

        var processInfo = new ProcessStartInfo
        {
            FileName = "ditto",
            Arguments = $"-c -k --sequesterRsrc --keepParent \"{appBundleName}\" \"{zipPath}\"",
            WorkingDirectory = appBundleParent,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo) ??
                            throw new InvalidOperationException("Failed to start ditto command.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!output.IsStringNullOrEmpty() && Arguments.Verbose)
            Logger.LogInfo("ditto output: {0}", output);

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            throw new InvalidOperationException($"ditto command failed: {message}");
        }

        Logger.LogInfo("Archive created using ditto (preserves macOS metadata).");
    }

    private void CreateZipWithDotNet(string zipPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var appBundleName = Path.GetFileName(AppBundlePath);
            var tempAppPath = Path.Combine(tempDir, appBundleName);

            // Copy .app bundle to temp directory
            CopyDirectory(AppBundlePath, tempAppPath);

            // Create zip from temp directory
            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.SmallestSize, includeBaseDirectory: false);

            Logger.LogInfo("Archive created using .NET compression.");
        }
        finally
        {
            // Cleanup temp directory
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    #endregion
}