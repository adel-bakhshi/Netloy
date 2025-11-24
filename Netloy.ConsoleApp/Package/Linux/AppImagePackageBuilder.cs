using System.Diagnostics;
using System.Text;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for AppImage format on Linux
/// </summary>
public class AppImagePackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string AppRunFileName = "AppRun";
    private const string DesktopFileExtension = ".desktop";
    private const string MetaInfoFileExtension = ".appdata.xml";

    #endregion

    #region Properties

    /// <summary>
    /// usr directory inside AppDir
    /// </summary>
    public string UsrDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/bin directory inside AppDir (where dotnet publish output goes)
    /// </summary>
    public string PublishOutputDir { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share directory inside AppDir
    /// </summary>
    public string UsrShareDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share/applications directory
    /// </summary>
    public string ApplicationsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share/metainfo directory
    /// </summary>
    public string MetaInfoDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share/icons directory
    /// </summary>
    public string IconsShareDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Desktop file path in AppDir root
    /// </summary>
    public string RootDesktopFile { get; private set; } = string.Empty;

    /// <summary>
    /// Desktop file path in usr/share/applications
    /// </summary>
    public string ShareDesktopFile { get; private set; } = string.Empty;

    /// <summary>
    /// MetaInfo file path in AppDir root
    /// </summary>
    public string RootMetaInfoFile { get; private set; } = string.Empty;

    /// <summary>
    /// MetaInfo file path in usr/share/metainfo
    /// </summary>
    public string ShareMetaInfoFile { get; private set; } = string.Empty;

    /// <summary>
    /// AppRun script path in AppDir root
    /// </summary>
    public string AppRunPath { get; private set; } = string.Empty;

    /// <summary>
    /// Final AppImage output path
    /// </summary>
    public string OutputPath { get; }

    #endregion

    public AppImagePackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        // Initialize directory paths
        InitializeDirectoryPaths();

        // Set output path
        OutputPath = Path.Combine(OutputDirectory, OutputName);

        // Set install exec
        MacroExpander.SetMacroValue(MacroId.InstallExec, $"/usr/bin/{AppExecName}");
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
                errors.Add("appimagetool not found. Please install it from: https://github.com/AppImage/AppImageKit/releases");
                errors.Add("On Ubuntu/Debian: wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage");
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

    public void Clear()
    {
        try
        {
            Logger.LogInfo("Cleaning AppImage build artifacts...");

            // Delete AppDir if exists
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, true);
                Logger.LogInfo("Deleted AppDir: {0}", RootDirectory);
            }

            Logger.LogSuccess("Cleanup completed!");
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to clean build artifacts: {0}", forceLog: true, ex.Message);
            throw;
        }
    }

    #endregion

    #region Directory Structure Creation

    private void InitializeDirectoryPaths()
    {
        // usr directory structure
        UsrDirectory = Path.Combine(RootDirectory, "usr");
        PublishOutputDir = Path.Combine(UsrDirectory, "bin");
        UsrShareDirectory = Path.Combine(UsrDirectory, "share");

        // Subdirectories under usr/share
        ApplicationsDirectory = Path.Combine(UsrShareDirectory, "applications");
        MetaInfoDirectory = Path.Combine(UsrShareDirectory, "metainfo");
        IconsShareDirectory = Path.Combine(UsrShareDirectory, "icons", "hicolor");

        // File paths
        var desktopFileName = $"{Configurations.AppId}{DesktopFileExtension}";
        var metaInfoFileName = $"{Configurations.AppId}{MetaInfoFileExtension}";

        RootDesktopFile = Path.Combine(RootDirectory, desktopFileName);
        ShareDesktopFile = Path.Combine(ApplicationsDirectory, desktopFileName);

        RootMetaInfoFile = Path.Combine(RootDirectory, metaInfoFileName);
        ShareMetaInfoFile = Path.Combine(MetaInfoDirectory, metaInfoFileName);

        AppRunPath = Path.Combine(RootDirectory, AppRunFileName);
    }

    private void CreateAppDirStructure()
    {
        Logger.LogInfo("Creating AppDir structure...");

        // Create usr directory structure
        Directory.CreateDirectory(UsrDirectory);
        Directory.CreateDirectory(PublishOutputDir);
        Directory.CreateDirectory(UsrShareDirectory);

        // Create subdirectories under usr/share
        Directory.CreateDirectory(ApplicationsDirectory);
        Directory.CreateDirectory(MetaInfoDirectory);
        Directory.CreateDirectory(IconsShareDirectory);

        // Create icon directories for different sizes
        foreach (var size in IconHelper.GetIconSizes())
        {
            var sizeDir = Path.Combine(IconsShareDirectory, size, "apps");
            Directory.CreateDirectory(sizeDir);
        }

        Logger.LogSuccess("AppDir structure created at: {0}", RootDirectory);
    }

    #endregion

    #region File Operations

    private async Task CopyDesktopFilesAsync()
    {
        Logger.LogInfo("Copying desktop file...");

        // Read desktop file content and expand macros
        var desktopContent = await File.ReadAllTextAsync(Configurations.DesktopFile);
        desktopContent = MacroExpander.ExpandMacros(desktopContent);

        // Write to both root and share locations (required for AppImage validation)
        await File.WriteAllTextAsync(RootDesktopFile, desktopContent, Encoding.UTF8);
        await File.WriteAllTextAsync(ShareDesktopFile, desktopContent, Encoding.UTF8);

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

        // Read metainfo content and expand macros
        var metaInfoContent = await File.ReadAllTextAsync(Configurations.MetaFile);
        metaInfoContent = MacroExpander.ExpandMacros(metaInfoContent);

        // Write to both root and share locations
        await File.WriteAllTextAsync(RootMetaInfoFile, metaInfoContent, Encoding.UTF8);
        await File.WriteAllTextAsync(ShareMetaInfoFile, metaInfoContent, Encoding.UTF8);

        Logger.LogSuccess("MetaInfo file copied to root and share: {0}", Path.GetFileName(RootMetaInfoFile));
    }

    private void CopyAndOrganizeIcons()
    {
        Logger.LogInfo("Copying and organizing icons...");

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
            // Copy largest icon to root with AppBaseName
            var rootIconPath = Path.Combine(RootDirectory, $"{Configurations.AppBaseName}.png");
            File.Copy(largestIcon, rootIconPath, true);
            Logger.LogInfo("Root icon copied: {0}", Path.GetFileName(rootIconPath));
        }

        // Copy all icons to appropriate size directories in usr/share/icons
        foreach (var iconPath in pngIcons)
        {
            var fileName = Path.GetFileName(iconPath);
            var sizeDir = DetermineIconSize(iconPath);

            var targetDir = Path.Combine(IconsShareDirectory, sizeDir, "apps");
            var targetPath = Path.Combine(targetDir, $"{Configurations.AppBaseName}.png");

            Directory.CreateDirectory(targetDir);
            File.Copy(iconPath, targetPath, true);

            if (Arguments.Verbose)
                Logger.LogDebug("Icon copied to {0}: {1}", sizeDir, fileName);
        }

        // Also copy SVG icons if available
        var svgIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".svg", StringComparison.OrdinalIgnoreCase));
        if (!svgIcon.IsStringNullOrEmpty() && File.Exists(svgIcon))
        {
            var targetDir = Path.Combine(IconsShareDirectory, "scalable", "apps");
            var targetPath = Path.Combine(targetDir, $"{Configurations.AppBaseName}.svg");

            Directory.CreateDirectory(targetDir);
            File.Copy(svgIcon, targetPath, true);

            Logger.LogInfo("SVG icon copied to scalable directory");
        }

        Logger.LogSuccess("Icons organized successfully!");
    }

    private static string DetermineIconSize(string iconPath)
    {
        var fileName = Path.GetFileName(iconPath);

        // Try to extract size from filename (e.g., icon.128x128.png)
        if (fileName.Contains("1024x1024"))
            return "1024x1024";

        if (fileName.Contains("512x512"))
            return "512x512";

        if (fileName.Contains("256x256"))
            return "256x256";

        if (fileName.Contains("128x128"))
            return "128x128";

        if (fileName.Contains("96x96"))
            return "96x96";

        if (fileName.Contains("64x64"))
            return "64x64";

        if (fileName.Contains("48x48"))
            return "48x48";

        if (fileName.Contains("32x32"))
            return "32x32";

        if (fileName.Contains("24x24"))
            return "24x24";

        if (fileName.Contains("16x16"))
            return "16x16";

        throw new InvalidOperationException($"Unable to determine icon size for {fileName}");
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
        await File.WriteAllTextAsync(AppRunPath, appRunContent, new UTF8Encoding(false)); // UTF8 without BOM

        Logger.LogSuccess("AppRun script created: {0}", AppRunPath);
    }

    private string GetArchLibPath()
    {
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "x86_64-linux-gnu",
            "linux-arm64" => "aarch64-linux-gnu",
            "linux-arm" => "arm-linux-gnueabihf",
            "linux-x86" => "i386-linux-gnu",
            _ => "x86_64-linux-gnu" // default fallback
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

    #endregion

    #region AppImage Building

    private async Task BuildAppImageAsync()
    {
        Logger.LogInfo("Building AppImage with appimagetool...");

        var arch = GetAppImageArch();

        // Prepare appimagetool arguments
        var arguments = $"\"{RootDirectory}\" \"{OutputPath}\"";

        if (!Configurations.AppImageArgs.IsStringNullOrEmpty())
            arguments = $"{Configurations.AppImageArgs} " + arguments;

        if (Arguments.Verbose && !Configurations.AppImageArgs.Contains("--verbose") && !Configurations.AppImageArgs.Contains("-v"))
            arguments = "--verbose " + arguments;

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

    private string GetAppImageArch()
    {
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "x86_64",
            "linux-x86" => "i686",
            "linux-arm64" => "arm_aarch64",
            "linux-arm" => "arm",
            _ => throw new InvalidOperationException($"Unsupported runtime for AppImage: {Arguments.Runtime}")
        };
    }

    #endregion

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

    private bool HasRequiredIcons()
    {
        return Configurations.IconsCollection.Any(icon => icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}