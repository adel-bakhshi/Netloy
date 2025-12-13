using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Helpers;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Text;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for Arch Linux packages (PKGBUILD/makepkg)
/// </summary>
public class PacmanPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string PkgBuildFileName = "PKGBUILD";

    #endregion Constants

    #region Private Fields

    /// <summary>
    /// Arch package name (lowercase)
    /// </summary>
    private readonly string _archPackageName;

    #endregion Private Fields

    #region Properties

    /// <summary>
    /// Directory where the package structure is placed
    /// </summary>
    public string PackageStructure { get; private set; } = string.Empty;

    /// <summary>
    /// opt/{AppId} directory (where application binaries go)
    /// </summary>
    public string OptDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Directory where dotnet publish output goes
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
    /// Pixmaps directory for backward compatibility
    /// </summary>
    public string PixmapsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Desktop file path in usr/share/applications
    /// </summary>
    public string DesktopFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// MetaInfo file path in usr/share/metainfo
    /// </summary>
    public string MetaInfoFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// PKGBUILD file path
    /// </summary>
    public string PkgBuildFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Final package output path
    /// </summary>
    public string OutputPath { get; }

    /// <summary>
    /// Install path for the executable
    /// </summary>
    public string InstallExec => $"/opt/{Configurations.AppId}/{AppExecName}";

    #endregion Properties

    public PacmanPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        // Arch package names must be lowercase
        _archPackageName = Configurations.PackageName
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
        Logger.LogInfo("Starting Arch Linux package build...");

        // Create directory structure
        CreatePackageStructure();

        // Publish .NET application to opt directory
        await PublishAsync(PublishOutputDir);

        // Copy desktop file
        await CopyDesktopFileAsync();

        // Copy metainfo file
        await CopyMetaInfoFileAsync();

        // Copy and organize icons
        CopyAndOrganizeIcons();

        // Copy license and changelog files
        CopyLicenseAndChangelogFiles();

        // Create launcher script in /usr/bin
        await CreateLauncherScriptAsync();

        // Generate PKGBUILD file
        await GeneratePkgBuildFileAsync();

        // Set file permissions
        await SetFilePermissionsAsync();

        // Build package with makepkg
        await BuildArchPackageAsync();

        Logger.LogSuccess("Arch Linux package built successfully! Output: {0}", OutputPath);
    }

    public bool Validate()
    {
        try
        {
            Logger.LogInfo("Validating Arch Linux package build requirements...");
            var errors = new List<string>();

            // Check if makepkg is available
            if (!IsMakepkgAvailable())
            {
                errors.Add("makepkg not found. Please install base-devel package:");
                errors.Add("On Arch Linux: sudo pacman -S base-devel");
                errors.Add("On Manjaro: sudo pacman -S base-devel");
            }

            // Check if desktop file exists
            if (Configurations.DesktopFile.IsStringNullOrEmpty() || !File.Exists(Configurations.DesktopFile))
                errors.Add($"Desktop file not found: {Configurations.DesktopFile}");

            // Check if metainfo file exists (optional but recommended)
            if (!Configurations.MetaFile.IsStringNullOrEmpty() && !File.Exists(Configurations.MetaFile))
                Logger.LogWarning("MetaInfo file not found: {0}. This is optional but recommended for Arch packages.", Configurations.MetaFile);

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

    #endregion IPackageBuilder Implementation

    #region Directory Structure Creation

    private void InitializeDirectoryPaths()
    {
        // Create package structure directory
        PackageStructure = Path.Combine(RootDirectory, "structure");

        // opt/{AppId} directory (application binaries)
        OptDirectory = Path.Combine(PackageStructure, "opt", Configurations.AppId);
        PublishOutputDir = OptDirectory;

        // usr directory structure
        UsrDirectory = Path.Combine(PackageStructure, "usr");
        UsrBinDirectory = Path.Combine(UsrDirectory, "bin");
        UsrShareDirectory = Path.Combine(UsrDirectory, "share");

        // Subdirectories under usr/share
        ApplicationsDirectory = Path.Combine(UsrShareDirectory, "applications");
        MetaInfoDirectory = Path.Combine(UsrShareDirectory, "metainfo");
        IconsShareDirectory = Path.Combine(UsrShareDirectory, "icons", "hicolor");

        // usr/share/pixmaps (for backward compatibility)
        PixmapsDirectory = Path.Combine(UsrShareDirectory, "pixmaps");

        // File paths
        var desktopFileName = $"{Configurations.AppId}.desktop";
        var metaInfoFileName = $"{Configurations.AppId}.appdata.xml";
        DesktopFilePath = Path.Combine(ApplicationsDirectory, desktopFileName);
        MetaInfoFilePath = Path.Combine(MetaInfoDirectory, metaInfoFileName);

        // PKGBUILD file
        PkgBuildFilePath = Path.Combine(RootDirectory, PkgBuildFileName);
    }

    private void CreatePackageStructure()
    {
        Logger.LogInfo("Creating Arch package structure...");

        // Create package structure directory
        Directory.CreateDirectory(PackageStructure);

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

        // Create pixmaps directory
        Directory.CreateDirectory(PixmapsDirectory);

        // Create icon directories for different sizes
        IconHelper.GetIconSizes()
            .ConvertAll(size => Path.Combine(IconsShareDirectory, size, "apps"))
            .ForEach(dir => Directory.CreateDirectory(dir));

        Logger.LogSuccess("Arch package structure created at: {0}", PackageStructure);
    }

    #endregion Directory Structure Creation

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

    /// <summary>
    /// Copy license and changelog files to the package
    /// </summary>
    private void CopyLicenseAndChangelogFiles()
    {
        // Copy license file if exists
        if (!Configurations.AppLicenseFile.IsStringNullOrEmpty() && File.Exists(Configurations.AppLicenseFile))
        {
            Logger.LogInfo("Copying license file...");
            var licenseFileName = Path.GetFileName(Configurations.AppLicenseFile);
            var licenseDest = Path.Combine(OptDirectory, licenseFileName);
            File.Copy(Configurations.AppLicenseFile, licenseDest, true);
            Logger.LogSuccess("License file copied: {0}", licenseFileName);
        }
        else
        {
            Logger.LogInfo("License file not provided. Skipping...");
        }

        // Copy changelog/readme file if exists
        if (!Configurations.AppChangeFile.IsStringNullOrEmpty() && File.Exists(Configurations.AppChangeFile))
        {
            Logger.LogInfo("Copying changelog file...");
            var changeFileName = Path.GetFileName(Configurations.AppChangeFile);
            var changeDest = Path.Combine(OptDirectory, changeFileName);
            File.Copy(Configurations.AppChangeFile, changeDest, true);
            Logger.LogSuccess("Changelog file copied: {0}", changeFileName);
        }
        else
        {
            Logger.LogInfo("Changelog file not provided. Skipping...");
        }
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

    #endregion File Operations

    #region PKGBUILD Generation

    private async Task GeneratePkgBuildFileAsync()
    {
        Logger.LogInfo("Generating PKGBUILD file...");

        var sb = new StringBuilder();

        // Maintainer comment (optional but common practice)
        sb.AppendLine($"# Maintainer: {Configurations.PublisherName} <{Configurations.PublisherEmail}>");
        sb.AppendLine();

        // Required variables
        sb.AppendLine($"pkgname={_archPackageName}");
        sb.AppendLine($"pkgver={AppVersion.Replace("-", "_")}"); // Arch doesn't allow hyphens in version
        sb.AppendLine($"pkgrel={PackageRelease}");
        sb.AppendLine($"pkgdesc=\"{Configurations.AppShortSummary}\"");
        sb.AppendLine($"arch=('{GetArchArch()}')");
        sb.AppendLine($"url=\"{Configurations.PublisherLinkUrl}\"");
        sb.AppendLine($"license=('{(!Configurations.AppLicenseId.IsStringNullOrEmpty() ? Configurations.AppLicenseId : "custom")}')");

        // Dependencies
        if (!Configurations.ArchDepends.IsStringNullOrEmpty())
        {
            var depends = Configurations.ArchDepends
                .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            if (depends.Count > 0)
                sb.AppendLine($"depends=({string.Join(" ", depends.Select(d => $"'{d}'"))})");
        }

        // Optional dependencies
        if (!Configurations.ArchOptDepends.IsStringNullOrEmpty())
        {
            var optdepends = Configurations.ArchOptDepends
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            if (optdepends.Count > 0)
            {
                sb.AppendLine("optdepends=(");
                foreach (var optdep in optdepends)
                    sb.AppendLine($"  '{optdep}'");

                sb.AppendLine(")");
            }
        }

        // Add options to disable stripping for .NET self-contained apps
        sb.AppendLine("options=('!strip')");
        sb.AppendLine();

        // Source (we're using local files that are already in the pkg directory)
        sb.AppendLine("source=()");
        sb.AppendLine("sha256sums=()");
        sb.AppendLine();

        // Package function
        sb.AppendLine("package() {");
        sb.AppendLine("  # Copy application files to package directory");
        sb.AppendLine("  cp -r \"${startdir}/structure/\"* \"${pkgdir}/\"");
        sb.AppendLine();
        sb.AppendLine("  # Set executable permissions");
        sb.AppendLine($"  chmod +x \"${{pkgdir}}{InstallExec}\"");

        if (!Configurations.StartCommand.IsStringNullOrEmpty())
            sb.AppendLine($"  chmod +x \"${{pkgdir}}/usr/bin/{Configurations.StartCommand}\"");

        sb.AppendLine("}");

        await File.WriteAllTextAsync(PkgBuildFilePath, sb.ToString(), Constants.Utf8WithoutBom);

        Logger.LogSuccess("PKGBUILD file generated: {0}", PkgBuildFilePath);
    }

    private string GetArchArch()
    {
        // Map .NET runtime to Arch architecture names
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "x86_64",
            "linux-arm64" => "aarch64",
            "linux-x86" => "i686",
            "linux-arm" => "armv7h",
            _ => "x86_64" // default
        };
    }

    #endregion PKGBUILD Generation

    #region File Permissions

    private async Task SetFilePermissionsAsync()
    {
        Logger.LogInfo("Setting file permissions...");

        try
        {
            // Set all directories to 755
            await SetPermissionsForAllDirectoriesAsync();

            // Set all regular files to 644
            await SetPermissionsForAllFilesAsync();

            // Set executable permissions for specific files (755)
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
        foreach (var dir in Directory.GetDirectories(PackageStructure, "*", SearchOption.AllDirectories))
            await ExecuteChmodAsync($"755 \"{dir}\"");
    }

    private async Task SetPermissionsForAllFilesAsync()
    {
        foreach (var file in Directory.GetFiles(PackageStructure, "*", SearchOption.AllDirectories))
            await ExecuteChmodAsync($"644 \"{file}\"");
    }

    private async Task SetExecutablePermissionsAsync()
    {
        // Main executable
        var mainExec = Path.Combine(PublishOutputDir, AppExecName);
        if (File.Exists(mainExec))
            await ExecuteChmodAsync($"755 \"{mainExec}\"");

        // Launcher script
        if (!Configurations.StartCommand.IsStringNullOrEmpty())
        {
            var launcherPath = Path.Combine(UsrBinDirectory, Configurations.StartCommand);
            if (File.Exists(launcherPath))
                await ExecuteChmodAsync($"755 \"{launcherPath}\"");
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

    #endregion File Permissions

    #region Package Building

    private async Task BuildArchPackageAsync()
    {
        Logger.LogInfo("Building Arch package with makepkg...");

        // Prepare makepkg arguments
        var arguments = new StringBuilder();
        arguments.Append("--nodeps"); // Don't check dependencies (we're packaging pre-built binaries)
        arguments.Append(" --skipinteg"); // Skip integrity checks (no source downloads)
        arguments.Append(" --skippgpcheck"); // Skip PGP checks
        arguments.Append(" --skipchecksums"); // Skip checksum verification
        arguments.Append(" --ignorearch"); // Skip arch check

        if (Arguments.Verbose)
            arguments.Append(" --nocolor");

        var args = arguments.ToString();
        Logger.LogInfo($"Running: makepkg {args}");

        var processInfo = new ProcessStartInfo
        {
            FileName = "makepkg",
            Arguments = args,
            WorkingDirectory = RootDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                // Set SOURCE_DATE_EPOCH for reproducible builds
                ["SOURCE_DATE_EPOCH"] = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString()
            }
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start makepkg process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        // Log output if verbose
        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("makepkg output:\n{0}", output);

        // Check exit code
        if (process.ExitCode != 0)
        {
            var errorMessage = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("makepkg failed:\n{0}", forceLog: true, errorMessage);
            throw new InvalidOperationException($"makepkg failed with exit code {process.ExitCode}");
        }

        // Move the generated package to the correct output path
        MoveGeneratedPackage();

        Logger.LogSuccess("Arch package built: {0}", Path.GetFileName(OutputPath));
    }

    private void MoveGeneratedPackage()
    {
        // makepkg creates a file like: pkgname-pkgver-pkgrel-arch.pkg.tar.zst
        var packagePattern = $"{_archPackageName}-{AppVersion.Replace("-", "_")}-{PackageRelease}-{GetArchArch()}.pkg.tar.zst";
        var packageFiles = Directory.GetFiles(RootDirectory, packagePattern, SearchOption.TopDirectoryOnly);

        if (packageFiles.Length == 0)
        {
            // Try with wildcard
            packagePattern = $"{_archPackageName}-*.pkg.tar.zst";
            packageFiles = Directory.GetFiles(RootDirectory, packagePattern, SearchOption.TopDirectoryOnly);
        }

        switch (packageFiles.Length)
        {
            case 0:
                throw new FileNotFoundException($"No package file found in: {RootDirectory}");
            case > 1:
                throw new InvalidOperationException($"Multiple package files found in: {RootDirectory}. Expected only one.");
        }

        var sourcePackage = packageFiles[0];
        File.Move(sourcePackage, OutputPath, true);
        Logger.LogInfo("Package moved from {0} to {1}", sourcePackage, OutputPath);
    }

    #endregion Package Building

    #region Validation Helpers

    private static bool IsMakepkgAvailable()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "makepkg",
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

    #endregion Validation Helpers
}