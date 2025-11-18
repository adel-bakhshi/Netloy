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

    public bool Validate()
    {
        // Check if Inno Setup is installed
        if (!IsInnoSetupInstalled())
            throw new InvalidOperationException($"Inno Setup compiler (iscc) not found. Please install Inno Setup from {Constants.InnoSetupDownloadUrl}");

        var ext = Path.GetExtension(Configurations.ExeWizardImageFile);
        if (!Configurations.ExeWizardImageFile.IsStringNullOrEmpty() &&
            (!File.Exists(Configurations.ExeWizardImageFile) || !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException($"Setup wizard image file not found. File path: {Configurations.ExeWizardImageFile}");

        ext = Path.GetExtension(Configurations.ExeWizardSmallImageFile);
        if (!Configurations.ExeWizardSmallImageFile.IsStringNullOrEmpty() &&
            (!File.Exists(Configurations.ExeWizardSmallImageFile) || !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException($"Setup wizard small image file not found. File path: {Configurations.ExeWizardSmallImageFile}");

        ext = Path.GetExtension(Configurations.SetupUninstallScript);
        if (!Configurations.SetupUninstallScript.IsStringNullOrEmpty() &&
            (!File.Exists(Configurations.SetupUninstallScript) || !ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException($"Setup uninstall script file not found. File path: {Configurations.SetupUninstallScript}");

        if ((Configurations.AssociateFiles || Configurations.ContextMenuIntegration) && !Configurations.SetupAdminInstall)
            throw new InvalidOperationException($"You must set {nameof(Configurations.SetupAdminInstall)} to true if you want to associate files or add context menu items.");

        return true;
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

        // Get primary icon
        var primaryIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".ico", StringComparison.OrdinalIgnoreCase));
        var iconFileName = Path.GetFileName(primaryIcon);

        if (primaryIcon.IsStringNullOrEmpty() || iconFileName.IsStringNullOrEmpty())
            throw new FileNotFoundException($"Couldn't find icon file. Icon path: {primaryIcon}");

        // Generate each section
        GenerateSetupSection(sb, primaryIcon!, iconFileName!);
        GenerateFilesSection(sb, primaryIcon!);
        GenerateTasksSection(sb);
        GenerateRegistrySection(sb, iconFileName!);
        GenerateIconsSection(sb, iconFileName!);
        GenerateRunSection(sb);
        GenerateInstallDeleteSection(sb);
        GenerateUninstallRunSection(sb);
        GenerateUninstallDeleteSection(sb);

        return sb.ToString().TrimEnd();
    }

    private void GenerateSetupSection(StringBuilder sb, string primaryIcon, string iconFileName)
    {
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
        sb.AppendLine("Compression=lzma2/max");
        sb.AppendLine("SolidCompression=yes");

        // Security & Validation
        if (!Configurations.SetupPasswordEncryption.IsStringNullOrEmpty())
            sb.AppendLine($"Password={Configurations.SetupPasswordEncryption}");

        if (!Configurations.ExeWizardImageFile.IsStringNullOrEmpty())
            sb.AppendLine($"WizardImageFile={Configurations.ExeWizardImageFile}");

        if (!Configurations.ExeWizardSmallImageFile.IsStringNullOrEmpty())
            sb.AppendLine($"WizardSmallImageFile={Configurations.ExeWizardSmallImageFile}");

        // Installation Management
        sb.AppendLine($"CloseApplications={(Configurations.SetupCloseApplications ? "yes" : "no")}");

        if (Configurations.SetupRestartIfNeeded)
            sb.AppendLine("RestartIfNeededByRun=yes");

        if (!Configurations.SetupDirExistsWarning)
            sb.AppendLine("DirExistsWarning=no");

        if (!Configurations.SetupAppendDefaultDirName)
            sb.AppendLine("AppendDefaultDirName=no");

        // Advanced Features
        if (Configurations.SetupDisableProgramGroupPage)
            sb.AppendLine("DisableProgramGroupPage=yes");

        if (Configurations.SetupDisableDirPage)
            sb.AppendLine("DisableDirPage=yes");

        if (Configurations.SetupDisableReadyPage)
            sb.AppendLine("DisableReadyPage=yes");

        // Uninstall & Registry
        if (!Configurations.SetupUninstallDisplayName.IsStringNullOrEmpty())
            sb.AppendLine($"UninstallDisplayName={Configurations.SetupUninstallDisplayName}");

        if (!Configurations.SetupCreateUninstallRegKey)
            sb.AppendLine("CreateUninstallRegKey=no");

        // Version Info
        if (!Configurations.SetupVersionInfoCompany.IsStringNullOrEmpty())
            sb.AppendLine($"VersionInfoCompany={Configurations.SetupVersionInfoCompany}");

        if (!Configurations.SetupVersionInfoDescription.IsStringNullOrEmpty())
            sb.AppendLine($"VersionInfoDescription={Configurations.SetupVersionInfoDescription}");

        // File Association
        if (Configurations.AssociateFiles && !Configurations.FileExtension.IsStringNullOrEmpty())
            sb.AppendLine("ChangesAssociations=yes");

        // Architecture
        var packageArch = GetPackageArch();
        if (packageArch is "x64" or "arm64")
        {
            // https://jrsoftware.org/ishelp/index.php?topic=setup_architecturesallowed
            sb.AppendLine($"ArchitecturesAllowed={packageArch}");

            // https://jrsoftware.org/ishelp/index.php?topic=setup_architecturesinstallin64bitmode
            sb.AppendLine($"ArchitecturesInstallIn64BitMode={packageArch}");
        }

        sb.AppendLine($"PrivilegesRequired={(Configurations.SetupAdminInstall ? "admin" : "lowest")}");

        if (!primaryIcon.IsStringNullOrEmpty())
            sb.AppendLine($"UninstallDisplayIcon={{app}}\\{iconFileName}");

        if (!Configurations.SetupSignTool.IsStringNullOrEmpty())
        {
            // SetupSignTool = \"C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/signtool.exe" sign /f "{#GetEnv('SigningCertificate')}" /p "{#GetEnv('SigningCertificatePassword')}" /tr http://timestamp.sectigo.com /td sha256 /fd sha256 $f
            sb.AppendLine($"SignTool={Configurations.SetupSignTool}");
        }

        sb.AppendLine();
    }

    private void GenerateFilesSection(StringBuilder sb, string primaryIcon)
    {
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

        if (!Configurations.SetupUninstallScript.IsStringNullOrEmpty())
            sb.AppendLine($"Source: \"{Configurations.SetupUninstallScript}\"; DestDir: \"{{app}}\"; Flags: ignoreversion recursesubdirs createallsubdirs;");

        sb.AppendLine();
    }

    private void GenerateTasksSection(StringBuilder sb)
    {
        sb.AppendLine("[Tasks]");

        // Desktop icon
        if (!Configurations.DesktopNoDisplay)
            sb.AppendLine("Name: \"desktopicon\"; Description: \"Create a &Desktop Icon\"; GroupDescription: \"Additional icons:\"; Flags: unchecked");

        // Quick Launch Icon
        sb.AppendLine("Name: \"quicklaunchicon\"; Description: \"Create a &Quick Launch icon\"; GroupDescription: \"Additional icons:\"; Flags: unchecked");

        // Startup Task
        sb.AppendLine($"Name: \"startup\"; Description: \"Run {Configurations.AppFriendlyName} at Windows startup\"; GroupDescription: \"Additional options:\"; Flags: unchecked");

        // File Association
        if (Configurations.AssociateFiles && !Configurations.FileExtension.IsStringNullOrEmpty())
            sb.AppendLine(
                $"Name: \"associatefiles\"; Description: \"Associate {Configurations.FileExtension} files with {Configurations.AppFriendlyName}\"; GroupDescription: \"File associations:\"; Flags: unchecked");

        // Context Menu
        if (Configurations.ContextMenuIntegration)
            sb.AppendLine("Name: \"contextmenu\"; Description: \"Add to context menu\"; GroupDescription: \"Integration:\"; Flags: unchecked");

        sb.AppendLine();
    }

    private void GenerateRegistrySection(StringBuilder sb, string iconFileName)
    {
        sb.AppendLine("[Registry]");

        // File Association
        if (Configurations.AssociateFiles && !Configurations.FileExtension.IsStringNullOrEmpty())
        {
            var ext = Configurations.FileExtension.StartsWith('.') ? Configurations.FileExtension : $".{Configurations.FileExtension}";
            var progId = $"{Configurations.AppBaseName}File";

            sb.AppendLine($"Root: HKCR; Subkey: \"{ext}\"; ValueType: string; ValueName: \"\"; ValueData: \"{progId}\"; Flags: uninsdeletevalue; Tasks: associatefiles");
            sb.AppendLine(
                $"Root: HKCR; Subkey: \"{progId}\"; ValueType: string; ValueName: \"\"; ValueData: \"{Configurations.AppFriendlyName} File\"; Flags: uninsdeletekey; Tasks: associatefiles");
            sb.AppendLine($"Root: HKCR; Subkey: \"{progId}\\DefaultIcon\"; ValueType: string; ValueName: \"\"; ValueData: \"{{app}}\\{iconFileName},0\"; Tasks: associatefiles");
            sb.AppendLine(
                $"Root: HKCR; Subkey: \"{progId}\\shell\\open\\command\"; ValueType: string; ValueName: \"\"; ValueData: \"\"\"{{app}}\\{AppExecName}\"\" \"\"%1\"\"\"; Tasks: associatefiles");
        }

        // Context Menu Integration
        if (Configurations.ContextMenuIntegration)
        {
            var menuText = Configurations.ContextMenuText.IsStringNullOrEmpty()
                ? $"Open with {Configurations.AppFriendlyName}"
                : Configurations.ContextMenuText;

            sb.AppendLine(
                $"Root: HKCR; Subkey: \"*\\shell\\{Configurations.AppBaseName}\"; ValueType: string; ValueName: \"\"; ValueData: \"{menuText}\"; Flags: uninsdeletekey; Tasks: contextmenu");
            sb.AppendLine(
                $"Root: HKCR; Subkey: \"*\\shell\\{Configurations.AppBaseName}\\command\"; ValueType: string; ValueName: \"\"; ValueData: \"\"\"{{app}}\\{AppExecName}\"\" \"\"%1\"\"\"; Tasks: contextmenu");
            sb.AppendLine(
                $"Root: HKCR; Subkey: \"*\\shell\\{Configurations.AppBaseName}\"; ValueType: string; ValueName: \"Icon\"; ValueData: \"{{app}}\\{iconFileName},0\"; Tasks: contextmenu");
        }

        sb.AppendLine();
    }

    private void GenerateIconsSection(StringBuilder sb, string iconFileName)
    {
        sb.AppendLine("[Icons]");

        // Quick Launch Icon
        sb.AppendLine(
            $"Name: \"{{userappdata}}\\Microsoft\\Internet Explorer\\Quick Launch\\{Configurations.AppFriendlyName}\"; Filename: \"{{app}}\\{AppExecName}\"; IconFilename: \"{{app}}\\{iconFileName}\"; Tasks: quicklaunchicon");

        if (!Configurations.DesktopNoDisplay)
        {
            // Start Menu Icon
            sb.AppendLine($"Name: \"{{group}}\\{Configurations.AppFriendlyName}\"; Filename: \"{{app}}\\{AppExecName}\"; IconFilename: \"{{app}}\\{iconFileName}\"");

            // Desktop Icon
            sb.AppendLine(
                $"Name: \"{{userdesktop}}\\{Configurations.AppFriendlyName}\"; Filename: \"{{app}}\\{AppExecName}\"; IconFilename: \"{{app}}\\{iconFileName}\"; Tasks: desktopicon");
        }

        // Startup Icon
        sb.AppendLine(
            $"Name: \"{{userstartup}}\\{Configurations.AppFriendlyName}\"; Filename: \"{{app}}\\{AppExecName}\"; IconFilename: \"{{app}}\\{iconFileName}\"; Tasks: startup");

        // Uninstaller Icon
        sb.AppendLine($"Name: \"{{group}}\\Uninstall {Configurations.AppFriendlyName}\"; Filename: \"{{uninstallexe}}\"");

        // Command Prompt
        if (!Configurations.SetupCommandPrompt.IsStringNullOrEmpty())
        {
            // Give special terminal icon rather meaningless default .bat icon
            var name = Path.GetFileName(TerminalIcon);
            sb.AppendLine($"Name: \"{{group}}\\{Configurations.SetupCommandPrompt}\"; Filename: \"{{app}}\\{PromptBat}\"; IconFilename: \"{{app}}\\{name}\"");
        }

        // Publisher Link
        if (!Configurations.PublisherLinkName.IsStringNullOrEmpty() && !Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
            sb.AppendLine($"Name: \"{{group}}\\{Configurations.PublisherLinkName}\"; Filename: \"{Configurations.PublisherLinkUrl}\"");

        sb.AppendLine();
    }

    private void GenerateRunSection(StringBuilder sb)
    {
        sb.AppendLine("[Run]");

        if (!Configurations.DesktopNoDisplay)
            sb.AppendLine($"Filename: \"{{app}}\\{AppExecName}\"; Description: Start Application Now; Flags: postinstall nowait skipifsilent");

        sb.AppendLine();
    }

    private static void GenerateInstallDeleteSection(StringBuilder sb)
    {
        sb.AppendLine("[InstallDelete]");
        sb.AppendLine("Type: filesandordirs; Name: \"{app}\\*\";");
        sb.AppendLine("Type: filesandordirs; Name: \"{group}\\*\";");
        sb.AppendLine();
    }

    private void GenerateUninstallRunSection(StringBuilder sb)
    {
        sb.AppendLine("[UninstallRun]");

        if (!Configurations.SetupUninstallScript.IsStringNullOrEmpty())
        {
            var uninstallScriptPath = $"{{app}}\\{Configurations.SetupUninstallScript}";
            sb.AppendLine($"Filename: \"{uninstallScriptPath}\"; Flags: runhidden waituntilterminated");
        }

        sb.AppendLine();
    }

    private static void GenerateUninstallDeleteSection(StringBuilder sb)
    {
        sb.AppendLine("[UninstallDelete]");
        sb.AppendLine("Type: dirifempty; Name: \"{app}\"");
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

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start Inno Setup compiler.");
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