using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Text;

namespace Netloy.ConsoleApp.Package.Linux;

/// <summary>
/// Package builder for Arch Linux packages (PKGBUILD/makepkg)
/// </summary>
public class PacmanPackageBuilder : LinuxPackageBuilderBase, IPackageBuilder
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
    /// PKGBUILD file path
    /// </summary>
    public string PkgBuildFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Final package output path
    /// </summary>
    public string OutputPath { get; }

    protected override string InstallExec => $"/opt/{Configurations.AppId}/{AppExecName}";

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
        CopyAndOrganizeIcons(includePixmaps: true);

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

        // Initialize common linux directories
        InitializeCommonLinuxDirectories(PackageStructure);

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

        // Create common linux directories
        CreateCommonLinuxDirectories();

        Logger.LogSuccess("Arch package structure created at: {0}", PackageStructure);
    }

    #endregion Directory Structure Creation

    #region File Operations

    /// <summary>
    /// Copy license and changelog files to the package
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
        sb.AppendLine($"arch=('{GetLinuxArchitecture("arch")}')");
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
        Logger.LogInfo("Searching for generated Arch package...");

        // Search for all .pkg.tar.* files (zst, xz, gz, etc.)
        // Exclude signature files
        var packageFiles = Directory.GetFiles(RootDirectory, "*.pkg.tar.*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        switch (packageFiles.Length)
        {
            case 0:
            {
                throw new FileNotFoundException($"No Arch package file found in: {RootDirectory}\n" +
                    $"Expected pattern: {_archPackageName}-*.pkg.tar.*");
            }

            case > 1:
            {
                var fileList = string.Join("\n  - ", packageFiles.Select(Path.GetFileName));
                throw new InvalidOperationException($"Multiple Arch package files found in: {RootDirectory}\n" +
                    $"Found files:\n  - {fileList}\n" +
                    "Expected only one package file. Please clean the build directory.");
            }

            default:
            {
                var sourcePackage = packageFiles[0];
                Logger.LogInfo("Found package: {0}", Path.GetFileName(sourcePackage));
                File.Move(sourcePackage, OutputPath, true);
                Logger.LogSuccess("Package moved to: {0}", OutputPath);
                break;
            }
        }
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