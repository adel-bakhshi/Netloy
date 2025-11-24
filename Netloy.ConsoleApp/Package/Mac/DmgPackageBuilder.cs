using System.Diagnostics;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Mac;

public class DmgPackageBuilder : MacOsPackageBuilderBase, IPackageBuilder
{
    #region Properties

    public string DmgPath { get; }

    #endregion

    public DmgPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        DmgPath = Path.Combine(OutputDirectory, OutputName);
    }

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting macOS DMG package build...", forceLog: true);

        // Create App Bundle structure
        Logger.LogInfo("Creating .app bundle structure...");
        CreateAppBundleStructure();

        // Publish project
        await PublishAsync(PublishOutputDir);

        // Copy icon to Resources directory
        Logger.LogInfo("Copying application icon...");
        CopyApplicationIcon();

        // Generate or copy Info.plist
        Logger.LogInfo("Processing Info.plist...");
        await ProcessInfoPlistAsync();

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

        // Create DMG image
        Logger.LogInfo("Creating DMG image...");
        await CreateDmgImageAsync();

        // Sign DMG (optional)
        if (!Arguments.MacOsSigningIdentity.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Code signing DMG...");
            await SignDmgAsync();
        }

        // Notarize DMG (optional)
        if (!Arguments.AppleId.IsStringNullOrEmpty() &&
            !Arguments.AppleTeamId.IsStringNullOrEmpty() &&
            !Arguments.ApplePassword.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Notarizing DMG...");
            await NotarizeDmgAsync();
        }

        Logger.LogSuccess("macOS DMG package build completed successfully!");
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

    #region DMG Creation

    private async Task CreateDmgImageAsync()
    {
        // Delete existing DMG if exists
        if (File.Exists(DmgPath))
            File.Delete(DmgPath);

        // Create a temporary DMG directory with .app and Applications symlink
        var tempDmgDir = Path.Combine(RootDirectory, "dmg_temp");
        if (Directory.Exists(tempDmgDir))
            Directory.Delete(tempDmgDir, true);

        Directory.CreateDirectory(tempDmgDir);

        try
        {
            // Copy .app bundle to temp directory
            var appBundleName = Path.GetFileName(AppBundlePath);
            var tempAppPath = Path.Combine(tempDmgDir, appBundleName);
            CopyDirectory(AppBundlePath, tempAppPath);

            // Create symlink to /Applications
            await CreateApplicationsSymlinkAsync(tempDmgDir);

            // Create DMG from temp directory
            await CreateDmgFromDirectoryAsync(tempDmgDir);

            Logger.LogInfo("DMG image created at: {0}", DmgPath);
            var fileInfo = new FileInfo(DmgPath);
            Logger.LogInfo("DMG size: {0:F2} MB", fileInfo.Length / (1024.0 * 1024.0));
        }
        finally
        {
            // Cleanup temp directory
            if (Directory.Exists(tempDmgDir))
                Directory.Delete(tempDmgDir, true);
        }
    }

    private static async Task CreateApplicationsSymlinkAsync(string targetDirectory)
    {
        var symlinkPath = Path.Combine(targetDirectory, "Applications");

        var processInfo = new ProcessStartInfo
        {
            FileName = "ln",
            Arguments = $"-s /Applications \"{symlinkPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Logger.LogWarning("Failed to create Applications symlink.");
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            Logger.LogInfo("Applications symlink created.");
        }
        else
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogWarning("Failed to create Applications symlink: {0}", message);
        }
    }

    private async Task CreateDmgFromDirectoryAsync(string sourceDirectory)
    {
        var volumeName = Configurations.AppFriendlyName;

        // Create compressed DMG directly
        var processInfo = new ProcessStartInfo
        {
            FileName = "hdiutil",
            Arguments = $"create \"{DmgPath}\" -volname \"{volumeName}\" -fs HFS+ -srcfolder \"{sourceDirectory}\" -format UDZO",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start hdiutil command.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("hdiutil output: {0}", output);

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("Failed to create DMG: {0}", forceLog: true, message);
            throw new InvalidOperationException($"hdiutil command failed: {message}");
        }

        Logger.LogInfo("DMG created using hdiutil with UDZO compression.");
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

    #region DMG Signing

    private async Task SignDmgAsync()
    {
        Logger.LogInfo("Signing DMG: {0}", Path.GetFileName(DmgPath));

        var processInfo = new ProcessStartInfo
        {
            FileName = "codesign",
            Arguments = $"--sign \"{Arguments.MacOsSigningIdentity}\" \"{DmgPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Logger.LogWarning("Failed to start codesign process for DMG.");
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogWarning("Failed to sign DMG: {0}", sanitizedMessage);
            return;
        }

        Logger.LogSuccess("DMG signed successfully!");

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("codesign output: {0}", output);
    }

    #endregion

    #region DMG Notarization

    private async Task NotarizeDmgAsync()
    {
        Logger.LogInfo("Starting DMG notarization process...");

        // Submit for notarization
        var requestUuid = await SubmitDmgForNotarizationAsync();

        // Wait for notarization to complete
        await WaitForDmgNotarizationAsync(requestUuid);

        // Staple the notarization ticket
        await StapleDmgNotarizationAsync();

        Logger.LogSuccess("DMG notarization completed successfully!");
    }

    private async Task<string> SubmitDmgForNotarizationAsync()
    {
        Logger.LogInfo("Submitting DMG for notarization...");

        var processInfo = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"notarytool submit \"{DmgPath}\" --apple-id \"{Arguments.AppleId}\" --team-id \"{Arguments.AppleTeamId}\" --password \"{Arguments.ApplePassword}\" --wait",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to submit DMG for notarization.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogError("DMG notarization submission failed: {0}", forceLog: true, sanitizedMessage);
            throw new InvalidOperationException("DMG notarization submission failed");
        }

        // Extract request UUID from output
        var lines = output.Split('\n');
        var requestUuid = string.Empty;
        foreach (var line in lines)
        {
            if (!line.Contains("id:"))
                continue;

            var parts = line.Split(':');
            if (parts.Length <= 1)
                continue;

            requestUuid = parts[1].Trim();
            break;
        }

        if (requestUuid.IsStringNullOrEmpty())
        {
            Logger.LogWarning("Could not extract request UUID. Notarization may still be in progress.");
            return string.Empty;
        }

        Logger.LogInfo("DMG notarization request submitted. Request ID: {0}", requestUuid);
        return requestUuid;
    }

    private async Task WaitForDmgNotarizationAsync(string requestUuid)
    {
        if (requestUuid.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Waiting for DMG notarization to complete (using --wait flag)...");
            return;
        }

        Logger.LogInfo("Checking DMG notarization status...");

        var processInfo = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"notarytool info \"{requestUuid}\" --apple-id \"{Arguments.AppleId}\" --team-id \"{Arguments.AppleTeamId}\" --password \"{Arguments.ApplePassword}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Logger.LogWarning("Could not check DMG notarization status.");
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogWarning("Could not check DMG notarization status: {0}", sanitizedMessage);
            return;
        }

        if (output.Contains("status: Accepted"))
        {
            Logger.LogSuccess("DMG notarization accepted!");
        }
        else if (output.Contains("status: Invalid"))
        {
            throw new InvalidOperationException("DMG notarization was rejected. Check Apple's feedback for details.");
        }
    }

    private async Task StapleDmgNotarizationAsync()
    {
        Logger.LogInfo("Stapling notarization ticket to DMG...");

        var processInfo = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"stapler staple \"{DmgPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to staple DMG notarization.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogWarning("Failed to staple DMG notarization: {0}", sanitizedMessage);
            return;
        }

        Logger.LogSuccess("DMG notarization ticket stapled successfully!");
    }

    #endregion
}