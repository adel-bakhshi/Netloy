using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Helpers;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Netloy.ConsoleApp.Package;

public class PackageBuilderBase
{
    #region Properties

    protected Arguments Arguments { get; }
    protected Configurations Configurations { get; }
    protected string NetloyTempPath { get; }
    protected string NetloyProjectTempPath { get; }
    protected string RootDirectory { get; }
    protected string AppVersion { get; }
    protected string PackageRelease { get; }
    protected string OutputDirectory { get; }
    protected string OutputName { get; }
    protected string IconsDirectory { get; }
    protected string ScriptsDirectory { get; set; }
    protected string AppExecName { get; }
    protected string DotnetProjectPath { get; }
    protected IconHelper IconHelper { get; }
    protected MacroExpander MacroExpander { get; }

    #endregion Properties

    protected PackageBuilderBase(Arguments arguments, Configurations configurations)
    {
        Arguments = arguments;
        Configurations = configurations;
        NetloyTempPath = Path.Combine(Path.GetTempPath(), "netloy");
        NetloyProjectTempPath = Path.Combine(NetloyTempPath, Configurations.AppBaseName);
        RootDirectory = Path.Combine(NetloyProjectTempPath, Arguments.PackageType!.Value.ToString().ToLowerInvariant());
        AppVersion = Arguments.AppVersion ?? GetAppVersion(Configurations.AppVersionRelease);
        PackageRelease = GetPackageRelease(Configurations.AppVersionRelease);
        OutputDirectory = GetOutputDirectory();
        OutputName = GetOutputName();
        IconsDirectory = Path.Combine(NetloyProjectTempPath, "icons");
        ScriptsDirectory = Path.Combine(NetloyProjectTempPath, "scripts");
        AppExecName = GetAppExecName();
        DotnetProjectPath = GetProjectPath();
        IconHelper = new IconHelper(Configurations, IconsDirectory);
        MacroExpander = new MacroExpander(Arguments);

        GenerateGlobalMacros();
        CreateDirectories();
        IconHelper.GenerateIconsAsync().Wait();
    }

    protected async Task PublishAsync(string outputDir)
    {
        // Check and clean build directory
        if (Directory.Exists(outputDir))
        {
            var files = Directory.GetFiles(outputDir);
            if (files.Length > 0)
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
        }

        Directory.CreateDirectory(outputDir);
        MacroExpander.SetMacroValue(MacroId.PublishOutputDirectory, outputDir);

        // Set primary icon in macros
        SetPrimaryIconInMacros();

        if (Arguments.BinaryPath.IsStringNullOrEmpty())
        {
            switch (Arguments.Framework)
            {
                case null:
                case FrameworkType.NetCore:
                {
                    await PublishWithDotnetCliAsync(outputDir);
                    break;
                }

                default:
                {
                    await PublishWithMsBuildAsync(outputDir);
                    break;
                }
            }
        }
        else
        {
            CopyBinaries(outputDir);
        }

        await RunPostPublishScriptAsync();

        Logger.LogSuccess("Publish completed successfully!");
    }

    protected string GetWindowsPackageArch()
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

    public virtual void Clear()
    {
        try
        {
            if (!Directory.Exists(NetloyTempPath))
                return;

            Logger.LogInfo("Cleaning {0} package build artifacts...", Arguments.PackageType?.ToString().ToUpperInvariant());

            Directory.Delete(NetloyTempPath, true);

            Logger.LogSuccess("Cleanup completed!");
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to clean build artifacts: {0}", forceLog: true, ex.Message);
            throw;
        }
    }

    private void SetPrimaryIconInMacros()
    {
        var primaryIcon = string.Empty;
        switch (Arguments.PackageType)
        {
            case PackageType.Exe:
            case PackageType.Msi:
            {
                primaryIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".ico", StringComparison.OrdinalIgnoreCase));
                break;
            }

            case PackageType.App:
            case PackageType.Dmg:
            {
                primaryIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".icns", StringComparison.OrdinalIgnoreCase));
                break;
            }

            case PackageType.AppImage:
            case PackageType.Deb:
            case PackageType.Flatpak:
            case PackageType.Rpm:
            case PackageType.Pacman:
            {
                var svgIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".svg", StringComparison.OrdinalIgnoreCase));
                if (!svgIcon.IsStringNullOrEmpty() && File.Exists(svgIcon))
                {
                    primaryIcon = svgIcon;
                }
                else
                {
                    var pngIcons = Configurations.IconsCollection
                        .Where(ico => Path.GetExtension(ico).Equals(".png", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    switch (pngIcons.Count)
                    {
                        case 0:
                        {
                            Logger.LogWarning("There is no PNG primary icon.");
                            break;
                        }

                        case 1:
                        {
                            primaryIcon = pngIcons[0];
                            break;
                        }

                        case > 1:
                        {
                            var biggestSize = 0;
                            var biggestPngPath = string.Empty;
                            foreach (var iconPath in pngIcons)
                            {
                                var sections = iconPath.Split('.');
                                var sizeSection = sections[1].Split('x');
                                var size = int.Parse(sizeSection[0]);
                                if (size > biggestSize)
                                {
                                    biggestSize = size;
                                    biggestPngPath = iconPath;
                                }
                            }

                            primaryIcon = biggestPngPath;
                            break;
                        }
                    }
                }

                break;
            }

            default:
            {
                Logger.LogWarning("There is no primary icon for {0} package type", Arguments.PackageType?.ToString().ToUpperInvariant());
                break;
            }
        }

        if (primaryIcon.IsStringNullOrEmpty())
        {
            Logger.LogWarning("Couldn't find valid primary icon for {0} package type", Arguments.PackageType?.ToString().ToUpperInvariant());
            return;
        }

        if (!File.Exists(primaryIcon))
            Logger.LogWarning("Primary icon file not found. File path: {0}", primaryIcon);

        MacroExpander.SetMacroValue(MacroId.PrimaryIconFileName, Path.GetFileName(primaryIcon) ?? string.Empty);
        MacroExpander.SetMacroValue(MacroId.PrimaryIconFilePath, primaryIcon ?? string.Empty);
    }

    private async Task PublishWithDotnetCliAsync(string outputDir)
    {
        if (Arguments.CleanProject)
            await CleanDotnetProjectAsync(DotnetProjectPath);

        await PublishDotnetProjectAsync(DotnetProjectPath, outputDir);
    }

    private async Task PublishDotnetProjectAsync(string projectPath, string outputDir)
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
        var exitCode = await ExecuteDotnetCommandAsync(publishArgs);
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

        var arguments = sb.ToString();

        // Add UseAppHost argument if not present and package type is App or Dmg
        // For more info visit https://docs.microsoft.com/en-us/dotnet/core/install/macos-notarization-issues
        if (Arguments.PackageType is PackageType.App or PackageType.Dmg)
        {
            if (!arguments.Contains("UseAppHost", StringComparison.OrdinalIgnoreCase))
            {
                bool addAppHost;

                if (!Arguments.SkipAll)
                {
                    Logger.LogWarning("For macOS App/DMG packaging it's recommended to publish with a native app host (-p:UseAppHost=true) so the app can be signed and notarized correctly.");
                    addAppHost = Confirm.ShowConfirm("Do you want to add -p:UseAppHost=true to dotnet publish?");
                }
                else
                {
                    addAppHost = true;
                    Logger.LogWarning("SkipAll is enabled. Automatically adding -p:UseAppHost=true for macOS App/DMG packaging.");
                }

                if (addAppHost)
                    sb.Append(" -p:UseAppHost=true");
            }
            else if (arguments.Contains("UseAppHost=false", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning("DotnetPublishArgs contains UseAppHost=false. macOS App/DMG packaging may fail notarization or signing if no app host is generated.");
            }
        }

        return arguments;
    }

    private async Task CleanDotnetProjectAsync(string projectPath)
    {
        Logger.LogInfo("Cleaning .NET project...");

        var cleanArgs = BuildCleanArguments(projectPath);
        Logger.LogInfo("Running: dotnet {0}", cleanArgs);

        // Execute dotnet clean
        var exitCode = await ExecuteDotnetCommandAsync(cleanArgs);
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

    private async Task<int> ExecuteDotnetCommandAsync(string arguments)
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
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

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

    private async Task PublishWithMsBuildAsync(string outputDir)
    {
        Logger.LogInfo("Building .NET Framework project with MSBuild...");

        if (Arguments.CleanProject)
            await CleanWithMsBuildAsync();

        var buildArgs = BuildMsBuildArguments(outputDir);
        Logger.LogInfo("Running: msbuild {0}", buildArgs);

        var exitCode = await ExecuteMsBuildCommandAsync(buildArgs);
        if (exitCode != 0)
            throw new InvalidOperationException($"MSBuild failed with exit code {exitCode}");

        Logger.LogSuccess("Project built successfully with MSBuild!");
    }

    private string BuildMsBuildArguments(string outputDir)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{DotnetProjectPath}\"");
        sb.Append(" /t:Build");
        sb.Append($" /p:Configuration={Arguments.PublishConfiguration}");
        sb.Append($" /p:OutputPath=\"{outputDir}\"");

        // Map Runtime to Platform for .NET Framework projects
        var platform = GetWindowsPackageArch();
        // Valid values are x86, x64 and ARM64. So we need to convert arm64 to ARM64
        platform = platform.Equals("arm64", StringComparison.CurrentCultureIgnoreCase) ? platform.ToUpperInvariant() : platform;
        sb.Append($" /p:PlatformTarget={platform}");
        sb.Append(" /p:Prefer32Bit=false");

        // Add any extra publish arguments from configuration
        if (!Configurations.DotnetPublishArgs.IsStringNullOrEmpty())
        {
            var customArgs = MacroExpander.ExpandMacros(Configurations.DotnetPublishArgs);
            sb.Append($" {customArgs}");
        }

        return sb.ToString();
    }

    private async Task CleanWithMsBuildAsync()
    {
        Logger.LogInfo("Cleaning .NET Framework project with MSBuild...");

        var sb = new StringBuilder();
        sb.Append($"\"{DotnetProjectPath}\"");
        sb.Append(" /t:Clean");

        Logger.LogInfo("Running: msbuild {0}", sb.ToString());

        var exitCode = await ExecuteMsBuildCommandAsync(sb.ToString());
        if (exitCode != 0)
            throw new InvalidOperationException($"MSBuild clean failed with exit code {exitCode}");

        Logger.LogSuccess("Project cleaned successfully with MSBuild!");
    }

    private async Task<int> ExecuteMsBuildCommandAsync(string arguments)
    {
        var msbuildPath = FindMsBuildProcess();
        if (msbuildPath.IsStringNullOrEmpty())
            throw new InvalidOperationException("Failed to find MSBuild process.");

        var processInfo = new ProcessStartInfo
        {
            FileName = msbuildPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start msbuild process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        // Log output if verbose mode is enabled
        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogInfo("msbuild output:\n{0}", output);

        if (error.IsStringNullOrEmpty())
            return process.ExitCode;

        if (!output.IsStringNullOrEmpty() && Arguments.Verbose)
            Logger.LogInfo("msbuild output:\n{0}", output);

        // Always log errors
        if (process.ExitCode != 0)
        {
            var message = error.IsStringNullOrEmpty() ? output : error;
            Logger.LogError("msbuild error:\n{0}", forceLog: true, message);
        }
        else if (!error.IsStringNullOrEmpty())
        {
            // Sometimes warnings come through stderr
            Logger.LogWarning("msbuild warnings:\n{0}", forceLog: true, error);
        }

        return process.ExitCode;
    }

    private static string? FindMsBuildProcess()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!File.Exists(vswherePath))
            return null;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = vswherePath,
                Arguments = "-latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return paths.FirstOrDefault();
    }

    private void CopyBinaries(string outputDir)
    {
        Logger.LogInfo("Copying binaries from '{0}' to '{1}'...", Arguments.BinaryPath, outputDir);

        var filesCopied = 0;
        foreach (var binaryFile in Directory.GetFiles(Arguments.BinaryPath!, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(Arguments.BinaryPath!, binaryFile);
            var targetPath = Path.Combine(outputDir, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath) ?? throw new DirectoryNotFoundException("Unable to find target directory.");

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            File.Copy(binaryFile, targetPath, true);
            filesCopied++;

            Logger.LogDebug("Copied '{0}' binary file from '{1}' to '{2}'", Path.GetFileName(binaryFile), binaryFile, targetPath);
        }

        Logger.LogSuccess("Copied {0} file(s) from binary path.", filesCopied);
    }

    private async Task RunPostPublishScriptAsync()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var postPublishScript = isWindows ? Configurations.DotnetPostPublishOnWindows : Configurations.DotnetPostPublish;

        // Check if post-publish script path is configured
        if (postPublishScript.IsStringNullOrEmpty())
        {
            Logger.LogDebug("No post-publish script configured. Skipping post-publish step.");
            return;
        }

        // Check if the script file exists
        if (!File.Exists(postPublishScript))
        {
            Logger.LogWarning("Post-publish script file not found: {0}. Skipping post-publish step.", postPublishScript);
            return;
        }

        Logger.LogInfo("Running post-publish script: {0}", postPublishScript);

        var scriptContent = await File.ReadAllTextAsync(postPublishScript);
        scriptContent = MacroExpander.ExpandMacros(scriptContent);

        if ((isWindows && scriptContent.Contains("pause")) || (!isWindows && scriptContent.Contains("read")))
        {
            var invalidCommand = isWindows ? "pause" : "read";
            var message = $"Post-publish script contains '{invalidCommand}' command. This will prevent the application from closing after running. Consider removing it.";
            Logger.LogWarning(message, forceLog: true);
        }

        var fileName = Path.GetFileName(postPublishScript);
        var scriptPath = Path.Combine(ScriptsDirectory, fileName);
        await File.WriteAllTextAsync(scriptPath, scriptContent);

        var exitCode = await ScriptRunner.RunScriptAsync(scriptPath);
        if (exitCode != 0)
            throw new InvalidOperationException($"Post-publish script failed with exit code {exitCode}");

        Logger.LogSuccess("Post-publish script completed successfully!");
    }

    private static string GetAppVersion(string version)
    {
        if (!version.Contains('['))
            return version.Replace("]", "");

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
            PackageType.App => ".app.zip",
            PackageType.Dmg => ".dmg",
            PackageType.AppImage => ".AppImage",
            PackageType.Deb => ".deb",
            PackageType.Rpm => ".rpm",
            PackageType.Flatpak => ".flatpak",
            PackageType.Pacman   => ".pkg.tar.zst",
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

        GenerateAppStreamMetadataAsync().Wait();
    }

    private async Task GenerateAppStreamMetadataAsync()
    {
        var description = AppStreamMetadataHelper.GenerateDescriptionXml(Configurations.AppDescription);
        MacroExpander.SetMacroValue(MacroId.AppStreamDescriptionXml, description);

        var changelog = await AppStreamMetadataHelper.GenerateChangelogXmlAsync(Configurations.AppChangeFile);
        MacroExpander.SetMacroValue(MacroId.AppStreamChangelogXml, changelog);
    }
}