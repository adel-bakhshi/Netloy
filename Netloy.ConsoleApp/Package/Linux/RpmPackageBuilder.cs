using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Text;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for RPM packages on Linux
/// </summary>
public class RpmPackageBuilder : LinuxPackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string SpecFileExtension = ".spec";

    #endregion Constants

    #region Private Fields

    /// <summary>
    /// RPM package name (lowercase)
    /// </summary>
    private readonly string _rpmPackageName;

    #endregion Private Fields

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
    /// Spec file path
    /// </summary>
    public string SpecFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Final RPM output path
    /// </summary>
    public string OutputPath { get; }

    protected override string InstallExec => $"/opt/{Configurations.AppId}/{AppExecName}";

    #endregion Properties

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
        CopyAndOrganizeIcons(includePixmaps: true);

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

    #endregion IPackageBuilder Implementation

    #region Directory Structure Creation

    private void InitializeDirectoryPaths()
    {
        // Create rpm dir structure
        RpmStructure = Path.Combine(RootDirectory, "structure");

        // opt/{AppId} directory (application binaries)
        OptDirectory = Path.Combine(RpmStructure, "opt", Configurations.AppId);
        PublishOutputDir = OptDirectory;

        // Initialize common linux directories
        InitializeCommonLinuxDirectories(RpmStructure);

        // RPM spec file
        SpecFilePath = Path.Combine(RootDirectory, _rpmPackageName + SpecFileExtension);
    }

    private void CreateRpmStructure()
    {
        Logger.LogInfo("Creating RPM package structure...");

        // Create RPM structure directory
        Directory.CreateDirectory(RpmStructure);
        // Create opt directory (for application binaries)
        Directory.CreateDirectory(OptDirectory);

        // Create common linux directories
        CreateCommonLinuxDirectories();

        Logger.LogSuccess("RPM package structure created at: {0}", RpmStructure);
    }

    #endregion Directory Structure Creation

    #region File Operations

    /// <summary>
    /// Copy license and changelog files to the package (similar to PupNet's approach)
    /// </summary>
    private void CopyLicenseAndChangelogFiles()
    {
        // Copy license file
        CopyLicenseFile();

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

    protected override string GetLicenseTargetPath()
    {
        var licenseFileName = Path.GetFileName(Configurations.AppLicenseFile);
        return Path.Combine(OptDirectory, licenseFileName);
    }

    #endregion File Operations

    #region Spec File Generation

    private async Task GenerateSpecFileAsync()
    {
        Logger.LogInfo("Generating RPM spec file...");

        var sb = new StringBuilder();

        // Header section
        sb.AppendLine($"Name: {_rpmPackageName}");
        sb.AppendLine($"Version: {AppVersion}");
        sb.AppendLine($"Release: {PackageRelease}");

        // ARM64 packages couldn't build in Debian x64 if this line exists in the .spec file
        // Remove this line and add --target to the rpmbuild command
        //sb.AppendLine($"BuildArch: {GetRpmArch()}");

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

        string description;
        if (!Configurations.AppDescription.IsStringNullOrEmpty())
        {
            description = Configurations.AppDescription;
        }
        else if (!Configurations.AppShortSummary.IsStringNullOrEmpty())
        {
            description = Configurations.AppShortSummary;
        }
        else
        {
            description = "No description available.";
        }

        // Description section
        sb.AppendLine("%description");
        sb.AppendLine(description);
        sb.AppendLine();

        // Files section
        sb.AppendLine("%files");

        // Get all files from RpmStructure and mark license/doc files appropriately
        foreach (var file in GetAllFilesRelativeToRoot())
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

        foreach (var file in Directory.GetFiles(RpmStructure, "*", SearchOption.AllDirectories))
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

    #endregion Spec File Generation

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
        foreach (var dir in Directory.GetDirectories(RpmStructure, "*", SearchOption.AllDirectories))
            await ExecuteChmodAsync($"755 \"{dir}\"");
    }

    private async Task SetPermissionsForAllFilesAsync()
    {
        foreach (var file in Directory.GetFiles(RpmStructure, "*", SearchOption.AllDirectories))
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

    #endregion File Permissions

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
        arguments.Append($" --target {GetLinuxArchitecture()}");

        if (Arguments.Verbose)
            arguments.Append(" -v");

        var args = arguments.ToString();
        Logger.LogInfo($"Running: rpmbuild {args}");

        var processInfo = new ProcessStartInfo
        {
            FileName = "rpmbuild",
            Arguments = args,
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
        var archDir = Path.Combine(rpmOutputDir, GetLinuxArchitecture());

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

        Logger.LogInfo("RPM moved from {0} to {1}", sourceRpm, OutputPath);
    }

    #endregion Package Building

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

    #endregion Validation Helpers
}