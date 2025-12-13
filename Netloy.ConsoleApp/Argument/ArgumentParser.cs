using CommandLine;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;
using System.Runtime.InteropServices;
using System.Text;

namespace Netloy.ConsoleApp.Argument;

public class ArgumentParser
{
    #region Private fields

    private readonly string[] _appArgs;
    private Arguments? _arguments;

    #endregion

    #region Properties

    public List<Error>? Errors { get; private set; }

    #endregion

    #region Constructor

    public ArgumentParser(string[] args)
    {
        _appArgs = args;
    }

    #endregion

    #region Public Methods

    public Arguments Parse()
    {
        using var parser = new Parser(config =>
        {
            config.HelpWriter = null;
            config.AutoHelp = false;
            config.AutoVersion = false;
        });

        var parserResult = parser.ParseArguments<Arguments>(_appArgs);

        Logger.CurrentLevel = parserResult.Value.LogLevel;
        Logger.IsVerbose = parserResult.Value.Verbose;

        parserResult
            .WithParsed(HandleArguments)
            .WithNotParsed(err => Errors = err.ToList());

        _arguments = parserResult.Value;
        return _arguments;
    }

    public void ShowVersion()
    {
        if (_arguments == null)
            throw new InvalidOperationException("Arguments is null");

        var sb = new StringBuilder();

        if (_arguments.Verbose)
        {
            sb.AppendLine($"Netloy version {Constants.Version}");
            sb.AppendLine();
            sb.AppendLine("Runtime Information:");
            sb.AppendLine($"  .NET version: {Environment.Version}");
            sb.AppendLine($"  OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"  Architecture: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"  RID: {RuntimeInformation.RuntimeIdentifier}");
            sb.AppendLine();
            sb.AppendLine($"{Constants.Copyright}");
            sb.AppendLine($"{Constants.ProjectLicense} License - {Constants.ProjectUrl}");
        }
        else
        {
            sb.AppendLine($"Netloy version {Constants.Version}");
            sb.AppendLine();
            sb.AppendLine("Use --verbose for more information");
        }

        Console.WriteLine(sb.ToString());
    }

    public void ShowHelps(IEnumerable<Error>? err = null)
    {
        if (_arguments == null)
            throw new InvalidOperationException("Arguments is null");

        var helpText = new StringBuilder();

        // Header
        helpText.AppendLine($"Netloy {Constants.Version} - .NET Application Packaging Tool");
        helpText.AppendLine();

        if (err?.Any() == true)
        {
            helpText.AppendLine("ERROR:");
            helpText.AppendLine($"  Invalid arguments: {string.Join(' ', _appArgs)}");
            helpText.AppendLine();
        }

        if (_arguments.Verbose)
        {
            // Description
            helpText.AppendLine("DESCRIPTION:");
            helpText.AppendLine("  Package and distribute .NET applications for multiple platforms");
            helpText.AppendLine("  with platform-specific installers and formats.");
            helpText.AppendLine();
        }

        // Usage
        helpText.AppendLine("USAGE:");
        helpText.AppendLine("  netloy [OPTIONS]");
        helpText.AppendLine();

        if (_arguments.Verbose)
        {
            // Common Examples First
            helpText.AppendLine("COMMON EXAMPLES:");
            helpText.AppendLine("  # Create new configuration file");
            helpText.AppendLine("  netloy --new conf");
            helpText.AppendLine();
            helpText.AppendLine("  # Build Linux DEB package");
            helpText.AppendLine("  netloy -t deb -r linux-x64");
            helpText.AppendLine();
            helpText.AppendLine("  # Build RPM package with custom name");
            helpText.AppendLine("  netloy -t rpm -r linux-x64 -o MyApp");
            helpText.AppendLine();
            helpText.AppendLine("  # Build macOS app bundle");
            helpText.AppendLine("  netloy -t app-bundle -r osx-x64 --app-version 1.0.0");
            helpText.AppendLine();
            helpText.AppendLine("  # Build AppImage for Linux");
            helpText.AppendLine("  netloy -t app-image -r linux-x64");
            helpText.AppendLine();
            helpText.AppendLine("  # Skip prompts for CI/CD");
            helpText.AppendLine("  netloy -t deb -r linux-x64 -y --clean");
            helpText.AppendLine();
            helpText.AppendLine("  # Build .NET Framework application");
            helpText.AppendLine("  netloy -t exe -r win-x64 -f netframework");
            helpText.AppendLine();
            helpText.AppendLine("  # Build and sign macOS DMG");
            helpText.AppendLine("  netloy -t dmg -r osx-arm64 --signing-identity \"Developer ID\" \\");
            helpText.AppendLine("         --apple-id \"dev@example.com\" --apple-team-id \"ABC1234XYZ\"");
            helpText.AppendLine();
        }

        if (!_arguments.Verbose)
        {
            helpText.AppendLine("PACKAGE OPTIONS:");
            helpText.AppendLine("  -t, --package-type <TYPE>");
            helpText.AppendLine("  -r, --runtime <RID>");
            helpText.AppendLine("  -f, --framework <TYPE>");
            helpText.AppendLine("  -o, --output-path <NAME>");
            helpText.AppendLine("  -v, --app-version <VERSION>");
            helpText.AppendLine("  -p, --project-path <PATH>");
            helpText.AppendLine("  -c, --publish-config <CONFIG>");
            helpText.AppendLine("      --config-path <PATH>");
            helpText.AppendLine("  -b, --binary-path <PATH>");
            helpText.AppendLine("      --clean");
            helpText.AppendLine("  -n, --new <TYPE>");
            helpText.AppendLine("      --upgrade-config");
            helpText.AppendLine("  -l, --log-level <LEVEL>");
            helpText.AppendLine("      --verbose");
            helpText.AppendLine("  -y, --skip-all");
            helpText.AppendLine("  -h, --help");
            helpText.AppendLine("      --version");
            helpText.AppendLine("      --signing-identity <ID>");
            helpText.AppendLine("      --apple-id <EMAIL>");
            helpText.AppendLine("      --apple-team-id <ID>");
            helpText.AppendLine("      --apple-password <PASSWORD>");
            helpText.AppendLine();

            helpText.AppendLine("Use --verbose for more information");
        }
        else
        {
            // Options (Grouped by category)
            helpText.AppendLine("PACKAGE OPTIONS:");
            helpText.AppendLine("  -t, --package-type <TYPE>      Package type:");
            helpText.AppendLine("                                   exe       - Windows executable");
            helpText.AppendLine("                                   msi       - Windows installer");
            helpText.AppendLine("                                   app       - macOS application bundle");
            helpText.AppendLine("                                   dmg       - macOS disk image");
            helpText.AppendLine("                                   appimage  - Linux AppImage");
            helpText.AppendLine("                                   deb       - Debian/Ubuntu package");
            helpText.AppendLine("                                   rpm       - RedHat/Fedora package");
            helpText.AppendLine("                                   flatpak   - Flatpak package");
            helpText.AppendLine("                                   pacman    - Arch package");
            helpText.AppendLine("                                   portable  - Compressed archive");
            helpText.AppendLine("  -r, --runtime <RID>            Target runtime identifier:");
            helpText.AppendLine("                                   linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64");
            helpText.AppendLine("  -f, --framework <TYPE>         Target framework type (default: netcore):");
            helpText.AppendLine("                                   netcore       - .NET Core and .NET projects");
            helpText.AppendLine("                                   netframework  - .NET Framework projects (Only works on Windows - exe, msi or portable)");
            helpText.AppendLine("  -o, --output-path <NAME>       Custom output package name");
            helpText.AppendLine("  -v, --app-version <VERSION>    Application version (e.g., 1.2.3)");
            helpText.AppendLine();

            helpText.AppendLine("PROJECT OPTIONS:");
            helpText.AppendLine("  -p, --project-path <PATH>      Project directory path:");
            helpText.AppendLine("                                   If not specified, uses the DotnetProjectPath from configuration file");
            helpText.AppendLine("  -c, --publish-config <CONFIG>  Publish configuration (default: Release):");
            helpText.AppendLine("                                   Release");
            helpText.AppendLine("                                   Debug");
            helpText.AppendLine("      --config-path <PATH>       Netloy configuration file path");
            helpText.AppendLine("  -b, --binary-path <PATH>       Directory containing pre-built binaries");
            helpText.AppendLine("                                   Skips automatic dotnet publish step");
            helpText.AppendLine("                                   Use for custom builds, cross-compilation");
            helpText.AppendLine("                                   or unsupported RIDs like linux-x86");
            helpText.AppendLine("      --clean                    Clean project before building:");
            helpText.AppendLine("                                   Also clean directories and files created by Netloy");
            helpText.AppendLine();

            helpText.AppendLine("CONFIGURATION OPTIONS:");
            helpText.AppendLine("  -n, --new <TYPE>               Create new configuration file:");
            helpText.AppendLine("                                   all     - All configuration files");
            helpText.AppendLine("                                   conf    - Main configuration file");
            helpText.AppendLine("                                   desktop - Linux desktop entry");
            helpText.AppendLine("                                   meta    - Application metadata");
            helpText.AppendLine("                                   plist   - macOS property list");
            helpText.AppendLine("                                   entitle - macOS entitlements");
            helpText.AppendLine("      --upgrade-config           Upgrade configuration to latest version");
            helpText.AppendLine();

            helpText.AppendLine("PLATFORM-SPECIFIC OPTIONS:");
            helpText.AppendLine("   --signing-identity <ID>       macOS code signing identity (e.g., \"Developer ID Application\")");
            helpText.AppendLine("                                   Optional for app and dmg packages on macOS");
            helpText.AppendLine("   --apple-id <EMAIL>            Apple ID email for notarization");
            helpText.AppendLine("                                   Optional for app and dmg packages notarization");
            helpText.AppendLine("   --apple-team-id <ID>          Apple Developer Team ID (10-character string)");
            helpText.AppendLine("                                   Optional for app and dmg packages notarization");
            helpText.AppendLine("   --apple-password <PASSWORD>   App-specific password for Apple ID");
            helpText.AppendLine("                                   Optional for app and dmg packages notarization");
            helpText.AppendLine("                                   Generate at: appleid.apple.com");
            helpText.AppendLine();

            helpText.AppendLine("OUTPUT OPTIONS:");
            helpText.AppendLine("  -l, --log-level <LEVEL>        Logging level (default: debug):");
            helpText.AppendLine("                                   debug   - Detailed debugging information");
            helpText.AppendLine("                                   info    - General information");
            helpText.AppendLine("                                   success - Success messages only");
            helpText.AppendLine("                                   warning - Warnings and errors");
            helpText.AppendLine("                                   error   - Errors only");
            helpText.AppendLine("      --verbose                  Enable detailed output");
            helpText.AppendLine("  -y, --skip-all                 Skip all confirmation prompts");
            helpText.AppendLine();

            helpText.AppendLine("INFORMATION:");
            helpText.AppendLine("  -h, --help                     Display this help message");
            helpText.AppendLine("      --version                  Display version information");
            helpText.AppendLine();

            helpText.AppendLine($"For more information, visit: {Constants.ProjectUrl}");
        }

        Console.WriteLine(helpText.ToString());
    }

    #endregion

    #region Helpers

    private void HandleArguments(Arguments arguments)
    {
        try
        {
            arguments.ShowHelp = _appArgs.Contains("-h") || _appArgs.Contains("--help");

            ValidateArguments(arguments);

            if (arguments is { ShowHelp: false, ShowVersion: false })
                CheckPathFormat(arguments);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            Logger.LogInfo("Use --help for more information", forceLog: true);
            throw;
        }
    }

    public void ShowConfigHelps()
    {
        if (_arguments == null)
            throw new InvalidOperationException("Arguments is null");

        var sb = new StringBuilder();

        if (!_arguments.Verbose)
        {
            sb.AppendLine("Use --verbose to see the detailed config content");
            sb.AppendLine();
        }

        var configParser = new ConfigurationParser(_arguments);
        var configs = configParser.GetDefaultConfigContent();
        sb.AppendLine(configs);

        Console.WriteLine(sb.ToString());
    }

    private static void CheckPathFormat(Arguments arguments)
    {
        // TODO: All these validation should be checked
        // TODO: Path validations are the same. Just names are different. Need to refactor this.

        // Validate and sanitize output name
        if (!arguments.OutputPath.IsStringNullOrEmpty())
        {
            var outputPath = arguments.OutputPath!.NormalizePath();
            if (outputPath.IsStringNullOrEmpty())
                throw new ArgumentException("Output path contains invalid characters.");

            if (!outputPath.IsAbsolutePath())
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), outputPath);

            outputPath = Path.GetFullPath(outputPath);
            Logger.LogDebug("Output path is: '{0}'", outputPath);
            arguments.OutputPath = outputPath;
        }

        // Normalize project path
        if (!arguments.ProjectPath.IsStringNullOrEmpty())
        {
            var projectPath = arguments.ProjectPath!.NormalizePath();
            if (projectPath.IsStringNullOrEmpty())
                throw new ArgumentException("Project path contains invalid characters.");

            if (!projectPath.IsAbsolutePath())
                projectPath = Path.Combine(Directory.GetCurrentDirectory(), projectPath);

            projectPath = Path.GetFullPath(projectPath);
            Logger.LogDebug("Project path is: '{0}'", projectPath);
            arguments.ProjectPath = projectPath;
        }

        // Normalize config path
        if (!arguments.ConfigPath.IsStringNullOrEmpty())
        {
            var configPath = arguments.ConfigPath!.NormalizePath();
            if (configPath.IsStringNullOrEmpty())
                throw new ArgumentException("Config path contains invalid characters.");

            if (!configPath.IsAbsolutePath())
                configPath = Path.Combine(Directory.GetCurrentDirectory(), configPath);

            configPath = Path.GetFullPath(configPath);
            Logger.LogDebug("Config path is: '{0}'", configPath);
            arguments.ConfigPath = configPath;
        }

        // Normalize binary path
        if (!arguments.BinaryPath.IsStringNullOrEmpty())
        {
            var binaryPath = arguments.BinaryPath!.NormalizePath();
            if (binaryPath.IsStringNullOrEmpty())
                throw new ArgumentException("Invalid binary path: contains disallowed characters.");

            if (!binaryPath.IsAbsolutePath())
                binaryPath = Path.Combine(Directory.GetCurrentDirectory(), binaryPath);

            binaryPath = Path.GetFullPath(binaryPath);
            if (!Directory.Exists(binaryPath))
                throw new DirectoryNotFoundException($"Binary directory not found: '{binaryPath}'. Ensure the path exists and contains your built files.");

            var binaryFiles = Directory.GetFiles(binaryPath, "*", SearchOption.AllDirectories);
            if (binaryFiles.Length == 0)
                throw new FileNotFoundException($"No .config files found in '{binaryPath}'. Checked all subdirectories. Add config files or check your build output.");

            Logger.LogDebug("Binary path set: '{0}' (skipping publish, using pre-built files)", binaryPath);
            arguments.BinaryPath = binaryPath;
        }
    }

    private void ValidateArguments(Arguments arguments)
    {
        // TODO: Complete validations

        // Check for conflicting flags
        if (arguments is { ShowHelp: true, ShowVersion: true })
            throw new ArgumentException("Cannot specify both --help and --version");

        // If help or version is requested, no other arguments should be provided
        if (arguments.ShowHelp || arguments.ShowVersion)
        {
            ValidateNoOtherArgumentsProvided(arguments);
            return;
        }

        if (arguments.NewType != null)
            return;

        if (arguments.UpgradeConfiguration)
        {
            if (arguments.ConfigPath.IsStringNullOrEmpty())
                throw new ArgumentException("Config path is required when upgrading configuration");

            return;
        }

        if (arguments.PackageType == null)
            throw new ArgumentException("Package type is required");

        if (!arguments.AppVersion.IsStringNullOrEmpty() && !arguments.AppVersion!.CheckShortVersion())
            throw new ArgumentException("App version is not in the correct format");

        if (arguments.PublishConfiguration.IsStringNullOrEmpty())
            throw new ArgumentException("Publish configuration is required");

        if (arguments.PublishConfiguration.Equals("Release", StringComparison.OrdinalIgnoreCase))
        {
            arguments.PublishConfiguration = "Release";
        }
        else if (arguments.PublishConfiguration.Equals("Debug", StringComparison.OrdinalIgnoreCase))
        {
            arguments.PublishConfiguration = "Debug";
        }
        else
        {
            throw new ArgumentException($"Invalid publish configuration: {arguments.PublishConfiguration}. Must be 'Release' or 'Debug'");
        }
    }

    private void ValidateNoOtherArgumentsProvided(Arguments arguments)
    {
        var conflictingArgs = new List<string>();

        if (arguments.PackageType != null)
            conflictingArgs.Add("--package-type");

        if (!arguments.Runtime.IsStringNullOrEmpty())
            conflictingArgs.Add("--runtime");

        if (arguments.Framework != null)
            conflictingArgs.Add("--framework");

        if (arguments.SkipAll)
            conflictingArgs.Add("--skip-all");

        if (!arguments.OutputPath.IsStringNullOrEmpty())
            conflictingArgs.Add("--output-path");

        if (arguments.NewType != null)
            conflictingArgs.Add("--new");

        if (!arguments.ProjectPath.IsStringNullOrEmpty())
            conflictingArgs.Add("--project-path");

        if (arguments.UpgradeConfiguration)
            conflictingArgs.Add("--upgrade-config");

        if (_appArgs.Contains("--publish-config"))
            conflictingArgs.Add("--publish-config");

        if (arguments.CleanProject)
            conflictingArgs.Add("--clean");

        if (!arguments.AppVersion.IsStringNullOrEmpty())
            conflictingArgs.Add("--app-version");

        if (!arguments.ConfigPath.IsStringNullOrEmpty())
            conflictingArgs.Add("--config-path");

        if (!arguments.BinaryPath.IsStringNullOrEmpty())
            conflictingArgs.Add("--binary-path");

        if (!arguments.MacOsSigningIdentity.IsStringNullOrEmpty())
            conflictingArgs.Add("--signing-identity");

        if (!arguments.AppleId.IsStringNullOrEmpty())
            conflictingArgs.Add("--apple-id");

        if (!arguments.AppleTeamId.IsStringNullOrEmpty())
            conflictingArgs.Add("--apple-team-id");

        if (!arguments.ApplePassword.IsStringNullOrEmpty())
            conflictingArgs.Add("--apple-password");

        if (conflictingArgs.Count == 0)
            return;

        var argsList = string.Join(", ", conflictingArgs);
        throw new ArgumentException($"Cannot specify {argsList} with --help or --version");
    }

    #endregion
}