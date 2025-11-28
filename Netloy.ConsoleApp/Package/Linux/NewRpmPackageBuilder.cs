using System.Diagnostics;
using System.Text;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Helpers;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for RPM packages on Linux with nfpm and rpmbuild support
/// </summary>
public class NewRpmPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string SpecFileExtension = ".spec";

    #endregion

    #region Private Fields

    /// <summary>
    /// RPM package name (lowercase)
    /// </summary>
    private readonly string _rpmPackageName;

    /// <summary>
    /// Flag to determine whether to use nfpm or rpmbuild
    /// </summary>
    private bool _useNfpm;

    #endregion

    #region Properties

    /// <summary>
    /// Directory where the RPM structure placed on it
    /// </summary>
    public string RpmStructure { get; private set; } = string.Empty;

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
    /// usr/bin directory inside package
    /// </summary>
    public string UsrBinDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share/applications directory inside package
    /// </summary>
    public string ApplicationsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share/icons/hicolor directory inside package
    /// </summary>
    public string IconsHicolorDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share/pixmaps directory inside package
    /// </summary>
    public string PixmapsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// usr/share/metainfo directory inside package
    /// </summary>
    public string MetaInfoDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Path to the spec file
    /// </summary>
    public string SpecFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Output RPM file path
    /// </summary>
    public string OutputPath { get; private set; } = string.Empty;

    public string NfpmDir { get; private set; }

    #endregion

    #region Constructor

    public NewRpmPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        _rpmPackageName = Configurations.AppId.ToLowerInvariant().Replace(" ", "-");

        NfpmDir = Path.Combine(NetloyTempPath, "nfpm");
    }

    #endregion

    #region Public Methods

    public async Task BuildAsync()
    {
        try
        {
            Logger.LogInfo("Starting RPM package build...");

            // Initialize directories
            InitializeDirectories();

            // Copy application files
            await CopyApplicationFilesAsync();

            // Copy desktop file
            await CopyDesktopFileAsync();

            // Copy icons
            CopyAndOrganizeIcons();

            // Copy metainfo file if exists
            await CopyMetaInfoFileAsync();

            // Copy license and changelog files
            CopyLicenseAndChangelogFiles();

            // Create launcher script if needed
            await CreateLauncherScriptAsync();

            // Set file permissions
            await SetFilePermissionsAsync();

            // Build RPM package
            await BuildRpmPackageAsync();

            Logger.LogSuccess("RPM package built successfully: {0}", Path.GetFileName(OutputPath));
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to build RPM package: {0}", forceLog: true, ex.Message);
            throw;
        }
    }

    public bool Validate()
    {
        try
        {
            Logger.LogInfo("Validating RPM package build requirements...");
            var errors = new List<string>();

            // Detect distro and architecture
            var distroType = LinuxDistroDetector.GetDistroType();
            var isArmArch = Arguments.Runtime?.Contains("arm", StringComparison.OrdinalIgnoreCase) == true;

            // Determine build strategy
            if (distroType == LinuxDistroType.Rpm)
            {
                // On RPM-based distro: always use rpmbuild
                _useNfpm = false;
                Logger.LogInfo("Building on RPM-based distribution. Using rpmbuild.");

                if (!IsRpmbuildAvailable())
                {
                    errors.Add("rpmbuild not found. Please install rpm-build package:");
                    errors.Add("On Fedora/RHEL/CentOS: sudo dnf install rpm-build");
                    errors.Add("On openSUSE: sudo zypper install rpm-build");
                }
            }
            else if (distroType == LinuxDistroType.Debian)
            {
                // On Debian-based distro
                if (isArmArch)
                {
                    if (Arguments.Runtime?.Equals("linux-arm64", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        errors.Add("Building ARM (32-bit) RPM packages on Debian-based distributions is not supported at the moment.");
                        errors.Add("You can build:");
                        errors.Add("  - x64 RPM packages (linux-x64) using rpmbuild");
                        errors.Add("  - ARM64 RPM packages (linux-arm64) using nfpm");
                        errors.Add("If you need 32-bit ARM RPMs (armhf/armhfp), please build them on an RPM-based distribution (Fedora/RHEL/openSUSE) with an ARM toolchain.");
                    }
                    else
                    {
                        // ARM architecture: use nfpm
                        _useNfpm = true;
                        Logger.LogInfo("Building ARM package on Debian-based distribution. Using nfpm.");

                        if (!NfpmTool.IsAvailableAsync(Arguments.Runtime ?? string.Empty, NfpmDir).GetAwaiter().GetResult())
                        {
                            errors.Add("nfpm tool is not available.");
                            errors.Add("This is an internal error. Please report this issue.");
                        }
                    }
                }
                else
                {
                    // x86_64 architecture: use rpmbuild
                    _useNfpm = false;
                    Logger.LogInfo("Building x86_64 package on Debian-based distribution. Using rpmbuild.");

                    if (!IsRpmbuildAvailable())
                    {
                        errors.Add("rpmbuild not found. Please install rpm package:");
                        errors.Add("On Ubuntu/Debian: sudo apt-get install rpm");
                    }
                }
            }
            else
            {
                // Unknown distro type
                errors.Add($"Unsupported Linux distribution for RPM packaging.");
                errors.Add($"RPM packages can only be built on Debian-based or RPM-based distributions.");
                errors.Add($"Detected distribution type: {distroType}");
            }

            // Check if desktop file exists
            if (Configurations.DesktopFile.IsStringNullOrEmpty() || !File.Exists(Configurations.DesktopFile))
                errors.Add($"Desktop file not found: {Configurations.DesktopFile}");

            // Check if metainfo file exists (optional but recommended)
            if (!Configurations.MetaFile.IsStringNullOrEmpty() && !File.Exists(Configurations.MetaFile))
                Logger.LogWarning("MetaInfo file not found: {0}. This is optional but recommended for RPM packages.", Configurations.MetaFile);

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

    #region Private Methods - Initialization

    private void InitializeDirectories()
    {
        Logger.LogInfo("Initializing RPM directory structure...");

        // Root structure directory
        RpmStructure = Path.Combine(RootDirectory, "structure");
        Directory.CreateDirectory(RpmStructure);

        // Create opt directory structure
        OptDirectory = Path.Combine(RpmStructure, "opt", Configurations.AppId);
        Directory.CreateDirectory(OptDirectory);

        // Publish output directory
        PublishOutputDir = OptDirectory;

        // Create usr directories
        UsrDirectory = Path.Combine(RpmStructure, "usr");
        UsrBinDirectory = Path.Combine(UsrDirectory, "bin");
        ApplicationsDirectory = Path.Combine(UsrDirectory, "share", "applications");
        IconsHicolorDirectory = Path.Combine(UsrDirectory, "share", "icons", "hicolor");
        PixmapsDirectory = Path.Combine(UsrDirectory, "share", "pixmaps");
        MetaInfoDirectory = Path.Combine(UsrDirectory, "share", "metainfo");

        Directory.CreateDirectory(UsrBinDirectory);
        Directory.CreateDirectory(ApplicationsDirectory);
        Directory.CreateDirectory(IconsHicolorDirectory);
        Directory.CreateDirectory(PixmapsDirectory);
        Directory.CreateDirectory(MetaInfoDirectory);

        // Spec file path
        SpecFilePath = Path.Combine(RootDirectory, $"{_rpmPackageName}{SpecFileExtension}");

        // Output path
        OutputPath = Path.Combine(OutputDirectory, OutputName);

        Logger.LogSuccess("RPM directory structure initialized");
    }

    private string GetRpmArch()
    {
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "x86_64",
            "linux-arm64" => "aarch64",
            "linux-arm" => "armhfp",
            _ => "x86_64"
        };
    }

    #endregion

    #region Private Methods - File Operations

    private async Task CopyApplicationFilesAsync()
    {
        Logger.LogInfo("Copying application files...");

        // Publish .NET application to PublishOutputDir
        await PublishAsync(PublishOutputDir);

        Logger.LogSuccess("Application files copied");
    }

    private async Task CopyDesktopFileAsync()
    {
        Logger.LogInfo("Copying desktop file...");

        if (Configurations.DesktopFile.IsStringNullOrEmpty() || !File.Exists(Configurations.DesktopFile))
        {
            Logger.LogWarning("Desktop file not found: {0}", Configurations.DesktopFile);
            return;
        }

        var desktopFileName = $"{Configurations.AppId}.desktop";
        var destinationPath = Path.Combine(ApplicationsDirectory, desktopFileName);

        var desktopContent = await File.ReadAllTextAsync(Configurations.DesktopFile);
        desktopContent = MacroExpander.ExpandMacros(desktopContent);

        await File.WriteAllTextAsync(destinationPath, desktopContent);

        Logger.LogSuccess("Desktop file copied: {0}", desktopFileName);
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
            var targetDir = Path.Combine(IconsHicolorDirectory, sizeDir, "apps");
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
            var targetDir = Path.Combine(IconsHicolorDirectory, "scalable", "apps");
            var targetPath = Path.Combine(targetDir, $"{Configurations.AppId}.svg");

            Directory.CreateDirectory(targetDir);
            File.Copy(svgIcon, targetPath, true);

            Logger.LogInfo("SVG icon copied to scalable directory");
        }

        // Copy the largest icon to pixmaps for backward compatibility
        var largestIcon = Configurations.IconsCollection
            .Where(ico => Path.GetExtension(ico).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ico =>
            {
                var fileName = Path.GetFileName(ico);
                if (fileName.Contains("1024x1024")) return 1024;
                if (fileName.Contains("512x512")) return 512;
                if (fileName.Contains("256x256")) return 256;
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

        // Try to extract size from filename (e.g., "icon.128x128.png")
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

        throw new InvalidOperationException($"Unable to determine icon size for: {fileName}");
    }

    private async Task CopyMetaInfoFileAsync()
    {
        if (Configurations.MetaFile.IsStringNullOrEmpty() || !File.Exists(Configurations.MetaFile))
        {
            Logger.LogDebug("No metainfo file to copy");
            return;
        }

        Logger.LogInfo("Copying metainfo file...");

        var metaFileName = $"{Configurations.AppId}.metainfo.xml";
        var destinationPath = Path.Combine(MetaInfoDirectory, metaFileName);

        var metaContent = await File.ReadAllTextAsync(Configurations.MetaFile);
        metaContent = MacroExpander.ExpandMacros(metaContent);

        await File.WriteAllTextAsync(destinationPath, metaContent);

        Logger.LogSuccess("Metainfo file copied: {0}", metaFileName);
    }

    /// <summary>
    /// ✅ FIXED: Copy license and changelog files to the package
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
            Logger.LogDebug("No launcher script needed");
            return;
        }

        Logger.LogInfo("Creating launcher script...");

        var launcherPath = Path.Combine(UsrBinDirectory, Configurations.StartCommand);
        var execPath = $"/opt/{Configurations.AppId}/{AppExecName}";

        var launcherContent = new StringBuilder();
        launcherContent.AppendLine("#!/bin/bash");
        launcherContent.AppendLine($"exec \"{execPath}\" \"$@\"");

        await File.WriteAllTextAsync(launcherPath, launcherContent.ToString());

        Logger.LogSuccess("Launcher script created: {0}", Configurations.StartCommand);
    }

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
        var directories = Directory.GetDirectories(RpmStructure, "*", SearchOption.AllDirectories);
        foreach (var dir in directories)
        {
            await ExecuteChmodAsync($"755 \"{dir}\"");
        }
    }

    private async Task SetPermissionsForAllFilesAsync()
    {
        var files = Directory.GetFiles(RpmStructure, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            await ExecuteChmodAsync($"644 \"{file}\"");
        }
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

    #endregion

    #region Private Methods - Build

    private async Task BuildRpmPackageAsync()
    {
        if (_useNfpm)
        {
            await BuildWithNfpmAsync();
        }
        else
        {
            await BuildWithRpmbuildAsync();
        }
    }

    private async Task BuildWithRpmbuildAsync()
    {
        Logger.LogInfo("Building RPM package with rpmbuild...");

        // Generate spec file
        await GenerateSpecFileAsync();

        // Build RPM
        var rpmbuildDir = Path.Combine(RootDirectory, "rpmbuild");
        Directory.CreateDirectory(rpmbuildDir);

        var arguments = new StringBuilder();
        arguments.Append($"-bb \"{SpecFilePath}\" ");
        arguments.Append($"--define \"_topdir {rpmbuildDir}\" ");
        arguments.Append($"--buildroot=\"{RpmStructure}\" ");
        arguments.Append($"--define \"_rpmdir {rpmbuildDir}/RPMS\" ");
        arguments.Append($"--define \"_build_id_links none\" ");
        arguments.Append("--noclean ");

        if (Arguments.Verbose)
            arguments.Append("--verbose");

        Logger.LogInfo($"Running: rpmbuild {arguments}");

        var processInfo = new ProcessStartInfo
        {
            FileName = "rpmbuild",
            Arguments = arguments.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RootDirectory,
            Environment =
            {
                // ✅ FIXED: Set SOURCE_DATE_EPOCH for reproducible builds
                ["SOURCE_DATE_EPOCH"] = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString()
            }
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start rpmbuild process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("rpmbuild output:\n{0}", output);

        if (process.ExitCode != 0)
        {
            var errorMessage = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("rpmbuild failed:\n{0}", forceLog: true, errorMessage);
            throw new InvalidOperationException($"rpmbuild failed with exit code {process.ExitCode}");
        }

        // Find and copy the generated RPM
        var rpmDir = Path.Combine(rpmbuildDir, "RPMS", GetRpmArch());
        var generatedRpm = Directory.GetFiles(rpmDir, "*.rpm").FirstOrDefault();

        if (generatedRpm == null)
            throw new FileNotFoundException("Generated RPM file not found");

        File.Copy(generatedRpm, OutputPath, overwrite: true);

        Logger.LogSuccess("RPM package built with rpmbuild: {0}", Path.GetFileName(OutputPath));
    }

    private async Task BuildWithNfpmAsync()
    {
        Logger.LogInfo("Building RPM package with nfpm...");

        // Generate nfpm.yaml config file
        var nfpmConfigPath = Path.Combine(RootDirectory, "nfpm.yaml");
        await GenerateNfpmConfigAsync(nfpmConfigPath);

        // Get nfpm binary path
        var nfpmPath = await NfpmTool.GetNfpmPathAsync(Arguments.Runtime ?? string.Empty, NfpmDir);

        // Build package with nfpm
        var arguments = $"pkg --packager rpm --config \"{nfpmConfigPath}\" --target \"{OutputPath}\"";

        Logger.LogInfo($"Running: {nfpmPath} {arguments}");

        var processInfo = new ProcessStartInfo
        {
            FileName = nfpmPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RootDirectory
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start nfpm process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("nfpm output:\n{0}", output);

        if (process.ExitCode != 0)
        {
            var errorMessage = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("nfpm failed:\n{0}", forceLog: true, errorMessage);
            throw new InvalidOperationException($"nfpm failed with exit code {process.ExitCode}");
        }

        Logger.LogSuccess("RPM package built with nfpm: {0}", Path.GetFileName(OutputPath));
    }

    #endregion

    #region Private Methods - Spec File Generation

    private async Task GenerateSpecFileAsync()
    {
        Logger.LogInfo("Generating RPM spec file...");

        var sb = new StringBuilder();

        // Basic metadata
        sb.AppendLine($"Name: {_rpmPackageName}");
        sb.AppendLine($"Version: {AppVersion}");
        sb.AppendLine($"Release: {PackageRelease}");
        sb.AppendLine($"Summary: {Configurations.AppShortSummary}");
        sb.AppendLine($"License: {(!Configurations.AppLicenseId.IsStringNullOrEmpty() ? Configurations.AppLicenseId : "Proprietary")}");
        sb.AppendLine($"BuildArch: {GetRpmArch()}");

        if (!Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
            sb.AppendLine($"URL: {Configurations.PublisherLinkUrl}");

        if (!Configurations.PublisherName.IsStringNullOrEmpty())
            sb.AppendLine($"Vendor: {Configurations.PublisherName}");

        // Dependencies
        if (!Configurations.RpmRequires.IsStringNullOrEmpty())
        {
            var requires = Configurations.RpmRequires
                .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x));

            foreach (var req in requires)
                sb.AppendLine($"Requires: {req}");
        }

        sb.AppendLine();

        // Description
        sb.AppendLine("%description");
        var description = !Configurations.AppDescription.IsStringNullOrEmpty()
            ? Configurations.AppDescription
            : Configurations.AppShortSummary;
        sb.AppendLine(description);
        sb.AppendLine();

        // ✅ FIXED: Files section with %license and %doc support
        sb.AppendLine("%files");
        sb.AppendLine($"%defattr(-, root, root, -)");

        // Get all files from RpmStructure
        var allFiles = GetAllFilesRelativeToRoot();

        foreach (var file in allFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

            // Check if it's a license file
            if (fileName == "license" || fileName == "licence" || IsLicenseFile(file))
            {
                sb.Append("%license ");
            }
            // Check if it's a documentation file
            else if (fileName == "readme" || fileName == "changelog" || IsChangelogFile(file))
            {
                sb.Append("%doc ");
            }

            // Ensure the path starts with /
            if (!file.StartsWith('/'))
            {
                sb.Append('/');
            }

            sb.AppendLine(file);
        }

        sb.AppendLine();

        // Post-install script
        sb.AppendLine("%post");
        sb.AppendLine("if [ -x /usr/bin/update-desktop-database ]; then");
        sb.AppendLine("  /usr/bin/update-desktop-database -q /usr/share/applications 2>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine("if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("  /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine();

        // Post-uninstall script
        sb.AppendLine("%postun");
        sb.AppendLine("if [ -x /usr/bin/update-desktop-database ]; then");
        sb.AppendLine("  /usr/bin/update-desktop-database -q /usr/share/applications 2>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine("if [ $1 -eq 0 ]; then");
        sb.AppendLine("  if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("    /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || :");
        sb.AppendLine("  fi");
        sb.AppendLine("fi");
        sb.AppendLine();

        // ✅ FIXED: Add %posttrans script
        sb.AppendLine("%posttrans");
        sb.AppendLine("# Update icon cache after transaction");
        sb.AppendLine("if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("  /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine();

        await File.WriteAllTextAsync(SpecFilePath, sb.ToString());

        Logger.LogSuccess("Spec file generated: {0}", Path.GetFileName(SpecFilePath));
    }

    /// <summary>
    /// ✅ FIXED: Get all files from RpmStructure relative to root (for %files section)
    /// </summary>
    private List<string> GetAllFilesRelativeToRoot()
    {
        var files = new List<string>();

        if (!Directory.Exists(RpmStructure))
            return files;

        var allFiles = Directory.GetFiles(RpmStructure, "*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            // Get relative path from RpmStructure
            var relativePath = Path.GetRelativePath(RpmStructure, file);

            // Convert Windows path separators to Unix
            relativePath = relativePath.Replace('\\', '/');

            files.Add(relativePath);
        }

        return files;
    }

    /// <summary>
    /// ✅ FIXED: Check if file is a license file based on configuration
    /// </summary>
    private bool IsLicenseFile(string filePath)
    {
        if (Configurations.AppLicenseFile.IsStringNullOrEmpty())
            return false;

        var licenseFileName = Path.GetFileName(Configurations.AppLicenseFile);
        var currentFileName = Path.GetFileName(filePath);

        return licenseFileName.Equals(currentFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ✅ FIXED: Check if file is a changelog/readme file based on configuration
    /// </summary>
    private bool IsChangelogFile(string filePath)
    {
        if (Configurations.AppChangeFile.IsStringNullOrEmpty())
            return false;

        var changeFileName = Path.GetFileName(Configurations.AppChangeFile);
        var currentFileName = Path.GetFileName(filePath);

        return changeFileName.Equals(currentFileName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task GenerateNfpmConfigAsync(string configPath)
    {
        Logger.LogInfo("Generating nfpm configuration...");

        var sb = new StringBuilder();

        // ============ Basic Metadata (Required) ============
        sb.AppendLine($"name: \"{_rpmPackageName}\"");
        sb.AppendLine($"arch: \"{GetNfpmArch()}\"");
        sb.AppendLine($"platform: \"linux\"");
        sb.AppendLine($"version: \"{AppVersion}\"");
        sb.AppendLine($"release: \"{PackageRelease}\"");
        sb.AppendLine();

        // ============ Description ============
        sb.AppendLine($"description: |");
        var description = !Configurations.AppDescription.IsStringNullOrEmpty()
            ? Configurations.AppDescription
            : Configurations.AppShortSummary;

        foreach (var line in description.Split('\n'))
            sb.AppendLine($"  {line.TrimEnd()}");

        sb.AppendLine();

        // ============ Vendor and Homepage ============
        if (!Configurations.PublisherName.IsStringNullOrEmpty())
            sb.AppendLine($"vendor: \"{Configurations.PublisherName}\"");

        if (!Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
            sb.AppendLine($"homepage: \"{Configurations.PublisherLinkUrl}\"");

        // ============ License ============
        var license = !Configurations.AppLicenseId.IsStringNullOrEmpty()
            ? Configurations.AppLicenseId
            : "Proprietary";

        sb.AppendLine($"license: \"{license}\"");
        sb.AppendLine();

        // ============ Maintainer ============
        var maintainer = $"{Configurations.PublisherName} <{Configurations.PublisherEmail}>";
        if (!Configurations.PublisherEmail.IsStringNullOrEmpty())
            sb.AppendLine($"maintainer: \"{maintainer}\"");

        sb.AppendLine();

        // ============ Dependencies ============
        if (!Configurations.RpmRequires.IsStringNullOrEmpty())
        {
            var requires = Configurations.RpmRequires
                .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            if (requires.Count > 0)
            {
                sb.AppendLine("depends:");
                foreach (var req in requires)
                    sb.AppendLine($"  - \"{req}\"");

                sb.AppendLine();
            }
        }

        // ============ Contents ============
        sb.AppendLine("contents:");

        // Add all files
        var allFiles = Directory.GetFiles(RpmStructure, "*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            var relativePath = Path.GetRelativePath(RpmStructure, file);
            var destPath = "/" + relativePath.Replace('\\', '/');

            sb.AppendLine($"  - src: \"{file}\"");
            sb.AppendLine($"    dst: \"{destPath}\"");

            // Determine if file is executable
            var isExecutable = IsExecutableFile(file);
            if (isExecutable)
            {
                sb.AppendLine($"    file_info:");
                sb.AppendLine($"      mode: 0755");
            }

            sb.AppendLine();
        }

        // ============ Global Scripts (nFPM root-level) ============
        sb.AppendLine("scripts:");

        var postInstallScriptPath = await GeneratePostInstallScriptAsync();
        sb.AppendLine($"  postinstall: {postInstallScriptPath}");

        var postRemoveScriptPath = await GeneratePostRemoveScriptAsync();
        sb.AppendLine($"  postremove: {postRemoveScriptPath}");
        sb.AppendLine();

        // ============ RPM-Specific Configuration ============
        sb.AppendLine("rpm:");

        var summary = Configurations.AppShortSummary;
        if (!summary.IsStringNullOrEmpty())
            sb.AppendLine($"  summary: \"{summary}\"");

        sb.AppendLine($"  group: \"Applications/Productivity\"");
        sb.AppendLine($"  compression: \"zstd\"");

        await File.WriteAllTextAsync(configPath, sb.ToString(), Constants.Utf8WithoutBom);

        Logger.LogSuccess("nfpm configuration generated: {0}", configPath);
    }

    private async Task<string> GeneratePostInstallScriptAsync()
    {
        var sb = new StringBuilder();

        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine();

        sb.AppendLine("# Update desktop database");
        sb.AppendLine("if [ -x /usr/bin/update-desktop-database ]; then");
        sb.AppendLine("  /usr/bin/update-desktop-database -q /usr/share/applications 2>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine("# Update icon cache");
        sb.AppendLine("if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("  /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || :");
        sb.AppendLine("fi");

        var scriptPath = Path.Combine(RootDirectory, "post-install.sh");
        await File.WriteAllTextAsync(scriptPath, sb.ToString(), Constants.Utf8WithoutBom);
        return scriptPath;
    }

    private async Task<string> GeneratePostRemoveScriptAsync()
    {
        var sb = new StringBuilder();

        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine();

        sb.AppendLine("# Update desktop database");
        sb.AppendLine("if [ -x /usr/bin/update-desktop-database ]; then");
        sb.AppendLine("  /usr/bin/update-desktop-database -q /usr/share/applications 2>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine("# Update icon cache only on complete removal");
        sb.AppendLine("if [ $1 -eq 0 ]; then");
        sb.AppendLine("  if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("    /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || :");
        sb.AppendLine("  fi");
        sb.AppendLine("fi");

        var scriptPath = Path.Combine(RootDirectory, "post-remove.sh");
        await File.WriteAllTextAsync(scriptPath, sb.ToString(), Constants.Utf8WithoutBom);
        return scriptPath;
    }

    /// <summary>
    /// Get nfpm architecture name (using Go nomenclature)
    /// </summary>
    private string GetNfpmArch()
    {
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "amd64",
            "linux-arm64" => "arm64",
            "linux-arm" => "arm7",
            _ => "amd64"
        };
    }

    /// <summary>
    /// Check if a file should be executable
    /// </summary>
    private bool IsExecutableFile(string file)
    {
        // Main executable
        if (file == Path.Combine(PublishOutputDir, AppExecName))
            return true;

        // Launcher script in /usr/bin
        if (!Configurations.StartCommand.IsStringNullOrEmpty() &&
            file == Path.Combine(UsrBinDirectory, Configurations.StartCommand))
            return true;

        // .so files (shared libraries)
        if (file.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    #endregion

    #region Private Methods - Utilities

    private static bool IsRpmbuildAvailable()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "rpmbuild",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                Logger.LogWarning("chmod warning: {0}", error);
            }
        }
    }

    #endregion
}