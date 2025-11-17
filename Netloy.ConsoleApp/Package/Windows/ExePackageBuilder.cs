using System.Diagnostics;
using System.Text;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Windows;

public class ExePackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string InnoSetupCompiler = "iscc";
    private const string PromptBat = "CommandPrompt.bat";

    #endregion

    #region Properties

    public string PublishOutputDir { get; private set; } = string.Empty;
    public string TerminalIcon { get; }
    public string InnoSetupScriptPath { get; }

    #endregion

    public ExePackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        TerminalIcon = Path.Combine(StartupDirectory, "Assets", "terminal.ico");
        InnoSetupScriptPath = Path.Combine(RootDirectory, $"{Configurations.AppBaseName}.iss");
    }

    public async Task BuildAsync()
    {
        try
        {
            Logger.LogInfo("Starting Windows EXE package build...", forceLog: true);

            // Check if Inno Setup is installed
            if (!IsInnoSetupInstalled())
                throw new InvalidOperationException($"Inno Setup compiler (iscc) not found. Please install Inno Setup from {Constants.InnoSetupDownloadUrl}");

            // Publish project
            PublishOutputDir = Path.Combine(RootDirectory, "publish");
            await PublishAsync(PublishOutputDir);

            if (!Configurations.StartCommand.IsStringNullOrEmpty() &&
                !Configurations.StartCommand.Equals(AppExecName, StringComparison.InvariantCultureIgnoreCase))
            {
                var path = Path.Combine(PublishOutputDir, Configurations.StartCommand + ".bat");
                var installPath = Path.Combine("", AppExecName);
                var script = $"start {installPath} %*";
                await File.WriteAllTextAsync(path, script, Encoding.UTF8);
            }

            if (!Configurations.SetupCommandPrompt.IsStringNullOrEmpty())
            {
                var title = EscapeBat(Configurations.SetupCommandPrompt);
                var cmd = EscapeBat(!Configurations.StartCommand.IsStringNullOrEmpty() ? Configurations.StartCommand : Configurations.AppBaseName);
                var path = Path.Combine(PublishOutputDir, PromptBat);

                var echoCopy = !Configurations.PublisherCopyright.IsStringNullOrEmpty()
                    ? $"& echo {EscapeBat(Configurations.PublisherCopyright)}"
                    : null;

                var script = $"start cmd /k \"cd /D %userprofile% & title {title} & echo {cmd} {AppVersion} {echoCopy} & set path=%path%;%~dp0\"";
                await File.WriteAllTextAsync(path, script, Encoding.UTF8);
            }

            // Generate Inno Setup script
            Logger.LogInfo("Generating Inno Setup script...");
            var scriptContent = GenerateInnoSetupScript();

            // Save script to temp file
            await File.WriteAllTextAsync(InnoSetupScriptPath, scriptContent, Encoding.UTF8);
            Logger.LogInfo("Script saved to: {0}", InnoSetupScriptPath);

            // Compile with Inno Setup
            Logger.LogInfo("Compiling setup with Inno Setup...");
            CompileInnoSetupScript();

            Logger.LogSuccess("Windows EXE package build completed successfully!");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            throw;
        }
    }

    public void Clear()
    {
        try
        {
            Logger.LogInfo("Cleaning up '{0}'...", RootDirectory);
            Directory.Delete(RootDirectory, true);

            Logger.LogSuccess("Cleanup completed successfully!");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            throw;
        }
    }

    private static bool IsInnoSetupInstalled()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = InnoSetupCompiler,
                Arguments = "/?",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            return process != null;
        }
        catch
        {
            return false;
        }
    }

    private string GenerateInnoSetupScript()
    {
        var sb = new StringBuilder();

        var primaryIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).ToLowerInvariant().Equals(".ico"));
        if (primaryIcon.IsStringNullOrEmpty())
            throw new FileNotFoundException($"Couldn't find icon file. Icon path: {primaryIcon}");

        sb.AppendLine("[Setup]");
        sb.AppendLine($"AppName={Configurations.AppFriendlyName}");
        sb.AppendLine($"AppId={Configurations.AppId}");
        sb.AppendLine($"AppVersion={AppVersion}");
        sb.AppendLine($"AppVerName={Configurations.AppFriendlyName} {AppVersion}");
        sb.AppendLine($"VersionInfoVersion={AppVersion}");
        sb.AppendLine($"OutputDir={OutputDirectory}");
        sb.AppendLine($"OutputBaseFilename={Path.GetFileNameWithoutExtension(OutputName)}");
        sb.AppendLine($"AppPublisher={Configurations.PublisherName}");
        sb.AppendLine($"AppCopyright={Configurations.PublisherCopyright}");
        sb.AppendLine($"AppPublisherURL={Configurations.PublisherLinkUrl}");
        sb.AppendLine($"InfoBeforeFile={Configurations.AppChangeFile}");
        sb.AppendLine($"LicenseFile={Configurations.AppLicenseFile}");
        sb.AppendLine($"SetupIconFile={primaryIcon}");
        sb.AppendLine("AllowNoIcons=yes");
        sb.AppendLine($"MinVersion={Configurations.SetupMinWindowsVersion}");

        sb.AppendLine($"DefaultDirName={{autopf}}\\{(!Configurations.SetupGroupName.IsStringNullOrEmpty() ? Configurations.SetupGroupName : Configurations.AppBaseName)}");
        sb.AppendLine($"DefaultGroupName={(!Configurations.SetupGroupName.IsStringNullOrEmpty() ? Configurations.SetupGroupName : Configurations.AppFriendlyName)}");

        var packageArch = GetPackageArch();
        if (packageArch is "x64" or "arm64")
        {
            // https://jrsoftware.org/ishelp/index.php?topic=setup_architecturesallowed
            sb.AppendLine($"ArchitecturesAllowed={packageArch}");

            // https://jrsoftware.org/ishelp/index.php?topic=setup_architecturesinstallin64bitmode
            sb.AppendLine($"ArchitecturesInstallIn64BitMode={packageArch}");
        }

        sb.AppendLine($"PrivilegesRequired={(Configurations.SetupAdminInstall ? "admin" : "lowest")}");

        // TODO: Check if possible to add uninstall icon
        if (!primaryIcon.IsStringNullOrEmpty())
            sb.AppendLine($"UninstallDisplayIcon={{app}}\\{Path.GetFileName(primaryIcon)}");

        if (!Configurations.SetupSignTool.IsStringNullOrEmpty())
        {
            // SetupSignTool = \"C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/signtool.exe" sign /f "{#GetEnv('SigningCertificate')}" /p "{#GetEnv('SigningCertificatePassword')}" /tr http://timestamp.sectigo.com /td sha256 /fd sha256 $f
            sb.AppendLine($"SignTool={Configurations.SetupSignTool}");
        }

        sb.AppendLine();
        sb.AppendLine("[Files]");
        sb.AppendLine($"Source: \"{PublishOutputDir}\\*.exe\"; DestDir: \"{{app}}\"; Flags: ignoreversion recursesubdirs createallsubdirs signonce;");

        var containsDll = Directory.GetFiles(PublishOutputDir, "*.dll").Length > 0;
        if (containsDll)
            sb.AppendLine($"Source: \"{PublishOutputDir}\\*.dll\"; DestDir: \"{{app}}\"; Flags: ignoreversion recursesubdirs createallsubdirs signonce;");

        var otherFilesExist = Directory.GetFiles(PublishOutputDir)
            .Any(f => !Path.GetExtension(f).Equals(".exe") && !Path.GetExtension(f).Equals(".dll"));

        if (otherFilesExist)
            sb.AppendLine($"Source: \"{PublishOutputDir}\\*\"; Excludes: \"*.exe,*.dll\"; DestDir: \"{{app}}\"; Flags: ignoreversion recursesubdirs createallsubdirs;");

        if (!primaryIcon.IsStringNullOrEmpty())
            sb.AppendLine($"Source: \"{primaryIcon}\"; DestDir: \"{{app}}\"; Flags: ignoreversion recursesubdirs createallsubdirs;");

        if (!Configurations.SetupCommandPrompt.IsStringNullOrEmpty())
        {
            // Need this below
            sb.AppendLine($"Source: \"{TerminalIcon}\"; DestDir: \"{{app}}\"; Flags: ignoreversion recursesubdirs createallsubdirs;");
        }

        sb.AppendLine();
        sb.AppendLine("[Tasks]");

        if (!Configurations.DesktopNoDisplay)
            sb.AppendLine($"Name: \"desktopicon\"; Description: \"Create a &Desktop Icon\"; GroupDescription: \"Additional icons:\"; Flags: unchecked");

        sb.AppendLine();
        sb.AppendLine("[REGISTRY]");
        sb.AppendLine();
        sb.AppendLine("[Icons]");

        if (!Configurations.DesktopNoDisplay)
        {
            sb.AppendLine($"Name: \"{{group}}\\{Configurations.AppFriendlyName}\"; Filename: \"{{app}}\\{AppExecName}\"");
            sb.AppendLine($"Name: \"{{userdesktop}}\\{Configurations.AppFriendlyName}\"; Filename: \"{{app}}\\{AppExecName}\"; Tasks: desktopicon");
        }

        // Still put CommandPrompt and Home Page link DesktopNoDisplay is true
        if (!Configurations.SetupCommandPrompt.IsStringNullOrEmpty())
        {
            // Give special terminal icon rather meaningless default .bat icon
            var name = Path.GetFileName(TerminalIcon);
            sb.AppendLine($"Name: \"{{group}}\\{Configurations.SetupCommandPrompt}\"; Filename: \"{{app}}\\{PromptBat}\"; IconFilename: \"{{app}}\\{name}\"");
        }

        if (!Configurations.PublisherLinkName.IsStringNullOrEmpty() && !Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
            sb.AppendLine($"Name: \"{{group}}\\{Configurations.PublisherLinkName}\"; Filename: \"{Configurations.PublisherLinkUrl}\"");

        sb.AppendLine();
        sb.AppendLine("[Run]");

        if (!Configurations.DesktopNoDisplay)
            sb.AppendLine($"Filename: \"{{app}}\\{AppExecName}\"; Description: Start Application Now; Flags: postinstall nowait skipifsilent");

        sb.AppendLine();
        sb.AppendLine("[InstallDelete]");
        sb.AppendLine("Type: filesandordirs; Name: \"{app}\\*\";");
        sb.AppendLine("Type: filesandordirs; Name: \"{group}\\*\";");
        sb.AppendLine();
        sb.AppendLine("[UninstallRun]");
        if (!Configurations.SetupUninstallScript.IsStringNullOrEmpty())
        {
            var uninstallScriptPath = $"{{app}}\\{Configurations.SetupUninstallScript}";
            sb.AppendLine($"Filename: \"{uninstallScriptPath}\"; Flags: runhidden waituntilterminated");
        }

        sb.AppendLine();
        sb.AppendLine("[UninstallDelete]");
        sb.AppendLine("Type: dirifempty; Name: \"{app}\"");

        return sb.ToString().TrimEnd();
    }

    private string GetPackageArch()
    {
        return Arguments.Runtime?.ToLowerInvariant() switch
        {
            "win-x64" => "x64",
            "win-x86" => "x86",
            "win-arm64" => "arm64",
            _ => throw new InvalidOperationException($"Unsupported runtime: {Arguments.Runtime}")
        };
    }

    private void CompileInnoSetupScript()
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = InnoSetupCompiler,
            Arguments = $"/O\"{OutputDirectory}\" \"{InnoSetupScriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start Inno Setup compiler.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogInfo("Inno Setup output:\n{0}", output);

        if (process.ExitCode == 0)
            return;

        var errorMsg = !error.IsStringNullOrEmpty() ? error : output;
        throw new InvalidOperationException($"Inno Setup compilation failed with exit code {process.ExitCode}:\n{errorMsg}");
    }

    private static string? EscapeBat(string? command)
    {
        if (command.IsStringNullOrEmpty())
            return null;

        // \ & | > < ^
        command = command!.Replace("^", "^^");

        command = command.Replace("\\", "^\\");
        command = command.Replace("&", "^&");
        command = command.Replace("|", "^|");
        command = command.Replace("<", "^<");
        command = command.Replace(">", "^>");

        command = command.Replace("%", "");

        return command;
    }
}