using System.Diagnostics;
using System.Text;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for Debian (.deb) packages on Linux
/// </summary>
public class DebPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string ControlFileName = "control";
    private const string DesktopFileExtension = ".desktop";
    private const string MetaInfoFileExtension = ".appdata.xml";

    #endregion

    #region Private Fields

    /// <summary>
    /// Debian package name (lowercase)
    /// </summary>
    private readonly string _debianPackageName;

    /// <summary>
    /// Install path for the executable
    /// </summary>
    private string InstallExec => $"/opt/{Configurations.AppId}/{AppExecName}";

    #endregion

    #region Properties

    /// <summary>
    /// DEBIAN directory (contains control file)
    /// </summary>
    public string DebianDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// opt/{AppId} directory (where application binaries go)
    /// </summary>
    public string OptDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/bin directory inside package (where dotnet publish output goes)
    /// </summary>
    public string PublishOutputDir { get; private set; } = string.Empty;

    /// <summary>
    /// usr directory inside package
    /// </summary>
    public string UsrDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/bin directory (for launcher script)
    /// </summary>
    public string UsrBinDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share directory
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
    /// usr/share/doc/{PackageName} directory
    /// </summary>
    public string DocDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Pixmaps directory for backward compatibility
    /// </summary>
    public string PixmapsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Control file path
    /// </summary>
    public string ControlFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Desktop file path in usr/share/applications
    /// </summary>
    public string DesktopFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// MetaInfo file path in usr/share/metainfo
    /// </summary>
    public string MetaInfoFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Final .deb output path
    /// </summary>
    public string OutputPath { get; }

    #endregion

    public DebPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        // Debian package names must be lowercase
        _debianPackageName = Configurations.PackageName
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-")
            .Trim('-');

        // Initialize directory paths
        InitializeDirectoryPaths();

        // Set output path
        OutputPath = Path.Combine(OutputDirectory, OutputName);

        // Set install exec in macros
        MacroExpander.SetMacroValue(MacroId.InstallExec, InstallExec);
    }

    #region IPackageBuilder Implementation

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting Debian package build...");

        // Create directory structure
        CreateDebianStructure();

        // Publish .NET application to opt directory
        await PublishAsync(PublishOutputDir);

        // Copy desktop file
        await CopyDesktopFileAsync();

        // Copy metainfo file
        await CopyMetaInfoFileAsync();

        // Copy and organize icons
        CopyAndOrganizeIcons();

        // Copy license file if exists
        CopyLicenseFile();

        // Create launcher script in /usr/bin
        await CreateLauncherScriptAsync();

        // Generate control file
        await GenerateControlFileAsync();

        // Set file permissions
        await SetFilePermissionsAsync();

        // Build .deb package
        await BuildDebPackageAsync();

        Logger.LogSuccess("Debian package built successfully! Output: {0}", OutputPath);
    }

    public bool Validate()
    {
        try
        {
            Logger.LogInfo("Validating Debian package build requirements...");

            var errors = new List<string>();

            // Check if dpkg-deb is available
            if (!IsDpkgDebAvailable())
            {
                errors.Add("dpkg-deb not found. Please install dpkg:");
                errors.Add("On Ubuntu/Debian: sudo apt-get install dpkg");
            }

            // Check if desktop file exists
            if (Configurations.DesktopFile.IsStringNullOrEmpty() || !File.Exists(Configurations.DesktopFile))
                errors.Add($"Desktop file not found: {Configurations.DesktopFile}");

            // Check if metainfo file exists (optional but recommended)
            if (!Configurations.MetaFile.IsStringNullOrEmpty() && !File.Exists(Configurations.MetaFile))
                Logger.LogWarning("MetaInfo file not found: {0}. This is optional but recommended for Debian packages.", Configurations.MetaFile);

            // Check if at least one icon exists
            if (Configurations.IconsCollection.Count == 0)
                Logger.LogWarning("No icons configured. It's recommended to provide at least one icon.");

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
        // DEBIAN directory (control files)
        DebianDirectory = Path.Combine(RootDirectory, "DEBIAN");
        ControlFilePath = Path.Combine(DebianDirectory, ControlFileName);

        // opt/{AppId} directory (application binaries)
        OptDirectory = Path.Combine(RootDirectory, "opt", Configurations.AppId);
        PublishOutputDir = OptDirectory;

        // usr directory structure
        UsrDirectory = Path.Combine(RootDirectory, "usr");
        UsrBinDirectory = Path.Combine(UsrDirectory, "bin");
        UsrShareDirectory = Path.Combine(UsrDirectory, "share");

        // Subdirectories under usr/share
        ApplicationsDirectory = Path.Combine(UsrShareDirectory, "applications");
        MetaInfoDirectory = Path.Combine(UsrShareDirectory, "metainfo");
        IconsShareDirectory = Path.Combine(UsrShareDirectory, "icons", "hicolor");
        DocDirectory = Path.Combine(UsrShareDirectory, "doc", _debianPackageName);

        // usr/share/pixmaps (for backward compatibility)
        PixmapsDirectory = Path.Combine(UsrShareDirectory, "pixmaps");

        // File paths
        var desktopFileName = $"{Configurations.AppId}{DesktopFileExtension}";
        var metaInfoFileName = $"{Configurations.AppId}{MetaInfoFileExtension}";

        DesktopFilePath = Path.Combine(ApplicationsDirectory, desktopFileName);
        MetaInfoFilePath = Path.Combine(MetaInfoDirectory, metaInfoFileName);
    }

    private void CreateDebianStructure()
    {
        Logger.LogInfo("Creating Debian package structure...");

        // Create DEBIAN directory
        Directory.CreateDirectory(DebianDirectory);

        // Create opt directory (for application binaries)
        Directory.CreateDirectory(OptDirectory);

        // Create usr directory structure
        Directory.CreateDirectory(UsrDirectory);
        Directory.CreateDirectory(UsrBinDirectory);
        Directory.CreateDirectory(UsrShareDirectory);

        // Create subdirectories under usr/share
        Directory.CreateDirectory(ApplicationsDirectory);
        Directory.CreateDirectory(MetaInfoDirectory);
        Directory.CreateDirectory(IconsShareDirectory);
        Directory.CreateDirectory(DocDirectory);

        // Create pixmaps directory
        Directory.CreateDirectory(PixmapsDirectory);

        // Create icon directories for different sizes
        IconHelper.GetIconSizes()
            .ConvertAll(size => Path.Combine(IconsShareDirectory, size, "apps"))
            .ForEach(dir => Directory.CreateDirectory(dir));

        Logger.LogSuccess("Debian package structure created at: {0}", RootDirectory);
    }

    #endregion

    #region File Operations

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
        var svgIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".svg", StringComparison.OrdinalIgnoreCase));
        if (!svgIcon.IsStringNullOrEmpty() && File.Exists(svgIcon))
        {
            var targetDir = Path.Combine(IconsShareDirectory, "scalable", "apps");
            var targetPath = Path.Combine(targetDir, $"{Configurations.AppId}.svg");

            Directory.CreateDirectory(targetDir);
            File.Copy(svgIcon, targetPath, true);

            Logger.LogInfo("SVG icon copied to scalable directory");
        }

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

    private void CopyLicenseFile()
    {
        if (Configurations.AppLicenseFile.IsStringNullOrEmpty() || !File.Exists(Configurations.AppLicenseFile))
        {
            Logger.LogInfo("License file not provided. Skipping...");
            return;
        }

        Logger.LogInfo("Copying license file...");

        var dest = Path.Combine(DocDirectory, "copyright");
        File.Copy(Configurations.AppLicenseFile, dest, true);

        Logger.LogSuccess("License file copied to: {0}", dest);
    }

    private async Task CreateLauncherScriptAsync()
    {
        if (Configurations.StartCommand.IsStringNullOrEmpty())
        {
            Logger.LogInfo("Start command not configured. Skipping launcher script...");
            return;
        }

        Logger.LogInfo("Creating launcher script...");

        var scriptPath = Path.Combine(UsrBinDirectory, Configurations.StartCommand);
        var scriptContent = $"#!/bin/sh\nexec {InstallExec} \"$@\"";

        await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(false));

        Logger.LogSuccess("Launcher script created: {0}", scriptPath);
    }

    private async Task GenerateControlFileAsync()
    {
        Logger.LogInfo("Generating control file...");

        var sb = new StringBuilder();

        // Required fields
        sb.AppendLine($"Package: {_debianPackageName}");
        sb.AppendLine($"Version: {AppVersion}-{PackageRelease}");
        sb.AppendLine($"Architecture: {GetDebianArch()}");

        // Optional but recommended fields
        sb.AppendLine($"Maintainer: {Configurations.PublisherEmail}");
        sb.AppendLine($"Section: multiverse/{GetDebianSection()}");
        sb.AppendLine("Priority: optional");

        // Calculate installed size (in KB)
        var installedSize = CalculateInstalledSize();
        sb.AppendLine($"Installed-Size: {installedSize}");

        // Homepage
        if (!Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
            sb.AppendLine($"Homepage: {Configurations.PublisherLinkUrl}");

        // Description (required)
        sb.AppendLine($"Description: {Configurations.AppShortSummary}");

        // Extended description
        if (!Configurations.AppDescription.IsStringNullOrEmpty())
        {
            var lines = Configurations.AppDescription
                .Split(['\r', '\n'], StringSplitOptions.None)
                .Select(line => line.Trim())
                .ToList();

            foreach (var line in lines)
            {
                if (!line.IsStringNullOrEmpty())
                {
                    sb.Append(' ');
                    sb.AppendLine(line);
                }
                else
                {
                    sb.AppendLine(" .");
                }
            }
        }

        // Additional metadata
        sb.AppendLine($"License: {Configurations.AppLicenseId}");
        sb.AppendLine($"Vendor: {Configurations.PublisherName}");

        // Dependencies (if configured)
        if (!Configurations.DebianRecommends.IsStringNullOrEmpty())
        {
            // Split by newlines, commas, and semicolons
            var recommends = Configurations.DebianRecommends
                .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            if (recommends.Count > 0)
                sb.AppendLine($"Recommends: {string.Join(", ", recommends)}");
        }

        // Required
        sb.AppendLine();

        await File.WriteAllTextAsync(ControlFilePath, sb.ToString(), Constants.Utf8WithoutBom);

        Logger.LogSuccess("Control file generated: {0}", ControlFilePath);
    }

    private string GetDebianArch()
    {
        // Map .NET runtime to Debian architecture names
        // https://www.debian.org/doc/debian-policy/ch-controlfields.html#s-f-architecture
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "amd64",
            "linux-arm64" => "arm64",
            "linux-x86" => "i386",
            "linux-arm" => "armhf",
            _ => "amd64" // default
        };
    }

    private string GetDebianSection()
    {
        var linuxCategory = MacroExpander.GetMacroValue(MacroId.PrimeCategory);

        // Map Linux Category to Debian Section
        return linuxCategory.ToLowerInvariant() switch
        {
            "audiovideo" => "sound",
            "audio" => "sound",
            "video" => "video",
            "development" => "devel",
            "education" => "education",
            "game" => "games",
            "graphics" => "graphics",
            "network" => "net",
            "office" => "text",
            "science" => "science",
            "settings" => "utils",
            "system" => "admin",
            "utility" => "utils",
            _ => "misc"
        };
    }

    private long CalculateInstalledSize()
    {
        try
        {
            // Calculate total size of all files in RootDirectory (excluding DEBIAN)
            var totalBytes = Directory.GetFiles(RootDirectory, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(DebianDirectory))
                .Sum(f => new FileInfo(f).Length);

            // Convert to KB (rounded up)
            return totalBytes / 1024 + 1;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to calculate installed size: {0}", ex.Message);
            return 1024; // default 1MB
        }
    }

    private async Task SetFilePermissionsAsync()
    {
        Logger.LogInfo("Setting file permissions...");
        try
        {
            // Set permissions for all directories first
            await SetPermissionsForAllDirectoriesAsync();

            // Set permissions for all regular files
            await SetPermissionsForAllFilesAsync();

            // Set executable permissions for specific files
            await SetExecutablePermissionsAsync();

            Logger.LogSuccess("File permissions set!");
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to set some permissions: {0}", ex.Message);
        }
    }

    private async Task SetPermissionsForAllDirectoriesAsync()
    {
        Logger.LogInfo("Setting directory permissions to 755...");

        var directories = Directory.GetDirectories(RootDirectory, "*", SearchOption.AllDirectories)
            .Prepend(RootDirectory) // Include root directory itself
            .ToList();

        foreach (var dir in directories)
        {
            await ExecuteChmodAsync($"755 \"{dir}\"");

            if (Arguments.Verbose)
                Logger.LogDebug("Directory permission set: {0}", dir);
        }
    }

    private async Task SetPermissionsForAllFilesAsync()
    {
        Logger.LogInfo("Setting file permissions to 644...");

        var files = Directory.GetFiles(RootDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(DebianDirectory + Path.DirectorySeparatorChar)) // Skip DEBIAN control files
            .ToList();

        foreach (var file in files)
        {
            await ExecuteChmodAsync($"644 \"{file}\"");

            if (Arguments.Verbose)
                Logger.LogDebug("File permission set: {0}", file);
        }
    }

    private async Task SetExecutablePermissionsAsync()
    {
        Logger.LogInfo("Setting executable permissions...");

        var executableFiles = new List<string>();

        // Main application executable
        if (Directory.Exists(PublishOutputDir))
        {
            var mainExec = Path.Combine(PublishOutputDir, AppExecName);
            if (File.Exists(mainExec))
                executableFiles.Add(mainExec);

            // Find all other executables in publish directory (files without extension or .so files)
            var publishFiles = Directory.GetFiles(PublishOutputDir, "*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return string.IsNullOrEmpty(ext) || ext == ".so";
                })
                .ToList();

            executableFiles.AddRange(publishFiles);
        }

        // Launcher script
        if (!Configurations.StartCommand.IsStringNullOrEmpty())
        {
            var launcherPath = Path.Combine(UsrBinDirectory, Configurations.StartCommand);
            if (File.Exists(launcherPath))
                executableFiles.Add(launcherPath);
        }

        // DEBIAN control scripts (if any)
        var controlScripts = new[] { "preinst", "postinst", "prerm", "postrm" };
        foreach (var script in controlScripts)
        {
            var scriptPath = Path.Combine(DebianDirectory, script);
            if (File.Exists(scriptPath))
                executableFiles.Add(scriptPath);
        }

        // Set 755 permission for all executable files
        foreach (var file in executableFiles.Distinct())
        {
            await ExecuteChmodAsync($"755 \"{file}\"");

            if (Arguments.Verbose)
                Logger.LogDebug("Executable permission set: {0}", file);
        }
    }

    private async Task ExecuteChmodAsync(string arguments)
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

    #endregion

    #region Package Building

    private async Task BuildDebPackageAsync()
    {
        Logger.LogInfo("Building .deb package with dpkg-deb...");

        // Prepare dpkg-deb arguments
        var arguments = new StringBuilder();
        arguments.Append("--root-owner-group");

        if (Arguments.Verbose)
            arguments.Append(" --verbose");

        arguments.Append($" --build \"{RootDirectory}\" \"{OutputPath}\"");

        var processInfo = new ProcessStartInfo
        {
            FileName = "dpkg-deb",
            Arguments = arguments.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start dpkg-deb process.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        // Log output if verbose
        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("dpkg-deb output:\n{0}", output);

        // Check exit code
        if (process.ExitCode != 0)
        {
            var errorMessage = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("dpkg-deb failed:\n{0}", forceLog: true, errorMessage);
            throw new InvalidOperationException($"dpkg-deb failed with exit code {process.ExitCode}");
        }

        Logger.LogSuccess(".deb package built: {0}", Path.GetFileName(OutputPath));
    }

    #endregion

    #region Validation Helpers

    private static bool IsDpkgDebAvailable()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "dpkg-deb",
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

    #endregion
}