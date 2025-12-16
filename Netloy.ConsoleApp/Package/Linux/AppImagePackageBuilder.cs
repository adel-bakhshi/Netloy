using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for AppImage format on Linux
/// </summary>
public partial class AppImagePackageBuilder : LinuxPackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string AppRunFileName = "AppRun";

    #endregion Constants

    #region Properties

    /// <summary>
    /// usr/bin directory inside AppDir (where dotnet publish output goes)
    /// </summary>
    public string PublishOutputDir { get; private set; } = string.Empty;

    /// <summary>
    /// Desktop file path in AppDir root
    /// </summary>
    public string RootDesktopFile { get; private set; } = string.Empty;

    /// <summary>
    /// MetaInfo file path in AppDir root
    /// </summary>
    public string RootMetaInfoFile { get; private set; } = string.Empty;

    /// <summary>
    /// AppRun script path in AppDir root
    /// </summary>
    public string AppRunPath { get; private set; } = string.Empty;

    protected override string InstallExec => $"/usr/bin/{AppExecName}";

    #endregion Properties

    public AppImagePackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        // Initialize directory paths
        InitializeDirectoryPaths();
    }

    #region IPackageBuilder Implementation

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting AppImage package build...");

        CreateAppDirStructure();

        await PublishAsync(PublishOutputDir);

        await CopyDesktopFilesAsync();

        await CopyMetaInfoFilesAsync();

        CopyAndOrganizeIcons();

        await CreateAppRunScriptAsync();

        await SetExecutablePermissionsAsync();

        await BuildAppImageAsync();

        Logger.LogSuccess("AppImage package built successfully! Output: {0}", OutputPath);
    }

    public bool Validate()
    {
        try
        {
            Logger.LogInfo("Validating AppImage build requirements...");

            var errors = new List<string>();

            // Check if appimagetool is available
            if (!IsAppImageToolAvailable())
            {
                errors.Add("appimagetool not found. Please install it from: https://github.com/AppImage/appimagetool/releases");
                errors.Add("On Ubuntu/Debian: wget https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage");
                errors.Add("Then: chmod +x appimagetool-x86_64.AppImage && sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool");
            }

            // Check if required PNG icons exist
            if (!HasRequiredIcons())
                errors.Add("No PNG icon found in configuration. AppImage requires at least one PNG icon.");

            // Check if desktop file exists
            if (Configurations.DesktopFile.IsStringNullOrEmpty() || !File.Exists(Configurations.DesktopFile))
                errors.Add($"Desktop file not found: {Configurations.DesktopFile}");

            // Check if metainfo file exists (optional but recommended)
            if (!Configurations.MetaFile.IsStringNullOrEmpty() && !File.Exists(Configurations.MetaFile))
                Logger.LogWarning("MetaInfo file not found: {0}. This is optional but recommended for AppImage.", Configurations.MetaFile);

            // Report errors if any
            if (errors.Count > 0)
            {
                var errorMessage = $"The following errors were found:\n\n{string.Join("\n", errors)}";
                Logger.LogError(errorMessage, forceLog: true);
                return false;
            }

            Logger.LogSuccess("Validation passed!");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError("Validation failed: {0}", forceLog: true, ex.Message);
            return false;
        }
    }

    #endregion IPackageBuilder Implementation

    #region Directory Structure Creation

    private void InitializeDirectoryPaths()
    {
        // Initialize common linux directories
        InitializeCommonLinuxDirectories(RootDirectory);

        // usr directory structure
        PublishOutputDir = UsrBinDirectory;

        // File paths
        var desktopFileName = Configurations.AppId + DesktopFileExtension;
        var metaInfoFileName = Configurations.AppId + MetaInfoFileExtension;

        RootDesktopFile = Path.Combine(RootDirectory, desktopFileName);
        RootMetaInfoFile = Path.Combine(RootDirectory, metaInfoFileName);

        AppRunPath = Path.Combine(RootDirectory, AppRunFileName);
    }

    private void CreateAppDirStructure()
    {
        Logger.LogInfo("Creating AppDir structure...");

        // Create common linux directories
        CreateCommonLinuxDirectories();

        Logger.LogSuccess("AppDir structure created at: {0}", RootDirectory);
    }

    #endregion Directory Structure Creation

    #region File Operations

    private async Task CopyDesktopFilesAsync()
    {
        Logger.LogInfo("Copying desktop file...");

        await CopyDesktopFileAsync();

        // Write to both root and share locations (required for AppImage validation)
        File.Copy(DesktopFilePath, RootDesktopFile, true);

        Logger.LogSuccess("Desktop file copied to root and share: {0}", Path.GetFileName(RootDesktopFile));
    }

    private async Task CopyMetaInfoFilesAsync()
    {
        // MetaInfo file is optional
        if (Configurations.MetaFile.IsStringNullOrEmpty() || !File.Exists(Configurations.MetaFile))
        {
            Logger.LogInfo("MetaInfo file not provided. Skipping...");
            return;
        }

        Logger.LogInfo("Copying metainfo file...");

        await CopyMetaInfoFileAsync();

        // Write to both root and share locations
        File.Copy(MetaInfoFilePath, RootMetaInfoFile, true);

        Logger.LogSuccess("MetaInfo file copied to root and share: {0}", Path.GetFileName(RootMetaInfoFile));
    }

    private void CopyAndOrganizeIcons()
    {
        Logger.LogInfo($"Copying and organizing icons for {Arguments.PackageType?.ToString().ToUpperInvariant()}...");

        // Get all PNG icons from IconsDirectory (generated by IconHelper)
        var pngIcons = Configurations.IconsCollection
            .Where(ico => Path.GetExtension(ico).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var largestIcon = pngIcons
            .Select(ico =>
            {
                var fileName = Path.GetFileNameWithoutExtension(ico);
                var sizeDot = fileName.LastIndexOf('.') + 1;
                var sizeSection = fileName.Substring(sizeDot).Split('x');
                var size = int.Parse(sizeSection[0]);

                return new { Size = size, IconPath = ico };
            })
            .OrderByDescending(x => x.Size)
            .FirstOrDefault()?
            .IconPath;

        if (!largestIcon.IsStringNullOrEmpty() && File.Exists(largestIcon))
        {
            // Copy largest icon to root with AppId
            var rootIconPath = Path.Combine(RootDirectory, $"{Configurations.AppId}.png");
            File.Copy(largestIcon, rootIconPath, true);
            Logger.LogInfo("Root icon copied: {0}", Path.GetFileName(rootIconPath));
        }

        CopyAndOrganizeIcons(includePixmaps: false);

        Logger.LogSuccess($"Icons organized successfully for {Arguments.PackageType?.ToString().ToUpperInvariant()}!");
    }

    private async Task CreateAppRunScriptAsync()
    {
        Logger.LogInfo("Creating AppRun script...");

        // Get architecture-specific library path
        var archLibPath = GetArchLibPath();

        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"# AppRun script for {Configurations.AppBaseName}");
        sb.AppendLine();
        sb.AppendLine("# Find the AppImage root directory");
        sb.AppendLine("SELF=$(readlink -f \"$0\")");
        sb.AppendLine("HERE=${SELF%/*}");
        sb.AppendLine();
        sb.AppendLine("# Export environment variables");
        sb.AppendLine("export PATH=\"${HERE}/usr/bin:${PATH}\"");
        sb.AppendLine($"export LD_LIBRARY_PATH=\"${{HERE}}/usr/lib:${{HERE}}/usr/lib/{archLibPath}:${{LD_LIBRARY_PATH}}\"");
        sb.AppendLine();
        sb.AppendLine("# Set XDG directories if not set");
        sb.AppendLine("export XDG_DATA_DIRS=\"${HERE}/usr/share:${XDG_DATA_DIRS:-/usr/local/share:/usr/share}\"");
        sb.AppendLine();
        sb.AppendLine("# Execute the main application");
        sb.AppendLine($"EXEC=\"${{HERE}}/usr/bin/{Configurations.AppBaseName}\"");
        sb.AppendLine();
        sb.AppendLine("if [ -x \"${EXEC}\" ]; then");
        sb.AppendLine("    exec \"${EXEC}\" \"$@\"");
        sb.AppendLine("else");
        sb.AppendLine("    echo \"Error: Cannot execute ${EXEC}\"");
        sb.AppendLine("    exit 1");
        sb.AppendLine("fi");

        // Create a robust AppRun script that handles environment setup
        var appRunContent = sb.ToString();

        // Write AppRun script
        await File.WriteAllTextAsync(AppRunPath, appRunContent, Constants.Utf8WithoutBom);

        Logger.LogSuccess("AppRun script created: {0}", AppRunPath);
    }

    /// <summary>
    /// Get architecture-specific library path for LD_LIBRARY_PATH
    /// </summary>
    private string GetArchLibPath()
    {
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "x86_64-linux-gnu",
            "linux-arm64" => "aarch64-linux-gnu",
            "linux-arm" => "arm-linux-gnueabihf",
            "linux-x86" => "i386-linux-gnu",
            _ => "x86_64-linux-gnu"
        };
    }

    private async Task SetExecutablePermissionsAsync()
    {
        Logger.LogInfo("Setting executable permissions...");

        try
        {
            // Step 1: Give read and execute permissions to ALL files recursively in usr directory
            var usrChmodProcess = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"-R a+rx \"{UsrDirectory}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var usrProcess = Process.Start(usrChmodProcess);
            if (usrProcess != null)
            {
                await usrProcess.WaitForExitAsync();

                if (usrProcess.ExitCode == 0)
                {
                    Logger.LogInfo("Set read/execute permissions for usr directory");
                }
                else
                {
                    var error = await usrProcess.StandardError.ReadToEndAsync();
                    Logger.LogWarning("Failed to set permissions for usr directory: {0}", error);
                }
            }

            // Step 2: Give execute permission to AppRun
            var appRunChmodProcess = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{AppRunPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var appRunProcess = Process.Start(appRunChmodProcess);
            if (appRunProcess != null)
            {
                await appRunProcess.WaitForExitAsync();

                if (appRunProcess.ExitCode == 0)
                {
                    Logger.LogInfo("Set executable permission for AppRun");
                }
                else
                {
                    var error = await appRunProcess.StandardError.ReadToEndAsync();
                    Logger.LogWarning("Failed to set executable permission for AppRun: {0}", error);
                }
            }

            // Step 3: Give execute permission to main executable
            var mainExecPath = Path.Combine(PublishOutputDir, AppExecName);
            if (File.Exists(mainExecPath))
            {
                var execChmodProcess = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{mainExecPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var execProcess = Process.Start(execChmodProcess);
                if (execProcess != null)
                {
                    await execProcess.WaitForExitAsync();

                    if (execProcess.ExitCode == 0)
                    {
                        Logger.LogInfo("Set executable permission for {0}", Configurations.AppBaseName);
                    }
                    else
                    {
                        var error = await execProcess.StandardError.ReadToEndAsync();
                        Logger.LogWarning("Failed to set executable permission for {0}: {1}", Configurations.AppBaseName, error);
                    }
                }
            }
            else
            {
                Logger.LogWarning("Main executable not found: {0}", mainExecPath);
            }

            Logger.LogSuccess("Executable permissions set!");
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to set executable permissions: {0}", forceLog: true, ex.Message);
            throw;
        }
    }

    #endregion File Operations

    #region AppImage Building

    private async Task BuildAppImageAsync()
    {
        Logger.LogInfo("Building AppImage with appimagetool...");

        var arch = GetLinuxArchitecture();

        // Prepare appimagetool arguments
        var arguments = $"\"{RootDirectory}\" \"{OutputPath}\"";

        if (!Configurations.AppImageArgs.IsStringNullOrEmpty())
            arguments = $"{Configurations.AppImageArgs} " + arguments;

        if (Arguments.Verbose && !Configurations.AppImageArgs.Contains("--verbose") && !Configurations.AppImageArgs.Contains("-v"))
            arguments = "--verbose " + arguments;

        var shouldUserExtractAndRun = await ShouldUseExtractAndRunAsync();
        if (shouldUserExtractAndRun)
        {
            // AppImage runtime (type 2) uses FUSE to mount the embedded SquashFS filesystem.
            // Modern Linux distributions (Ubuntu 24.04+, Fedora 40+) have migrated from FUSE2 (libfuse2)
            // to FUSE3 (libfuse3), which causes compatibility issues with older AppImage runtimes.
            // The --appimage-extract-and-run flag bypasses FUSE entirely by extracting the AppImage
            // contents to a temporary directory, running it, and cleaning up afterward.
            // This ensures appimagetool works on all systems without requiring FUSE2 installation.
            arguments = "--appimage-extract-and-run " + arguments;
        }

        Logger.LogInfo($"Running: appimagetool {arguments}");

        var processInfo = new ProcessStartInfo
        {
            FileName = "appimagetool",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                // Set ARCH environment variable (required by appimagetool)
                ["ARCH"] = arch
            }
        };

        // Disable UPDATE_INFORMATION if not needed
        if (!processInfo.Environment.ContainsKey("UPDATE_INFORMATION"))
            processInfo.Environment["NO_APPSTREAM"] = "1"; // Skip AppStream validation if needed

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start appimagetool process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        // Log output if verbose
        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("appimagetool output:\n{0}", output);

        // Check exit code
        if (process.ExitCode != 0)
        {
            var errorMessage = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("appimagetool failed:\n{0}", forceLog: true, errorMessage);
            throw new InvalidOperationException($"appimagetool failed with exit code {process.ExitCode}");
        }

        // Make the AppImage executable
        await SetAppImageExecutableAsync();

        Logger.LogSuccess("AppImage built successfully: {0}", Path.GetFileName(OutputPath));
    }

    private async Task SetAppImageExecutableAsync()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{OutputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    Logger.LogWarning("Failed to make AppImage executable");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not set executable permission on AppImage: {0}", ex.Message);
        }
    }

    #endregion AppImage Building

    #region Validation Helpers

    private static bool IsAppImageToolAvailable()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "appimagetool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ShouldUseExtractAndRunAsync()
    {
        try
        {
            // Check if we're on Ubuntu/Debian with only FUSE3 (known issue)
            var osRelease = await File.ReadAllTextAsync("/etc/os-release");
            if (osRelease.Contains("Ubuntu") || osRelease.Contains("Debian"))
            {
                // Check if fusermount is symlink to fusermount3 (FUSE3 only)
                if (File.Exists("/usr/bin/fusermount"))
                {
                    var fusermountTarget = await GetSymlinkTargetAsync("/usr/bin/fusermount");
                    if (fusermountTarget?.Contains("fusermount3") == true)
                    {
                        Logger.LogInfo("Detected FUSE3-only system. Using extract-and-run mode.");
                        return true;
                    }
                }

                // Check Ubuntu version (24.04+ has FUSE issues)
                var versionId = ExtractVersion(osRelease);
                if (osRelease.Contains("Ubuntu") && versionId >= 24)
                {
                    Logger.LogInfo("Detected Ubuntu {0}+. Using extract-and-run mode.", versionId);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not detect FUSE status: {0}. Using extract-and-run as safe default.", ex.Message);
            return true; // Safe default
        }

        return false;
    }

    private static async Task<string> GetSymlinkTargetAsync(string symlinkPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "readlink",
                Arguments = $"-f \"{symlinkPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return symlinkPath;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return !output.IsStringNullOrEmpty() ? output.Trim() : symlinkPath;
        }
        catch
        {
            return symlinkPath;
        }
    }

    private static int ExtractVersion(string osRelease)
    {
        try
        {
            var versionMatch = ExtractFuseVersionRegex().Match(osRelease);
            return versionMatch.Success ? int.Parse(versionMatch.Groups[1].Value) : 0;
        }
        catch
        {
            return 0;
        }
    }

    [GeneratedRegex(@"VERSION_ID=""(\d+)")]
    private static partial Regex ExtractFuseVersionRegex();

    #endregion Validation Helpers
}