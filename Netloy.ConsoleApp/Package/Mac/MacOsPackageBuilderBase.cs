using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Text;

namespace Netloy.ConsoleApp.Package.Mac;

/// <summary>
/// Base class for macOS package builders (.app and .dmg)
/// </summary>
public abstract class MacOsPackageBuilderBase : PackageBuilderBase
{
    #region Constants

    private const string InfoPlistFileName = "Info.plist";
    private const string PkgInfoFileName = "PkgInfo";

    #endregion Constants

    #region Properties

    public string PublishOutputDir { get; protected set; }
    public string AppBundlePath { get; protected set; }
    public string ContentsDirectory { get; protected set; }
    public string MacOsDirectory { get; protected set; }
    public string ResourcesDirectory { get; protected set; }
    public string InfoPlistPath { get; protected set; }

    #endregion Properties

    protected MacOsPackageBuilderBase(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        var appBundleName = $"{Configurations.AppFriendlyName}.app";
        AppBundlePath = Path.Combine(RootDirectory, appBundleName);
        ContentsDirectory = Path.Combine(AppBundlePath, "Contents");
        MacOsDirectory = Path.Combine(ContentsDirectory, "MacOS");
        ResourcesDirectory = Path.Combine(ContentsDirectory, "Resources");
        InfoPlistPath = Path.Combine(ContentsDirectory, InfoPlistFileName);
        PublishOutputDir = MacOsDirectory;
    }

    #region App Bundle Structure

    protected void CreateAppBundleStructure()
    {
        // Clean previous build
        if (Directory.Exists(AppBundlePath))
            Directory.Delete(AppBundlePath, true);

        // Create directory structure
        Directory.CreateDirectory(AppBundlePath);
        Directory.CreateDirectory(ContentsDirectory);
        Directory.CreateDirectory(MacOsDirectory);
        Directory.CreateDirectory(ResourcesDirectory);

        Logger.LogInfo("App bundle structure created at: {0}", AppBundlePath);
    }

    #endregion App Bundle Structure

    #region File Operations

    protected void CopyApplicationIcon()
    {
        var icnsIcon = MacroExpander.GetMacroValue(MacroId.PrimaryIconFilePath);
        var iconFileName = $"{Configurations.AppBaseName}.icns";
        var destPath = Path.Combine(ResourcesDirectory, iconFileName);

        File.Copy(icnsIcon, destPath, overwrite: true);
        Logger.LogInfo("Icon copied to Resources directory: {0}", iconFileName);
    }

    protected async Task ProcessInfoPlistAsync()
    {
        // Read user-provided Info.plist and expand macros
        Logger.LogInfo("Reading Info.plist content from: {0}", Configurations.MacOsInfoPlist);
        var rawContent = await File.ReadAllTextAsync(Configurations.MacOsInfoPlist);
        var plistContent = MacroExpander.ExpandMacros(rawContent);

        // Write to Info.plist
        await File.WriteAllTextAsync(InfoPlistPath, plistContent, Constants.Utf8WithoutBom);
        Logger.LogInfo("Info.plist saved at: {0}", InfoPlistPath);
    }

    protected void GeneratePkgInfo()
    {
        var pkgInfoPath = Path.Combine(ContentsDirectory, PkgInfoFileName);
        // PkgInfo contains 8 bytes: 4 for type code (APPL) and 4 for creator code
        File.WriteAllText(pkgInfoPath, "APPL????", Encoding.ASCII);
        Logger.LogInfo("PkgInfo generated at: {0}", pkgInfoPath);
    }

    protected async Task SetExecutablePermissionsAsync()
    {
        // Set executable permission for main binary
        var mainExecutable = Path.Combine(MacOsDirectory, AppExecName);
        if (!File.Exists(mainExecutable))
        {
            Logger.LogWarning("Main executable not found: {0}", mainExecutable);
            return;
        }

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{mainExecutable}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    Logger.LogInfo("Executable permissions set for: {0}", Path.GetFileName(mainExecutable));
                }
                else
                {
                    Logger.LogWarning("Failed to set executable permissions. Exit code: {0}", process.ExitCode);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not set executable permissions: {0}", ex.Message);
        }

        // Set read permissions recursively
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"-R a+rx \"{AppBundlePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    Logger.LogInfo("Read permissions set recursively for: {0}", AppBundlePath);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not set read permissions: {0}", ex.Message);
        }
    }

    #endregion File Operations

    #region Code Signing

    protected async Task SignAppBundleAsync()
    {
        if (Arguments.MacOsSigningIdentity.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Code signing skipped - no signing identity provided.");
            return;
        }

        Logger.LogInfo("Starting code signing process...");

        if (Configurations.MacOsEntitlements.IsStringNullOrEmpty() || !File.Exists(Configurations.MacOsEntitlements))
        {
            Logger.LogWarning("Entitlements file not provided or does not exist. Code signing aborted.");
            return;
        }

        // Sign all files in MacOS directory
        await SignFilesInDirectoryAsync(MacOsDirectory, Configurations.MacOsEntitlements);

        // Sign the app bundle itself
        await SignAppBundleMainAsync(Configurations.MacOsEntitlements);

        // Verify signing
        await VerifyCodeSignatureAsync();

        Logger.LogSuccess("Code signing completed successfully!");
    }

    private async Task SignFilesInDirectoryAsync(string directory, string entitlementsFile)
    {
        Logger.LogInfo("Signing files in directory: {0}", directory);
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            // Skip non-executable files
            if (file.EndsWith(".plist") || file.EndsWith(".icns") || file.EndsWith(".png"))
                continue;

            await SignSingleFileAsync(file, entitlementsFile);
        }
    }

    private async Task SignSingleFileAsync(string filePath, string entitlementsFile)
    {
        Logger.LogInfo("Signing file: {0}", Path.GetFileName(filePath));

        var arguments = $"--force --timestamp --options=runtime --sign \"{Arguments.MacOsSigningIdentity}\"";
        if (!entitlementsFile.IsStringNullOrEmpty() && File.Exists(entitlementsFile))
            arguments += $" --entitlements \"{entitlementsFile}\"";

        arguments += $" \"{filePath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = "codesign",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Logger.LogWarning("Failed to start codesign process for: {0}", filePath);
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogError("Failed to sign {0}: {1}", forceLog: true, Path.GetFileName(filePath), sanitizedMessage);
            throw new InvalidOperationException($"Code signing failed for {filePath}");
        }

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("codesign output: {0}", output);
    }

    private async Task SignAppBundleMainAsync(string entitlementsFile)
    {
        Logger.LogInfo("Signing app bundle: {0}", Path.GetFileName(AppBundlePath));

        var arguments = $"--force --timestamp --options=runtime --sign \"{Arguments.MacOsSigningIdentity}\"";
        if (!entitlementsFile.IsStringNullOrEmpty() && File.Exists(entitlementsFile))
            arguments += $" --entitlements \"{entitlementsFile}\"";

        arguments += $" \"{AppBundlePath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = "codesign",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start codesign process for app bundle.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogError("Failed to sign app bundle: {0}", forceLog: true, sanitizedMessage);
            throw new InvalidOperationException("Code signing failed for app bundle");
        }

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("codesign output: {0}", output);
    }

    private async Task VerifyCodeSignatureAsync()
    {
        Logger.LogInfo("Verifying code signature...");

        var processInfo = new ProcessStartInfo
        {
            FileName = "codesign",
            Arguments = $"--verify --verbose \"{AppBundlePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Logger.LogWarning("Failed to verify code signature.");
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            if (Arguments.Verbose && !output.IsStringNullOrEmpty())
                Logger.LogDebug("codesign output: {0}", output);
            Logger.LogSuccess("Code signature verification passed!");
        }
        else
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogWarning("Code signature verification failed: {0}", forceLog: true, sanitizedMessage);
        }
    }

    #endregion Code Signing

    #region Notarization

    protected async Task NotarizeAppBundleAsync()
    {
        if (Arguments.AppleId.IsStringNullOrEmpty() ||
            Arguments.AppleTeamId.IsStringNullOrEmpty() ||
            Arguments.ApplePassword.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Notarization skipped - Apple ID, Team ID, or Password not provided.");
            return;
        }

        Logger.LogInfo("Starting notarization process...");

        // Create zip file for notarization
        var zipPath = await CreateNotarizationZipAsync();

        // Submit for notarization
        var requestUuid = await SubmitForNotarizationAsync(zipPath);

        // Wait for notarization to complete
        await WaitForNotarizationAsync(requestUuid);

        // Staple the notarization ticket
        await StapleNotarizationAsync();

        Logger.LogSuccess("Notarization completed successfully!");
    }

    private async Task<string> CreateNotarizationZipAsync()
    {
        Logger.LogInfo("Creating zip file for notarization...");
        var zipFileName = $"{Configurations.AppBaseName}_notarization.zip";
        var zipPath = Path.Combine(RootDirectory, zipFileName);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

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

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to create notarization zip.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            throw new InvalidOperationException($"Failed to create notarization zip: {message}");
        }

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("ditto output: {0}", output);

        Logger.LogInfo("Notarization zip created: {0}", zipPath);
        return zipPath;
    }

    private async Task<string> SubmitForNotarizationAsync(string zipPath)
    {
        Logger.LogInfo("Submitting app for notarization...");

        var processInfo = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"notarytool submit \"{zipPath}\" --apple-id \"{Arguments.AppleId}\" --team-id \"{Arguments.AppleTeamId}\" --password \"{Arguments.ApplePassword}\" --wait",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to submit for notarization.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogError("Notarization submission failed: {0}", forceLog: true, sanitizedMessage);
            throw new InvalidOperationException("Notarization submission failed");
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

        Logger.LogInfo("Notarization request submitted. Request ID: {0}", requestUuid);
        return requestUuid;
    }

    private async Task WaitForNotarizationAsync(string requestUuid)
    {
        if (requestUuid.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Waiting for notarization to complete (using --wait flag)...");
            return;
        }

        Logger.LogInfo("Checking notarization status...");

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
            Logger.LogWarning("Could not check notarization status.");
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogWarning("Could not check notarization status: {0}", sanitizedMessage);
            return;
        }

        if (output.Contains("status: Accepted"))
        {
            Logger.LogSuccess("Notarization accepted!");
        }
        else if (output.Contains("status: Invalid"))
        {
            throw new InvalidOperationException("Notarization was rejected. Check Apple's feedback for details.");
        }
    }

    private async Task StapleNotarizationAsync()
    {
        Logger.LogInfo("Stapling notarization ticket to app bundle...");

        var processInfo = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"stapler staple \"{AppBundlePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to staple notarization.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogWarning("Failed to staple notarization: {0}", sanitizedMessage);
            return;
        }

        Logger.LogSuccess("Notarization ticket stapled successfully!");

        // Verify stapling
        await VerifyStaplingAsync();
    }

    private async Task VerifyStaplingAsync()
    {
        Logger.LogInfo("Verifying stapled notarization...");

        var processInfo = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"stapler validate \"{AppBundlePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Logger.LogWarning("Could not verify stapled notarization.");
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            if (Arguments.Verbose && !output.IsStringNullOrEmpty())
                Logger.LogDebug("Stapling verification output: {0}", output);
            Logger.LogSuccess("Stapled notarization verified!");
        }
        else
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            var sanitizedMessage = SanitizeCredentials(message);
            Logger.LogWarning("Stapled notarization verification failed: {0}", sanitizedMessage);
        }
    }

    #endregion Notarization

    #region Security Helpers

    /// <summary>
    /// Removes sensitive credentials from text before logging to prevent exposure in CI/CD logs
    /// </summary>
    protected string SanitizeCredentials(string text)
    {
        if (text.IsStringNullOrEmpty())
            return text;

        var sanitized = text;

        // Remove Apple ID (email)
        if (!Arguments.AppleId.IsStringNullOrEmpty())
            sanitized = sanitized.Replace(Arguments.AppleId!, "***APPLE_ID***");

        // Remove Team ID
        if (!Arguments.AppleTeamId.IsStringNullOrEmpty())
            sanitized = sanitized.Replace(Arguments.AppleTeamId!, "***TEAM_ID***");

        // Remove Password
        if (!Arguments.ApplePassword.IsStringNullOrEmpty())
            sanitized = sanitized.Replace(Arguments.ApplePassword!, "***PASSWORD***");

        // Remove Signing Identity
        if (!Arguments.MacOsSigningIdentity.IsStringNullOrEmpty())
            sanitized = sanitized.Replace(Arguments.MacOsSigningIdentity!, "***SIGNING_IDENTITY***");

        return sanitized;
    }

    #endregion Security Helpers

    #region Validation

    protected bool ValidateMacOsPackage()
    {
        var errors = new List<string>();

        // Validate icon file
        var icnsIcon = Configurations.IconsCollection.Find(ico =>
            Path.GetExtension(ico).Equals(".icns", StringComparison.OrdinalIgnoreCase));

        if (icnsIcon.IsStringNullOrEmpty())
            errors.Add("No .icns icon file found. macOS package requires an .icns icon file.");

        // Validate Info.plist file
        if (Configurations.MacOsInfoPlist.IsStringNullOrEmpty() || !File.Exists(Configurations.MacOsInfoPlist))
            errors.Add($"Info.plist file not found: {Configurations.MacOsInfoPlist}");

        // Validate Entitlements file
        if (!Configurations.MacOsEntitlements.IsStringNullOrEmpty() && !File.Exists(Configurations.MacOsEntitlements))
            errors.Add($"Entitlements file not found: {Configurations.MacOsEntitlements}");

        if (errors.Count <= 0)
            return true;

        var errorMessage = $"The following errors were found:\n\n{string.Join("\n", errors)}";
        throw new InvalidOperationException(errorMessage);
    }

    #endregion Validation
}