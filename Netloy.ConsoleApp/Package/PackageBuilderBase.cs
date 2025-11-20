using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Helpers;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package;

public class PackageBuilderBase
{
    #region Properties

    protected Arguments Arguments { get; }
    protected Configurations Configurations { get; }
    protected string NetloyTempPath { get; }
    protected string RootDirectory { get; }
    protected string AppVersion { get; }
    protected string PackageRelease { get; }
    protected string OutputDirectory { get; }
    protected string OutputName { get; }
    protected string IconsDirectory { get; }
    protected string ScriptsDirectory { get; set; }
    protected string StartupDirectory { get; }
    protected string AppExecName { get; }
    protected string DotnetProjectPath { get; }
    protected IconHelper IconHelper { get; }
    protected MacroExpander MacroExpander { get; }

    #endregion

    protected PackageBuilderBase(Arguments arguments, Configurations configurations)
    {
        Arguments = arguments;
        Configurations = configurations;
        NetloyTempPath = Path.Combine(Path.GetTempPath(), "netloy");
        RootDirectory = Path.Combine(NetloyTempPath, Configurations.AppBaseName, Arguments.PackageType!.Value.ToString().ToLowerInvariant());
        AppVersion = Arguments.AppVersion ?? GetAppVersion(Configurations.AppVersionRelease);
        PackageRelease = GetPackageRelease(Configurations.AppVersionRelease);
        OutputDirectory = GetOutputDirectory();
        OutputName = GetOutputName();
        IconsDirectory = Path.Combine(RootDirectory, "icons");
        ScriptsDirectory = Path.Combine(RootDirectory, "scripts");
        StartupDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? throw new DirectoryNotFoundException("Unable to find startup directory.");
        AppExecName = GetAppExecName();
        DotnetProjectPath = GetProjectPath();
        IconHelper = new IconHelper(Configurations, IconsDirectory);
        MacroExpander = new MacroExpander(Arguments);

        GenerateGlobalMacros();
        CreateDirectories();
        IconHelper.GenerateIconsAsync().Wait();
    }

    protected async Task PublishAsync(string outputDir, string primaryIconExt)
    {
        // Check and clean build directory
        if (Directory.Exists(outputDir))
        {
            if (!Arguments.SkipAll)
            {
                if (!Confirm.ShowConfirm($"Build directory already exists. Directory path: {outputDir}. Do you want to delete it?"))
                    throw new OperationCanceledException("Operation canceled by user.");
            }
            else
            {
                Logger.LogInfo("Build directory already exists. Directory path: {0}. Deleting it...", outputDir);
            }

            Directory.Delete(outputDir, true);
        }

        // Set primary icon in macros
        SetPrimaryIconInMacros(primaryIconExt);

        Directory.CreateDirectory(outputDir);
        MacroExpander.SetMacroValue(MacroId.PublishOutputDirectory, outputDir);

        if (Arguments.CleanProject)
            CleanDotnetProject(DotnetProjectPath);

        PublishDotnetProject(DotnetProjectPath, outputDir);

        await RunPostPublishScriptAsync();

        Logger.LogSuccess("Publish completed successfully!");
    }

    protected string GetPackageArch()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Arguments.Runtime?.ToLowerInvariant() switch
            {
                "win-x64" => "x64",
                "win-x86" => "x86",
                "win-arm64" => "arm64",
                _ => throw new InvalidOperationException($"Unsupported runtime: {Arguments.Runtime}")
            };
        }

        throw new PlatformNotSupportedException($"Couldn't get package arch. {RuntimeInformation.OSDescription} is not supported.");
    }

    private void SetPrimaryIconInMacros(string ext)
    {
        var primaryIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(ext, StringComparison.OrdinalIgnoreCase));
        if (primaryIcon.IsStringNullOrEmpty())
            return;

        if (!File.Exists(primaryIcon))
            Logger.LogWarning("Primary icon file not found. File path: {0}", primaryIcon);

        MacroExpander.SetMacroValue(MacroId.PrimaryIconFileName, Path.GetFileNameWithoutExtension(primaryIcon) ?? string.Empty);
        MacroExpander.SetMacroValue(MacroId.PrimaryIconFilePath, primaryIcon ?? string.Empty);
    }

    private void PublishDotnetProject(string projectPath, string outputDir)
    {
        // Build the project with dotnet publish
        Logger.LogInfo("Building .NET project...");

        var configuration = !Arguments.PublishConfiguration.IsStringNullOrEmpty()
            ? Arguments.PublishConfiguration
            : "Release";

        // Construct dotnet publish arguments
        var publishArgs = BuildPublishArguments(projectPath, outputDir, Arguments.Runtime!, configuration);

        Logger.LogInfo("Running: dotnet {0}", publishArgs);

        // Execute dotnet publish
        var exitCode = ExecuteDotnetCommand(publishArgs);
        if (exitCode != 0)
            throw new InvalidOperationException($"dotnet publish failed with exit code {exitCode}");

        Logger.LogSuccess("Project published successfully!");
    }

    private string BuildPublishArguments(string projectPath, string outputDir, string runtime, string configuration)
    {
        var sb = new StringBuilder();

        sb.Append("publish");
        sb.Append($" \"{projectPath}\"");
        sb.Append($" -c {configuration}");
        sb.Append($" -r {runtime}");
        sb.Append($" -o \"{outputDir}\"");

        // Add any extra publish arguments from configuration
        if (!Configurations.DotnetPublishArgs.IsStringNullOrEmpty())
        {
            var customArgs = MacroExpander.ExpandMacros(Configurations.DotnetPublishArgs);
            sb.Append($" {customArgs}");
        }

        return sb.ToString();
    }

    private void CleanDotnetProject(string projectPath)
    {
        Logger.LogInfo("Cleaning .NET project...");

        var cleanArgs = BuildCleanArguments(projectPath);
        Logger.LogInfo("Running: dotnet {0}", cleanArgs);

        // Execute dotnet clean
        var exitCode = ExecuteDotnetCommand(cleanArgs);
        if (exitCode != 0)
            throw new InvalidOperationException($"dotnet clean failed with exit code {exitCode}");

        Logger.LogSuccess("Project cleaned successfully!");
    }

    private static string BuildCleanArguments(string projectPath)
    {
        var sb = new StringBuilder();

        sb.Append("clean");
        sb.Append($" \"{projectPath}\"");

        return sb.ToString();
    }

    private int ExecuteDotnetCommand(string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start dotnet process.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        // Log output if verbose mode is enabled
        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogInfo("dotnet output:\n{0}", output);

        if (error.IsStringNullOrEmpty())
            return process.ExitCode;

        // Always log errors
        if (process.ExitCode != 0)
        {
            Logger.LogError("dotnet error:\n{0}", forceLog: true, error);
        }
        else if (!error.IsStringNullOrEmpty())
        {
            // Sometimes warnings come through stderr
            Logger.LogWarning("dotnet warnings:\n{0}", forceLog: true, error);
        }

        return process.ExitCode;
    }

    private async Task RunPostPublishScriptAsync()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var postPublishScript = isWindows ? Configurations.DotnetPostPublishOnWindows : Configurations.DotnetPostPublish;

        Logger.LogInfo("Running post publish script: {0}", postPublishScript);

        var scriptContent = await File.ReadAllTextAsync(postPublishScript);
        scriptContent = MacroExpander.ExpandMacros(scriptContent);

        if ((isWindows && scriptContent.Contains("pause")) || (!isWindows && scriptContent.Contains("read")))
        {
            var invalidCommand = isWindows ? "pause" : "read";
            var message = $"Post publish script contains '{invalidCommand}' command. This will prevent the application from closing after running. Consider removing it.";
            Logger.LogWarning(message, forceLog: true);
        }

        var fileName = Path.GetFileName(postPublishScript);
        var scriptPath = Path.Combine(ScriptsDirectory, fileName);
        await File.WriteAllTextAsync(scriptPath, scriptContent);

        var arguments = Configurations.DotnetPostPublishArguments.IsStringNullOrEmpty()
            ? string.Empty
            : MacroExpander.ExpandMacros(Configurations.DotnetPostPublishArguments);

        var exitCode = ScriptRunner.RunScript(scriptPath, arguments);
        if (exitCode != 0)
            throw new InvalidOperationException($"Post publish script failed with exit code {exitCode}");
    }

    private static string GetAppVersion(string version)
    {
        if (!version.Contains('['))
        {
            version = version.Replace("]", "");
            return version;
        }

        var index = version.IndexOf("[", StringComparison.OrdinalIgnoreCase);
        return version.Substring(0, index);
    }

    private static string GetPackageRelease(string version)
    {
        if (!version.Contains('['))
            return "1";

        var startIndex = version.IndexOf("[", StringComparison.OrdinalIgnoreCase);
        var endIndex = version.IndexOf("]", StringComparison.OrdinalIgnoreCase);
        return version.Substring(startIndex + 1, endIndex - startIndex - 1);
    }

    private string GetOutputDirectory()
    {
        var path = Arguments.OutputPath;

        if (!path.IsStringNullOrEmpty())
        {
            if (!path!.IsAbsolutePath())
                path = Path.Combine(Configurations.OutputDirectory, path!);

            if (Directory.Exists(path))
                return path;
        }

        return Path.GetDirectoryName(path) ?? Configurations.OutputDirectory;
    }

    private string GetOutputName()
    {
        var outputPath = Arguments.OutputPath;
        var name = Path.GetFileName(outputPath);

        if (!name.IsStringNullOrEmpty() && !Directory.Exists(outputPath))
            return name!;

        var ext = Arguments.PackageType switch
        {
            PackageType.Exe => ".exe",
            PackageType.Msi => ".msi",
            PackageType.AppBundle => ".app.zip",
            PackageType.Dmg => ".dmg",
            PackageType.AppImage => ".AppImage",
            PackageType.Deb => ".deb",
            PackageType.Rpm => ".rpm",
            PackageType.Flatpack => ".flatpak",
            PackageType.Portable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".zip" : ".tar.gz",
            _ => throw new InvalidOperationException("Invalid package type.")
        };

        name = Configurations.PackageName;
        name += $".{AppVersion}-{PackageRelease}";
        name += $".{Arguments.Runtime}";
        name += $"{ext}";

        return name;
    }

    private void CreateDirectories()
    {
        if (Directory.Exists(RootDirectory))
            Directory.Delete(RootDirectory, true);

        Directory.CreateDirectory(RootDirectory);

        if (!Directory.Exists(OutputDirectory))
            Directory.CreateDirectory(OutputDirectory);

        if (!Directory.Exists(IconsDirectory))
            Directory.CreateDirectory(IconsDirectory);

        if (!Directory.Exists(ScriptsDirectory))
            Directory.CreateDirectory(ScriptsDirectory);
    }

    private string GetAppExecName()
    {
        var isWindows = Arguments.Runtime switch
        {
            "win-x64" or "win-x86" or "win-arm64" => true,
            _ => false
        };

        return isWindows ? Configurations.AppBaseName + ".exe" : Configurations.AppBaseName;
    }

    private string GetProjectPath()
    {
        // Get dotnet project path
        var projectPath = Arguments.ProjectPath.IsStringNullOrEmpty()
            ? Configurations.DotnetProjectPath
            : Arguments.ProjectPath;

        projectPath = Path.GetFullPath(projectPath ?? string.Empty);
        if (projectPath.IsStringNullOrEmpty())
            throw new InvalidOperationException("Project path not specified.");

        var isArgument = !Arguments.ProjectPath.IsStringNullOrEmpty();
        var directoryPath = isArgument ? Directory.GetCurrentDirectory() : Constants.ConfigFileDirectory;

        projectPath = projectPath.IsAbsolutePath()
            ? projectPath
            : Path.Combine(directoryPath, projectPath);

        // Check if project file exists
        if (File.Exists(projectPath) && Path.GetExtension(projectPath) == ".csproj")
            return projectPath;

        var projectFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
        switch (projectFiles.Length)
        {
            case 0:
                throw new FileNotFoundException("No project file found in the specified directory.");

            case > 1:
                {
                    Logger.LogWarning("Multiple project files found in the specified directory. Directory path: {0}", projectPath);

                    if (!Arguments.SkipAll)
                    {
                        if (!Confirm.ShowConfirm("Multiple project files found. Do you want to use the first project file found?"))
                            throw new OperationCanceledException("Operation canceled by user.");
                    }
                    else
                    {
                        Logger.LogInfo("Using first project file found. Project path: {0}", projectFiles[0]);
                    }

                    break;
                }
        }

        return projectFiles[0];
    }

    private void GenerateGlobalMacros()
    {
        MacroExpander.SetMacroValue(MacroId.ConfFileDirectory, Constants.ConfigFileDirectory);
        MacroExpander.SetMacroValue(MacroId.AppBaseName, Configurations.AppBaseName);
        MacroExpander.SetMacroValue(MacroId.AppFriendlyName, Configurations.AppFriendlyName);
        MacroExpander.SetMacroValue(MacroId.AppId, Configurations.AppId);
        MacroExpander.SetMacroValue(MacroId.AppShortSummary, Configurations.AppShortSummary);
        MacroExpander.SetMacroValue(MacroId.AppLicenseId, Configurations.AppLicenseId);
        MacroExpander.SetMacroValue(MacroId.AppExecName, AppExecName);
        MacroExpander.SetMacroValue(MacroId.PublisherName, Configurations.PublisherName);
        MacroExpander.SetMacroValue(MacroId.PublisherId, Configurations.PublisherId.IsStringNullOrEmpty() ? Configurations.AppId : Configurations.PublisherId);
        MacroExpander.SetMacroValue(MacroId.PublisherCopyright, Configurations.PublisherCopyright);
        MacroExpander.SetMacroValue(MacroId.PublisherLinkName, Configurations.PublisherLinkName);
        MacroExpander.SetMacroValue(MacroId.PublisherLinkUrl, Configurations.PublisherLinkUrl);
        MacroExpander.SetMacroValue(MacroId.PublisherEmail, Configurations.PublisherEmail);
        MacroExpander.SetMacroValue(MacroId.DesktopNoDisplay, Configurations.DesktopNoDisplay.ToString().ToLowerInvariant());
        MacroExpander.SetMacroValue(MacroId.DesktopIntegrate, (!Configurations.DesktopNoDisplay).ToString().ToLowerInvariant());
        MacroExpander.SetMacroValue(MacroId.DesktopTerminal, Configurations.DesktopTerminal.ToString().ToLowerInvariant());
        MacroExpander.SetMacroValue(MacroId.PrimeCategory, Configurations.PrimeCategory);
        MacroExpander.SetMacroValue(MacroId.AppVersion, AppVersion);
        MacroExpander.SetMacroValue(MacroId.PackageRelease, PackageRelease);
        MacroExpander.SetMacroValue(MacroId.PackageType, Arguments.PackageType!.ToString()!.ToLowerInvariant());
        MacroExpander.SetMacroValue(MacroId.DotnetRuntime, RuntimeInformation.FrameworkDescription);
        MacroExpander.SetMacroValue(MacroId.PackageArch, Arguments.Runtime!);
    }
}