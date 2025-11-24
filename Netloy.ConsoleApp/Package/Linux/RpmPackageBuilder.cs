using System.Diagnostics;
using System.Text;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for RPM packages on Linux
/// </summary>
public class RpmPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string SpecFileExtension = ".spec";

    #endregion

    #region Private Fields

    /// <summary>
    /// RPM package name (lowercase)
    /// </summary>
    private readonly string _rpmPackageName;

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
    /// Spec file path
    /// </summary>
    public string SpecFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Final RPM output path
    /// </summary>
    public string OutputPath { get; }

    /// <summary>
    /// Install path for the executable
    /// </summary>
    public string InstallExec => $"/opt/{Configurations.AppId}/{AppExecName}";

    #endregion

    public RpmPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        // RPM package names must be lowercase
        _rpmPackageName = Configurations.PackageName
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
        Logger.LogInfo("Starting RPM package build...");

        // Create directory structure
        CreateRpmStructure();

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

        // Generate spec file
        await GenerateSpecFileAsync();

        // Set file permissions
        await SetFilePermissionsAsync();

        // Build RPM package
        await BuildRpmPackageAsync();

        Logger.LogSuccess("RPM package built successfully! Output: {0}", OutputPath);
    }

    public bool Validate()
    {
        try
        {
            Logger.LogInfo("Validating RPM package build requirements...");
            var errors = new List<string>();

            // Check if rpmbuild is available
            if (!IsRpmbuildAvailable())
            {
                errors.Add("rpmbuild not found. Please install rpm-build package:");
                errors.Add("On Fedora/RHEL/CentOS: sudo dnf install rpm-build");
                errors.Add("On openSUSE: sudo zypper install rpm-build");
                errors.Add("On Ubuntu/Debian: sudo apt-get install rpm");
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

    public void Clear()
    {
        try
        {
            Logger.LogInfo("Cleaning RPM package build artifacts...");

            // Delete build directory if exists
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, true);
                Logger.LogInfo("Deleted build directory: {0}", RootDirectory);
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
        // Create rpm dir structure
        RpmStructure = Path.Combine(RootDirectory, "structure");

        // opt/{AppId} directory (application binaries)
        OptDirectory = Path.Combine(RpmStructure, "opt", Configurations.AppId);
        PublishOutputDir = OptDirectory;

        // usr directory structure
        UsrDirectory = Path.Combine(RpmStructure, "usr");
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

        // RPM spec file
        SpecFilePath = Path.Combine(RootDirectory, $"{_rpmPackageName}{SpecFileExtension}");
    }

    private void CreateRpmStructure()
    {
        Logger.LogInfo("Creating RPM package structure...");

        // Create RPM structure directory
        Directory.CreateDirectory(RpmStructure);

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

        Logger.LogSuccess("RPM package structure created at: {0}", RpmStructure);
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

    /// <summary>
    /// Copy license and changelog files to the package (similar to PupNet's approach)
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

    #endregion

    #region Spec File Generation

    private async Task GenerateSpecFileAsync()
    {
        Logger.LogInfo("Generating RPM spec file...");

        var sb = new StringBuilder();

        // Header section
        sb.AppendLine($"Name: {_rpmPackageName}");
        sb.AppendLine($"Version: {AppVersion}");
        sb.AppendLine($"Release: {PackageRelease}");
        sb.AppendLine($"BuildArch: {GetRpmArch()}");
        sb.AppendLine($"Summary: {Configurations.AppShortSummary}");
        sb.AppendLine($"License: {(!Configurations.AppLicenseId.IsStringNullOrEmpty() ? Configurations.AppLicenseId : "Proprietary")}");
        sb.AppendLine($"Vendor: {(!Configurations.PublisherName.IsStringNullOrEmpty() ? Configurations.PublisherName : "Unknown")}");

        if (!Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
            sb.AppendLine($"URL: {Configurations.PublisherLinkUrl}");

        sb.AppendLine($"AutoReq: {(Configurations.RpmAutoReq ? "yes" : "no")}");
        sb.AppendLine($"AutoProv: {(Configurations.RpmAutoProv ? "yes" : "no")}");

        // Add dependencies (Requires)
        if (!Configurations.RpmRequires.IsStringNullOrEmpty())
        {
            var requires = Configurations.RpmRequires
                .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            foreach (var req in requires)
                sb.AppendLine($"Requires: {req}");
        }

        sb.AppendLine();

        var description = !Configurations.AppDescription.IsStringNullOrEmpty()
            ? Configurations.AppDescription
            : !Configurations.AppShortSummary.IsStringNullOrEmpty()
                ? Configurations.AppShortSummary
                : "No description available.";

        // Description section
        sb.AppendLine("%description");
        sb.AppendLine(description);
        sb.AppendLine();

        // Files section
        sb.AppendLine("%files");

        // Get all files from RpmStructure and mark license/doc files appropriately
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
        sb.AppendLine("# Update desktop database");
        sb.AppendLine("if [ -x /usr/bin/update-desktop-database ]; then");
        sb.AppendLine("  /usr/bin/update-desktop-database -q /usr/share/applications &>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine("# Update icon cache");
        sb.AppendLine("touch --no-create /usr/share/icons/hicolor &>/dev/null || :");
        sb.AppendLine("if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("  /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor &>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine();

        // Post-uninstall script
        sb.AppendLine("%postun");
        sb.AppendLine("# Update desktop database");
        sb.AppendLine("if [ -x /usr/bin/update-desktop-database ]; then");
        sb.AppendLine("  /usr/bin/update-desktop-database -q /usr/share/applications &>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine("# Update icon cache on package removal");
        sb.AppendLine("if [ $1 -eq 0 ] ; then");
        sb.AppendLine("  touch --no-create /usr/share/icons/hicolor &>/dev/null");
        sb.AppendLine("  if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("    /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor &>/dev/null || :");
        sb.AppendLine("  fi");
        sb.AppendLine("fi");
        sb.AppendLine();

        // Post-transaction script
        sb.AppendLine("%posttrans");
        sb.AppendLine("# Update icon cache after transaction");
        sb.AppendLine("if [ -x /usr/bin/gtk-update-icon-cache ]; then");
        sb.AppendLine("  /usr/bin/gtk-update-icon-cache -q /usr/share/icons/hicolor &>/dev/null || :");
        sb.AppendLine("fi");
        sb.AppendLine();

        await File.WriteAllTextAsync(SpecFilePath, sb.ToString(), Constants.Utf8WithoutBom);

        Logger.LogSuccess("Spec file generated: {0}", SpecFilePath);
    }

    /// <summary>
    /// Get all files from RpmStructure relative to root (for %files section)
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
    /// Check if file is a license file based on configuration
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
    /// Check if file is a changelog/readme file based on configuration
    /// </summary>
    private bool IsChangelogFile(string filePath)
    {
        if (Configurations.AppChangeFile.IsStringNullOrEmpty())
            return false;

        var changeFileName = Path.GetFileName(Configurations.AppChangeFile);
        var currentFileName = Path.GetFileName(filePath);

        return changeFileName.Equals(currentFileName, StringComparison.OrdinalIgnoreCase);
    }

    private string GetRpmArch()
    {
        // Map .NET runtime to RPM architecture names
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "linux-x64" => "x86_64",
            "linux-arm64" => "aarch64",
            "linux-x86" => "i686",
            "linux-arm" => "armhfp",
            _ => "x86_64" // default
        };
    }

    #endregion

    #region File Permissions

    private async Task SetFilePermissionsAsync()
    {
        Logger.LogInfo("Setting file permissions...");

        try
        {
            // Set directory permissions (755)
            await ExecuteChmodAsync($"-R 755 \"{RpmStructure}\"");

            // Set file permissions (644)
            await ExecuteChmodAsync($"-R 644 \"{RpmStructure}\"/*");

            // Set executable permissions for binaries and scripts (755)
            if (Directory.Exists(PublishOutputDir))
            {
                var mainExec = Path.Combine(PublishOutputDir, AppExecName);
                if (File.Exists(mainExec))
                    await ExecuteChmodAsync($"755 \"{mainExec}\"");
            }

            // Set executable permission for launcher script
            if (!Configurations.StartCommand.IsStringNullOrEmpty())
            {
                var launcherPath = Path.Combine(UsrBinDirectory, Configurations.StartCommand);
                if (File.Exists(launcherPath))
                    await ExecuteChmodAsync($"755 \"{launcherPath}\"");
            }

            Logger.LogSuccess("File permissions set!");
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to set some permissions: {0}", ex.Message);
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

    private async Task BuildRpmPackageAsync()
    {
        Logger.LogInfo("Building RPM package with rpmbuild...");

        // Create rpmbuild directory structure
        var rpmbuildDir = Path.Combine(RootDirectory, "rpmbuild");
        Directory.CreateDirectory(rpmbuildDir);

        // rpmbuild creates subdirectories: BUILD, RPMS, SOURCES, SPECS, SRPMS
        var rpmOutputDir = Path.Combine(rpmbuildDir, "RPMS");
        Directory.CreateDirectory(rpmOutputDir);

        // Prepare rpmbuild arguments
        var arguments = new StringBuilder();
        arguments.Append($"-bb \"{SpecFilePath}\"");
        arguments.Append($" --define \"_topdir {rpmbuildDir}\"");
        arguments.Append($" --buildroot=\"{RpmStructure}\"");
        arguments.Append($" --define \"_rpmdir {rpmOutputDir}\"");
        arguments.Append(" --define \"_build_id_links none\"");
        arguments.Append(" --noclean");

        if (Arguments.Verbose)
            arguments.Append(" --verbose");

        var processInfo = new ProcessStartInfo
        {
            FileName = "rpmbuild",
            Arguments = arguments.ToString(),
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

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start rpmbuild process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        // Log output if verbose
        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogDebug("rpmbuild output:\n{0}", output);

        // Check exit code
        if (process.ExitCode != 0)
        {
            var errorMessage = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("rpmbuild failed:\n{0}", forceLog: true, errorMessage);
            throw new InvalidOperationException($"rpmbuild failed with exit code {process.ExitCode}");
        }

        // Move the generated RPM to the correct output path
        MoveGeneratedRpm(rpmOutputDir);

        Logger.LogSuccess("RPM package built: {0}", Path.GetFileName(OutputPath));
    }

    private void MoveGeneratedRpm(string rpmOutputDir)
    {
        // rpmbuild creates a subdirectory by architecture (e.g., RPMS/x86_64/)
        var archDir = Path.Combine(rpmOutputDir, GetRpmArch());

        if (!Directory.Exists(archDir))
        {
            // Try to find any subdirectory (in case architecture naming is different)
            var subDirs = Directory.GetDirectories(rpmOutputDir);
            archDir = subDirs.Length switch
            {
                1 => subDirs[0],
                > 1 => throw new InvalidOperationException($"Multiple architecture directories found in {rpmOutputDir}"),
                _ => throw new DirectoryNotFoundException($"No architecture directory found in {rpmOutputDir}")
            };
        }

        var rpmFiles = Directory.GetFiles(archDir, "*.rpm", SearchOption.TopDirectoryOnly);
        switch (rpmFiles.Length)
        {
            case 0:
                throw new FileNotFoundException($"No RPM file found in: {archDir}");

            case > 1:
                throw new InvalidOperationException($"Multiple RPM files found in: {archDir}. Expected only one.");
        }

        var sourceRpm = rpmFiles[0];
        File.Move(sourceRpm, OutputPath, true);

        Logger.LogInfo("RPM moved from {0} to {1}", Path.GetFileName(sourceRpm), Path.GetFileName(OutputPath));
    }

    #endregion

    #region Validation Helpers

    private static bool IsRpmbuildAvailable()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "rpmbuild",
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