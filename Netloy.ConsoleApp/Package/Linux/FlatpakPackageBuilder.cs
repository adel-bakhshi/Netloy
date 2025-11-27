using System.Diagnostics;
using System.Text;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Helpers;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for Flatpak format on Linux
/// </summary>
public class FlatpakPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string DesktopFileExtension = ".desktop";
    private const string MetaInfoFileExtension = ".appdata.xml";
    private const string ManifestFileName = "manifest.yml";

    #endregion

    #region Properties

    /// <summary>
    /// Flatpak build directory
    /// </summary>
    public string BuildDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Flatpak files directory (where app files go)
    /// </summary>
    public string FilesDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Publish output directory inside Flatpak
    /// </summary>
    public string PublishOutputDir { get; private set; } = string.Empty;

    /// <summary>
    /// Desktop file directory
    /// </summary>
    public string DesktopDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Icons directory
    /// </summary>
    public string IconsShareDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// MetaInfo directory
    /// </summary>
    public string MetaInfoDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Flatpak manifest file path
    /// </summary>
    public string ManifestFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Desktop file path
    /// </summary>
    public string DesktopFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// MetaInfo file path
    /// </summary>
    public string MetaInfoFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Final Flatpak output path
    /// </summary>
    public string OutputPath { get; }

    /// <summary>
    /// Flatpak repository directory
    /// </summary>
    public string RepoDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Flatpak state directory
    /// </summary>
    public string StateDirectory { get; private set; } = string.Empty;

    #endregion

    public FlatpakPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        // Initialize directory paths
        InitializeDirectoryPaths();

        // Set output path
        OutputPath = Path.Combine(OutputDirectory, OutputName);

        // Set install exec for desktop file
        MacroExpander.SetMacroValue(MacroId.InstallExec, AppExecName);
    }

    #region IPackageBuilder Implementation

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting Flatpak package build...");

        // Generate AppStream metadata
        await GenerateAppStreamMetadataAsync();

        // Create Flatpak directory structure
        CreateFlatpakStructure();

        // Publish .NET application
        await PublishAsync(PublishOutputDir);

        // Copy desktop and metainfo files
        await CopyDesktopFileAsync();
        await CopyMetaInfoFileAsync();

        // Copy and organize icons
        CopyAndOrganizeIcons();

        // Generate Flatpak manifest
        await GenerateFlatpakManifestAsync();

        // Build Flatpak using flatpak-builder
        await BuildFlatpakAsync();

        // Export to single-file bundle
        await ExportFlatpakBundleAsync();

        Logger.LogSuccess("Flatpak package built successfully! Output: {0}", OutputPath);
    }

    public bool Validate()
    {
        try
        {
            Logger.LogInfo("Validating Flatpak build requirements...");
            var errors = new List<string>();

            // Check if flatpak-builder is available
            if (!IsProcessAvailable("flatpak-builder"))
            {
                errors.Add("flatpak-builder not found. Please install it:");
                errors.Add("On Ubuntu/Debian: sudo apt install flatpak-builder");
                errors.Add("On Fedora: sudo dnf install flatpak-builder");
            }

            // Check if flatpak is available
            if (!IsProcessAvailable("flatpak"))
            {
                errors.Add("flatpak not found. Please install it:");
                errors.Add("Visit: https://flatpak.org/setup/");
            }

            // Check runtime configuration
            if (Configurations.FlatpakPlatformRuntime.IsStringNullOrEmpty())
                errors.Add("FlatpakPlatformRuntime not configured (e.g., org.freedesktop.Platform)");

            if (Configurations.FlatpakPlatformSdk.IsStringNullOrEmpty())
                errors.Add("FlatpakPlatformSdk not configured (e.g., org.freedesktop.Sdk)");

            if (Configurations.FlatpakPlatformVersion.IsStringNullOrEmpty())
                errors.Add("FlatpakPlatformVersion not configured (e.g., 23.08)");

            // Check if desktop file exists
            if (Configurations.DesktopFile.IsStringNullOrEmpty() || !File.Exists(Configurations.DesktopFile))
                errors.Add($"Desktop file not found: {Configurations.DesktopFile}");

            // Check if at least one icon exists
            if (!HasRequiredIcons())
                errors.Add("No icon found in configuration. Flatpak requires at least one icon.");

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

    #endregion

    #region Directory Structure Creation

    private void InitializeDirectoryPaths()
    {
        // Build and repo directories
        BuildDirectory = Path.Combine(RootDirectory, "build");
        RepoDirectory = Path.Combine(RootDirectory, "repo");
        FilesDirectory = Path.Combine(RootDirectory, "files");
        StateDirectory = Path.Combine(RootDirectory, "state");

        // Application directories
        PublishOutputDir = Path.Combine(FilesDirectory, "bin");
        DesktopDirectory = Path.Combine(FilesDirectory, "share", "applications");
        MetaInfoDirectory = Path.Combine(FilesDirectory, "share", "metainfo");
        IconsShareDirectory = Path.Combine(FilesDirectory, "share", "icons", "hicolor");

        // File paths
        var desktopFileName = Configurations.AppId + DesktopFileExtension;
        var metaInfoFileName = Configurations.AppId + MetaInfoFileExtension;

        ManifestFilePath = Path.Combine(RootDirectory, ManifestFileName);
        DesktopFilePath = Path.Combine(DesktopDirectory, desktopFileName);
        MetaInfoFilePath = Path.Combine(MetaInfoDirectory, metaInfoFileName);
    }

    private void CreateFlatpakStructure()
    {
        Logger.LogInfo("Creating Flatpak directory structure...");

        // Create main directories
        Directory.CreateDirectory(BuildDirectory);
        Directory.CreateDirectory(RepoDirectory);
        Directory.CreateDirectory(FilesDirectory);

        // Create application directories
        Directory.CreateDirectory(PublishOutputDir);
        Directory.CreateDirectory(DesktopDirectory);
        Directory.CreateDirectory(MetaInfoDirectory);
        Directory.CreateDirectory(IconsShareDirectory);

        // Create icon directories for different sizes
        foreach (var size in IconHelper.GetIconSizes())
        {
            var sizeDir = Path.Combine(IconsShareDirectory, size, "apps");
            Directory.CreateDirectory(sizeDir);
        }

        Logger.LogSuccess("Flatpak directory structure created at: {0}", RootDirectory);
    }

    #endregion

    #region File Operations

    private async Task GenerateAppStreamMetadataAsync()
    {
        var description = AppStreamMetadataHelper.GenerateDescriptionXml(Configurations.AppDescription);
        MacroExpander.SetMacroValue(MacroId.AppStreamDescriptionXml, description);

        var changelog = await AppStreamMetadataHelper.GenerateChangelogXmlAsync(Configurations.AppChangeFile);
        MacroExpander.SetMacroValue(MacroId.AppStreamChangelogXml, changelog);
    }

    private async Task CopyDesktopFileAsync()
    {
        Logger.LogInfo("Copying desktop file...");

        // Read desktop file content and expand macros
        var desktopContent = await File.ReadAllTextAsync(Configurations.DesktopFile);
        desktopContent = MacroExpander.ExpandMacros(desktopContent);

        // Write to applications directory
        await File.WriteAllTextAsync(DesktopFilePath, desktopContent, Constants.Utf8WithoutBom);

        Logger.LogSuccess("Desktop file copied: {0}", Path.GetFileName(DesktopFilePath));
    }

    private async Task CopyMetaInfoFileAsync()
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

        // Write to metainfo directory
        await File.WriteAllTextAsync(MetaInfoFilePath, metaInfoContent, Constants.Utf8WithoutBom);

        Logger.LogSuccess("MetaInfo file copied: {0}", Path.GetFileName(MetaInfoFilePath));
    }

    private void CopyAndOrganizeIcons()
    {
        Logger.LogInfo("Copying and organizing icons...");

        // Get all PNG icons
        var pngIcons = Configurations.IconsCollection
            .Where(ico => Path.GetExtension(ico).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Copy all PNG icons to appropriate size directories
        foreach (var iconPath in pngIcons)
        {
            var fileName = Path.GetFileName(iconPath);
            var sizeDir = DetermineIconSize(iconPath);
            var targetDir = Path.Combine(IconsShareDirectory, sizeDir, "apps");
            var targetPath = Path.Combine(targetDir, $"{Configurations.AppId}.png");

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
            var targetPath = Path.Combine(targetDir, $"{Configurations.AppId}.svg");

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
        if (fileName.Contains("1024x1024")) return "1024x1024";
        if (fileName.Contains("512x512")) return "512x512";
        if (fileName.Contains("256x256")) return "256x256";
        if (fileName.Contains("128x128")) return "128x128";
        if (fileName.Contains("96x96")) return "96x96";
        if (fileName.Contains("64x64")) return "64x64";
        if (fileName.Contains("48x48")) return "48x48";
        if (fileName.Contains("32x32")) return "32x32";
        if (fileName.Contains("24x24")) return "24x24";
        if (fileName.Contains("16x16")) return "16x16";

        throw new InvalidOperationException($"Unable to determine icon size for {fileName}");
    }

    private async Task GenerateFlatpakManifestAsync()
    {
        Logger.LogInfo("Generating Flatpak manifest...");

        var sb = new StringBuilder();

        // Basic app info
        sb.AppendLine($"app-id: {Configurations.AppId}");
        sb.AppendLine($"runtime: {Configurations.FlatpakPlatformRuntime}");
        sb.AppendLine($"runtime-version: '{Configurations.FlatpakPlatformVersion}'");
        sb.AppendLine($"sdk: {Configurations.FlatpakPlatformSdk}");
        sb.AppendLine($"command: {AppExecName}");

        // Finish args (permissions)
        if (!Configurations.FlatpakFinishArgs.IsStringNullOrEmpty())
        {
            sb.AppendLine("finish-args:");
            var finishArgs = MacroExpander.ExpandMacros(Configurations.FlatpakFinishArgs);
            foreach (var arg in finishArgs.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                sb.AppendLine($"  - {arg.Trim()}");
        }

        // Modules
        sb.AppendLine("modules:");
        sb.AppendLine($"  - name: {Configurations.AppBaseName}");
        sb.AppendLine("    buildsystem: simple");
        sb.AppendLine("    build-commands:");
        sb.AppendLine("      - mkdir -p /app/bin");
        sb.AppendLine("      - cp -rn bin/* /app/bin");
        sb.AppendLine("      - mkdir -p /app/share");
        sb.AppendLine("      - cp -rn share/* /app/share");
        sb.AppendLine("    sources:");
        sb.AppendLine("      - type: dir");
        sb.AppendLine("        path: files");

        var manifestContent = sb.ToString().TrimEnd();
        await File.WriteAllTextAsync(ManifestFilePath, manifestContent, Constants.Utf8WithoutBom);

        Logger.LogSuccess("Flatpak manifest generated: {0}", ManifestFilePath);
    }

    private async Task BuildFlatpakAsync()
    {
        Logger.LogInfo("Building Flatpak package using flatpak-builder...");

        var arch = GetFlatpakArch();
        var extraArgs = Configurations.FlatpakBuilderArgs.IsStringNullOrEmpty()
            ? string.Empty
            : MacroExpander.ExpandMacros(Configurations.FlatpakBuilderArgs);

        if (!Configurations.FlatpakGpgSign.IsStringNullOrEmpty())
        {
            extraArgs += $" --gpg-sign={Configurations.FlatpakGpgSign}";

            if (!Configurations.FlatpakGpgHomedir.IsStringNullOrEmpty())
                extraArgs += $" --gpg-homedir={Configurations.FlatpakGpgHomedir}";
        }

        var arguments = $"{extraArgs} --arch={arch} --repo=\"{RepoDirectory}\" --force-clean \"{BuildDirectory}\" --state-dir \"{StateDirectory}\" \"{ManifestFilePath}\"";
        arguments = arguments.Trim();

        var processInfo = new ProcessStartInfo
        {
            FileName = "flatpak-builder",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Logger.LogInfo("Running: flatpak-builder {0}", arguments);

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start flatpak-builder process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("flatpak-builder output:\n{0}", output);

        if (!error.IsStringNullOrEmpty() && process.ExitCode != 0)
            Logger.LogError("flatpak-builder error:\n{0}", forceLog: true, error);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"flatpak-builder failed with exit code {process.ExitCode}");

        Logger.LogSuccess("Flatpak build completed!");
    }

    private async Task ExportFlatpakBundleAsync()
    {
        Logger.LogInfo("Exporting Flatpak bundle...");

        var arch = GetFlatpakArch();
        var arguments = $"build-bundle \"{RepoDirectory}\" \"{OutputPath}\" {Configurations.AppId} ";

        if (!Configurations.FlatpakRuntimeRepo.IsStringNullOrEmpty())
            arguments += $"--runtime-repo={Configurations.FlatpakRuntimeRepo} ";

        arguments += $"--arch={arch} --branch={Arguments.FlatpakBranch}";
        arguments = arguments.Trim();

        var processInfo = new ProcessStartInfo
        {
            FileName = "flatpak",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Logger.LogInfo("Running: flatpak {0}", arguments);

        using var process = Process.Start(processInfo)
                            ?? throw new InvalidOperationException("Failed to start flatpak process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("flatpak output:\n{0}", output);

        if (!error.IsStringNullOrEmpty() && process.ExitCode != 0)
            Logger.LogError("flatpak error:\n{0}", forceLog: true, error);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"flatpak build-bundle failed with exit code {process.ExitCode}");

        Logger.LogSuccess("Flatpak bundle exported successfully!");
    }

    private string GetFlatpakArch()
    {
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "x86_64",
            "linux-x86" => "i386",
            "linux-arm64" => "aarch64",
            "linux-arm" => "arm",
            _ => throw new InvalidOperationException($"Unsupported runtime for Flatpak: {Arguments.Runtime}")
        };
    }

    #endregion

    #region Validation Helpers

    private static bool IsProcessAvailable(string processName)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "flatpak-builder",
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
            Logger.LogError("Process {0} not found.", processName);
            return false;
        }
    }

    private bool HasRequiredIcons()
    {
        return Configurations.IconsCollection.Any(icon =>
            icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            icon.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}