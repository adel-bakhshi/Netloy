using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Text;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Base class for all Linux package builders containing common functionality
/// </summary>
public abstract class LinuxPackageBuilderBase : PackageBuilderBase
{
    #region Constants

    protected const string DesktopFileExtension = ".desktop";
    protected const string MetaInfoFileExtension = ".appdata.xml";

    #endregion Constants

    #region Common Properties

    /// <summary>
    /// usr directory inside package
    /// </summary>
    public string UsrDirectory { get; protected set; } = string.Empty;

    /// <summary>
    /// usr/bin directory (for launcher script)
    /// </summary>
    public string UsrBinDirectory { get; protected set; } = string.Empty;

    /// <summary>
    /// usr/share directory
    /// </summary>
    public string UsrShareDirectory { get; protected set; } = string.Empty;

    /// <summary>
    /// usr/share/applications directory
    /// </summary>
    public string ApplicationsDirectory { get; protected set; } = string.Empty;

    /// <summary>
    /// usr/share/metainfo directory
    /// </summary>
    public string MetaInfoDirectory { get; protected set; } = string.Empty;

    /// <summary>
    /// usr/share/icons directory
    /// </summary>
    public string IconsShareDirectory { get; protected set; } = string.Empty;

    /// <summary>
    /// Pixmaps directory for backward compatibility
    /// </summary>
    public string PixmapsDirectory { get; protected set; } = string.Empty;

    /// <summary>
    /// Desktop file path in usr/share/applications
    /// </summary>
    public string DesktopFilePath { get; protected set; } = string.Empty;

    /// <summary>
    /// MetaInfo file path in usr/share/metainfo
    /// </summary>
    public string MetaInfoFilePath { get; protected set; } = string.Empty;

    /// <summary>
    /// Final package output path
    /// </summary>
    public string OutputPath { get; }

    /// <summary>
    /// Install path for the executable
    /// </summary>
    protected abstract string InstallExec { get; }

    #endregion Common Properties

    #region Constructor

    protected LinuxPackageBuilderBase(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        // Set output path
        OutputPath = Path.Combine(OutputDirectory, OutputName);

        // Set install exec in macros
        MacroExpander.SetMacroValue(MacroId.InstallExec, InstallExec);

        // Change package arch macro for AppImage package
        MacroExpander.SetMacroValue(MacroId.PackageArch, GetLinuxArchitecture());
    }

    #endregion Constructor

    #region Desktop File Operations

    /// <summary>
    /// Copy desktop file and expand macros
    /// </summary>
    protected virtual async Task CopyDesktopFileAsync()
    {
        Logger.LogInfo("Copying desktop file...");

        // Read desktop file content and expand macros
        var desktopContent = await File.ReadAllTextAsync(Configurations.DesktopFile);
        desktopContent = MacroExpander.ExpandMacros(desktopContent);

        // Convert CRLF to LF for Linux
        desktopContent = desktopContent.Replace("\r\n", "\n");

        // Write to applications directory
        await File.WriteAllTextAsync(DesktopFilePath, desktopContent, Constants.Utf8WithoutBom);

        Logger.LogSuccess("Desktop file copied: {0}", Path.GetFileName(DesktopFilePath));
    }

    #endregion Desktop File Operations

    #region MetaInfo File Operations

    /// <summary>
    /// Copy metainfo file and expand macros (optional)
    /// </summary>
    protected virtual async Task CopyMetaInfoFileAsync()
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

        // Convert CRLF to LF for Linux
        metaInfoContent = metaInfoContent.Replace("\r\n", "\n");

        // Write to metainfo directory
        await File.WriteAllTextAsync(MetaInfoFilePath, metaInfoContent, Constants.Utf8WithoutBom);

        Logger.LogSuccess("MetaInfo file copied: {0}", Path.GetFileName(MetaInfoFilePath));
    }

    #endregion MetaInfo File Operations

    #region Icon Operations

    /// <summary>
    /// Copy and organize icons to proper directories
    /// </summary>
    protected virtual void CopyAndOrganizeIcons(bool includePixmaps)
    {
        Logger.LogInfo("Copying and organizing icons...");

        // Get all PNG icons
        var pngIcons = Configurations.IconsCollection
            .Where(ico => Path.GetExtension(ico).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Copy all icons to appropriate size directories
        foreach (var iconPath in pngIcons)
        {
            if (!File.Exists(iconPath))
            {
                Logger.LogWarning("Icon file not found: {0}", iconPath);
                continue;
            }

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
        var svgIcon = Configurations.IconsCollection
            .Find(ico => Path.GetExtension(ico).Equals(".svg", StringComparison.OrdinalIgnoreCase));

        if (!svgIcon.IsStringNullOrEmpty() && File.Exists(svgIcon))
        {
            var targetDir = Path.Combine(IconsShareDirectory, "scalable", "apps");
            var targetPath = Path.Combine(targetDir, $"{Configurations.AppId}.svg");

            Directory.CreateDirectory(targetDir);
            File.Copy(svgIcon, targetPath, true);

            Logger.LogInfo("SVG icon copied to scalable directory");
        }

        if (includePixmaps)
        {
            // Copy the largest icon to pixmaps (for backward compatibility)
            var largestIcon = Configurations.IconsCollection
                .Where(ico => Path.GetExtension(ico).Equals(".png", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ico =>
                {
                    var fileName = Path.GetFileName(ico);
                    if (fileName.Contains("1024x1024"))
                        return 1024;

                    if (fileName.Contains("512x512"))
                        return 512;

                    if (fileName.Contains("256x256"))
                        return 256;

                    return 0;
                })
                .FirstOrDefault();

            if (!largestIcon.IsStringNullOrEmpty() && File.Exists(largestIcon))
            {
                var pixmapPath = Path.Combine(PixmapsDirectory, $"{Configurations.AppId}.png");
                File.Copy(largestIcon, pixmapPath, true);
                Logger.LogInfo("Icon copied to pixmaps directory");
            }
        }

        Logger.LogSuccess("Icons organized successfully!");
    }

    /// <summary>
    /// Determine icon size from filename
    /// </summary>
    protected static string DetermineIconSize(string iconPath)
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

    /// <summary>
    /// Check if required icons exist
    /// </summary>
    protected bool HasRequiredIcons()
    {
        return Arguments.PackageType switch
        {
            PackageType.AppImage => Configurations.IconsCollection.Any(icon => icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    #endregion Icon Operations

    #region Launcher Script Operations

    /// <summary>
    /// Create launcher script in /usr/bin
    /// </summary>
    protected virtual async Task CreateLauncherScriptAsync()
    {
        // Skip if no start command configured
        if (Configurations.StartCommand.IsStringNullOrEmpty())
        {
            Logger.LogInfo("No start command configured. Skipping launcher script creation...");
            return;
        }

        Logger.LogInfo("Creating launcher script...");

        var launcherPath = Path.Combine(UsrBinDirectory, Configurations.StartCommand!);
        var sb = new StringBuilder();

        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"# Launcher script for {Configurations.AppBaseName}");
        sb.AppendLine();
        sb.AppendLine($"exec {InstallExec} \"$@\"");

        var launcherContent = sb.ToString();
        await File.WriteAllTextAsync(launcherPath, launcherContent, Constants.Utf8WithoutBom);

        Logger.LogSuccess("Launcher script created: {0}", launcherPath);
    }

    #endregion Launcher Script Operations

    #region License File Operations

    /// <summary>
    /// Copy license file if exists
    /// </summary>
    protected virtual void CopyLicenseFile()
    {
        // Check if license file is configured
        if (Configurations.AppLicenseFile.IsStringNullOrEmpty())
        {
            Logger.LogInfo("No license file configured. Skipping...");
            return;
        }

        // Check if license file exists
        if (!File.Exists(Configurations.AppLicenseFile))
        {
            Logger.LogWarning("License file not found: {0}. Skipping...", Configurations.AppLicenseFile);
            return;
        }

        Logger.LogInfo("Copying license file...");

        // This method should be overridden by child classes to specify target directory
        var targetPath = GetLicenseTargetPath();
        if (targetPath.IsStringNullOrEmpty())
        {
            Logger.LogWarning("License target path not specified. Skipping license file copy.");
            return;
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!targetDir.IsStringNullOrEmpty())
            Directory.CreateDirectory(targetDir!);

        File.Copy(Configurations.AppLicenseFile, targetPath, true);

        Logger.LogSuccess("License file copied: {0}", Path.GetFileName(targetPath));
    }

    /// <summary>
    /// Get target path for license file (should be overridden by child classes)
    /// </summary>
    protected virtual string GetLicenseTargetPath()
    {
        return string.Empty;
    }

    #endregion License File Operations

    #region File Permissions

    /// <summary>
    /// Execute chmod command to set file permissions
    /// </summary>
    protected async Task ExecuteChmodAsync(string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && Arguments.Verbose)
            {
                var error = await process.StandardError.ReadToEndAsync();
                Logger.LogDebug("chmod warning: {0}", error);
            }
        }
    }

    #endregion File Permissions

    #region Architecture Mapping

    /// <summary>
    /// Map .NET runtime identifier to Linux architecture name
    /// </summary>
    protected string GetLinuxArchitecture()
    {
        var runtime = Arguments.Runtime?.ToLowerInvariant() ?? "linux-x64";

        return Arguments.PackageType switch
        {
            PackageType.Deb => runtime switch
            {
                "linux-x64" => "amd64",
                "linux-arm64" => "arm64",
                "linux-x86" => "i386",
                "linux-arm" => "armhf",
                _ => "amd64"
            },
            PackageType.Rpm => runtime switch
            {
                "linux-x64" => "x86_64",
                "linux-arm64" => "aarch64",
                "linux-x86" => "i686",
                "linux-arm" => "armhfp",
                _ => "x86_64"
            },
            PackageType.Pacman => runtime switch
            {
                "linux-x64" => "x86_64",
                "linux-arm64" => "aarch64",
                "linux-x86" => "i686",
                "linux-arm" => "armv7h",
                _ => "x86_64"
            },
            PackageType.AppImage => runtime switch
            {
                "linux-x64" => "x86_64",
                "linux-arm64" => "arm_aarch64",
                "linux-arm" => "arm",
                "linux-x86" => "i686",
                _ => "x86_64"
            },
            _ => throw new ArgumentException($"Unknown format: {Arguments.PackageType}")
        };
    }

    #endregion Architecture Mapping

    #region Common Initialization

    /// <summary>
    /// Initialize common Linux directory paths
    /// </summary>
    protected void InitializeCommonLinuxDirectories(string baseDirectory)
    {
        // usr directory structure
        UsrDirectory = Path.Combine(baseDirectory, "usr");
        UsrBinDirectory = Path.Combine(UsrDirectory, "bin");
        UsrShareDirectory = Path.Combine(UsrDirectory, "share");

        // Subdirectories under usr/share
        ApplicationsDirectory = Path.Combine(UsrShareDirectory, "applications");
        MetaInfoDirectory = Path.Combine(UsrShareDirectory, "metainfo");
        IconsShareDirectory = Path.Combine(UsrShareDirectory, "icons", "hicolor");

        // Pixmaps directory for backward compatibility
        PixmapsDirectory = Path.Combine(UsrShareDirectory, "pixmaps");

        // File paths
        var desktopFileName = Configurations.AppId + DesktopFileExtension;
        var metaInfoFileName = Configurations.AppId + MetaInfoFileExtension;

        DesktopFilePath = Path.Combine(ApplicationsDirectory, desktopFileName);
        MetaInfoFilePath = Path.Combine(MetaInfoDirectory, metaInfoFileName);
    }

    /// <summary>
    /// Create common Linux directory structure
    /// </summary>
    protected void CreateCommonLinuxDirectories()
    {
        // Create usr directory structure
        Directory.CreateDirectory(UsrDirectory);
        Directory.CreateDirectory(UsrBinDirectory);
        Directory.CreateDirectory(UsrShareDirectory);

        // Create subdirectories under usr/share
        Directory.CreateDirectory(ApplicationsDirectory);
        Directory.CreateDirectory(MetaInfoDirectory);
        Directory.CreateDirectory(IconsShareDirectory);

        // Create pixmaps directory
        Directory.CreateDirectory(PixmapsDirectory);

        // Create icon directories for different sizes
        IconHelper.GetIconSizes()
            .ConvertAll(size => Path.Combine(IconsShareDirectory, size, "apps"))
            .ForEach(dir => Directory.CreateDirectory(dir));
    }

    #endregion Common Initialization
}