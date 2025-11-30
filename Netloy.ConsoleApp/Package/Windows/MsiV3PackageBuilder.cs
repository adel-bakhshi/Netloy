using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Windows;

public class MsiV3PackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string WixDownloadUrl = "https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314-binaries.zip";
    private const string WixNamespace = "http://schemas.microsoft.com/wix/2006/wi";
    private const string CandleExe = "candle.exe";
    private const string LightExe = "light.exe";

    private static readonly string[] WixRequiredFiles =
    [
        "candle.exe",
        "candle.exe.config",
        "darice.cub",
        "light.exe",
        "light.exe.config",
        "wconsole.dll",
        "winterop.dll",
        "wix.dll",
        "WixUIExtension.dll",
        "WixUtilExtension.dll"
    ];

    #endregion

    #region Private Fields

    private readonly List<string> _fileComponentIds;

    #endregion

    #region Properties

    public string PublishOutputDir { get; private set; } = string.Empty;
    public string WixSourcePath { get; }
    public string WixToolsPath { get; }
    public string IntermediatesPath { get; }
    public string TerminalIcon { get; }
    public string RegistryKeyPathRoot { get; }

    #endregion

    public MsiV3PackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        _fileComponentIds = [];

        WixToolsPath = Path.Combine(NetloyTempPath, "wix-v3");
        IntermediatesPath = Path.Combine(RootDirectory, "wix-intermediates");
        WixSourcePath = Path.Combine(IntermediatesPath, $"{Configurations.AppBaseName}.wxs");
        TerminalIcon = Path.Combine(Constants.NetloyAppDir, "Assets", "terminal.ico");
        RegistryKeyPathRoot = Configurations.SetupAdminInstall ? "HKLM" : "HKCU";
    }

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting Windows MSI package build (WiX v3)...", forceLog: true);

        // Ensure WiX v3 tools are available
        await EnsureWixToolsInstalledAsync();

        // Publish project
        PublishOutputDir = Path.Combine(RootDirectory, "publish");
        await PublishAsync(PublishOutputDir);

        // Create intermediates directory
        if (Directory.Exists(IntermediatesPath))
            Directory.Delete(IntermediatesPath, true);

        Directory.CreateDirectory(IntermediatesPath);

        // Generate WiX source file
        Logger.LogInfo("Generating WiX source file...");
        await GenerateWixSourceAsync();

        // Build MSI with WiX
        Logger.LogInfo("Building MSI with WiX Toolset v3...");
        await RunCandleAsync();
        await RunLightAsync();

        Logger.LogSuccess("Windows MSI package build completed successfully!");
    }

    public bool Validate()
    {
        var errors = new List<string>();

        // Validate wizard images
        if (!Configurations.MsiUiBanner.IsStringNullOrEmpty() && !File.Exists(Configurations.MsiUiBanner))
            errors.Add($"MSI UI banner file not found: {Configurations.MsiUiBanner}");

        if (!Configurations.MsiUiDialog.IsStringNullOrEmpty() && !File.Exists(Configurations.MsiUiDialog))
            errors.Add($"MSI UI dialog file not found: {Configurations.MsiUiDialog}");

        // File association and context menu require admin
        if ((Configurations.AssociateFiles || Configurations.ContextMenuIntegration) && !Configurations.SetupAdminInstall)
            errors.Add($"You must set {nameof(Configurations.SetupAdminInstall)} to true if you want to associate files or add context menu items.");

        // SetupUninstallScript validation
        if (!Configurations.SetupUninstallScript.IsStringNullOrEmpty() && !File.Exists(Configurations.SetupUninstallScript))
            errors.Add($"Setup uninstall script file not found: {Configurations.SetupUninstallScript}");


        if (!Configurations.MsiUpgradeCode.IsStringNullOrEmpty() && !Guid.TryParse(Configurations.MsiUpgradeCode, out _))
            errors.Add($"Invalid MsiUpgradeCode: {Configurations.MsiUpgradeCode}. Must be a valid GUID.");

        var icoIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".ico", StringComparison.OrdinalIgnoreCase));
        if (icoIcon.IsStringNullOrEmpty() || !File.Exists(icoIcon))
            errors.Add($"Couldn't find icon file. Icon path: The ico file is required for building {Arguments.PackageType.ToString()?.ToUpperInvariant()} package.");

        if (errors.Count > 0)
        {
            var errorMessage = $"The following errors were found:\n\n{string.Join("\n", errors)}";
            throw new InvalidOperationException(errorMessage);
        }

        return true;
    }

    #region WiX Tools Management

    private async Task EnsureWixToolsInstalledAsync()
    {
        if (Directory.Exists(WixToolsPath) && WixRequiredFiles.All(f => File.Exists(Path.Combine(WixToolsPath, f))))
        {
            Logger.LogInfo("WiX v3 tools already installed at: {0}", WixToolsPath);
            return;
        }

        Logger.LogInfo("Downloading WiX v3 tools...");

        if (Directory.Exists(WixToolsPath))
            Directory.Delete(WixToolsPath, true);

        Directory.CreateDirectory(WixToolsPath);

        using var client = new HttpClient();
        var zipBytes = await client.GetByteArrayAsync(WixDownloadUrl);

        Logger.LogInfo("Extracting WiX v3 tools...");
        ZipFile.ExtractToDirectory(new MemoryStream(zipBytes), WixToolsPath);

        Logger.LogSuccess("WiX v3 tools installed successfully!");
    }

    #endregion

    #region WiX Source Generation

    private async Task GenerateWixSourceAsync()
    {
        var primaryIcon = MacroExpander.GetMacroValue(MacroId.PrimaryIconFilePath);

        // Create wix content
        var wixContent = CreateWixXml(primaryIcon);

        await File.WriteAllTextAsync(WixSourcePath, wixContent.ToString(), Constants.Utf8WithoutBom);
        Logger.LogInfo("WiX source saved to: {0}", WixSourcePath);
    }

    private XDocument CreateWixXml(string primaryIcon)
    {
        var packageArch = GetWindowsPackageArch();

        var isWin64 = !packageArch.Equals("x86", StringComparison.OrdinalIgnoreCase);
        var win64 = isWin64 ? "yes" : "no";
        var programFilesFolder = isWin64 ? "ProgramFiles64Folder" : "ProgramFilesFolder";

        var product = CreateProductElement(primaryIcon);
        GenerateAdvancedSetupFeatures(product);

        return new XDocument(
            new XProcessingInstruction("define", $"Win64=\"{win64}\""),
            new XProcessingInstruction("define", $"PlatformProgramFilesFolder=\"{programFilesFolder}\""),
            new XElement(XName.Get("Wix", WixNamespace), product));
    }

    private XElement CreateProductElement(string primaryIcon)
    {
        var upgradeCode = GenerateUpgradeCode();
        var pathComponentGuid = GenerateComponentGuid("PathComponent");

        var product = new XElement(XName.Get("Product", WixNamespace),
            new XAttribute("Id", "*"),
            new XAttribute("Name", Configurations.AppFriendlyName),
            new XAttribute("UpgradeCode", upgradeCode),
            new XAttribute("Language", "1033"),
            new XAttribute("Manufacturer", Configurations.PublisherName),
            new XAttribute("Version", ConvertVersion(AppVersion)));

        // Package
        var package = new XElement(XName.Get("Package", WixNamespace),
            new XAttribute("Id", "*"),
            new XAttribute("Keywords", "Installer"),
            new XAttribute("InstallerVersion", "450"),
            new XAttribute("Languages", "0"),
            new XAttribute("Compressed", "yes"),
            new XAttribute("InstallScope", Configurations.SetupAdminInstall ? "perMachine" : "perUser"),
            new XAttribute("SummaryCodepage", "1252"));

        if (!Configurations.AppShortSummary.IsStringNullOrEmpty())
            package.Add(new XAttribute("Description", Configurations.AppShortSummary));

        product.Add(package);

        // MajorUpgrade
        product.Add(new XElement(XName.Get("MajorUpgrade", WixNamespace),
            new XAttribute("Schedule", "afterInstallInitialize"),
            new XAttribute("DowngradeErrorMessage", "A newer version of [ProductName] is already installed."),
            new XAttribute("AllowSameVersionUpgrades", "no")));

        // InstallExecuteSequence
        var installExec = new XElement(XName.Get("InstallExecuteSequence", WixNamespace));
        installExec.Add(new XElement(XName.Get("RemoveShortcuts", WixNamespace), "Installed AND NOT UPGRADINGPRODUCTCODE"));
        product.Add(installExec);

        // Media
        product.Add(new XElement(XName.Get("Media", WixNamespace),
            new XAttribute("Id", "1"),
            new XAttribute("Cabinet", $"{Configurations.AppBaseName}.cab"),
            new XAttribute("EmbedCab", "yes")));

        // Icon
        product.Add(new XElement(XName.Get("Icon", WixNamespace),
            new XAttribute("Id", "ProductIcon"),
            new XAttribute("SourceFile", primaryIcon)));

        product.Add(new XElement(XName.Get("Property", WixNamespace),
            new XAttribute("Id", "ARPPRODUCTICON"),
            new XAttribute("Value", "ProductIcon")));

        if (!Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
        {
            product.Add(new XElement(XName.Get("Property", WixNamespace),
                new XAttribute("Id", "ARPHELPLINK"),
                new XAttribute("Value", Configurations.PublisherLinkUrl)));
        }

        product.Add(new XElement(XName.Get("Property", WixNamespace),
            new XAttribute("Id", "ARPNOREPAIR"),
            new XAttribute("Value", "yes"),
            new XAttribute("Secure", "yes")));

        // Initialize with previous InstallDir
        var installDirProperty = new XElement(XName.Get("Property", WixNamespace),
            new XAttribute("Id", "INSTALLDIR"));

        installDirProperty.Add(new XElement(XName.Get("RegistrySearch", WixNamespace),
            new XAttribute("Id", "PrevInstallDirReg"),
            new XAttribute("Root", "HKCU"),
            new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"),
            new XAttribute("Name", "InstallDir"),
            new XAttribute("Type", "raw")));

        product.Add(installDirProperty);

        // Launch app checkbox
        product.Add(new XElement(XName.Get("Property", WixNamespace),
            new XAttribute("Id", "WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT"),
            new XAttribute("Value", $"Launch {Configurations.AppFriendlyName}")));

        product.Add(new XElement(XName.Get("Property", WixNamespace),
            new XAttribute("Id", "WIXUI_EXITDIALOGOPTIONALCHECKBOX"),
            new XAttribute("Value", "1")));

        product.Add(new XElement(XName.Get("Property", WixNamespace),
            new XAttribute("Id", "WixShellExecTarget"),
            new XAttribute("Value", "[!ApplicationExeFile]")));

        product.Add(new XElement(XName.Get("CustomAction", WixNamespace),
            new XAttribute("Id", "LaunchApplication"),
            new XAttribute("BinaryKey", "WixCA"),
            new XAttribute("DllEntry", "WixShellExec"),
            new XAttribute("Impersonate", "yes")));

        // WixVariables for custom images
        if (!Configurations.MsiUiBanner.IsStringNullOrEmpty() && File.Exists(Configurations.MsiUiBanner))
        {
            product.Add(new XElement(XName.Get("WixVariable", WixNamespace),
                new XAttribute("Id", "WixUIBannerBmp"),
                new XAttribute("Value", Configurations.MsiUiBanner)));
        }

        if (!Configurations.MsiUiDialog.IsStringNullOrEmpty() && File.Exists(Configurations.MsiUiDialog))
        {
            product.Add(new XElement(XName.Get("WixVariable", WixNamespace),
                new XAttribute("Id", "WixUIDialogBmp"),
                new XAttribute("Value", Configurations.MsiUiDialog)));
        }

        if (!Configurations.AppLicenseFile.IsStringNullOrEmpty() && File.Exists(Configurations.AppLicenseFile))
        {
            var licensePath = Configurations.AppLicenseFile;
            if (!licensePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                licensePath = ConvertLicenseToRtf(licensePath);

            product.Add(new XElement(XName.Get("WixVariable", WixNamespace),
                new XAttribute("Id", "WixUILicenseRtf"),
                new XAttribute("Value", licensePath)));
        }

        // UI
        var ui = CreateUiElement();
        product.Add(ui);

        // Directory Structure
        product.Add(CreateDirectoryStructure(pathComponentGuid));

        // Feature
        product.Add(CreateFeatureElement());

        return product;
    }

    private XElement CreateUiElement()
    {
        var ui = new XElement(XName.Get("UI", WixNamespace));

        // Use WixUI_Mondo dialog set
        ui.Add(new XElement(XName.Get("UIRef", WixNamespace),
            new XAttribute("Id", "WixUI_Mondo")));

        // Launch app checkbox
        ui.Add(new XElement(XName.Get("Publish", WixNamespace),
            new XAttribute("Dialog", "ExitDialog"),
            new XAttribute("Control", "Finish"),
            new XAttribute("Event", "DoAction"),
            new XAttribute("Value", "LaunchApplication"),
            "WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed"));

        // Skip license dialog if no license
        if (Configurations.AppLicenseFile.IsStringNullOrEmpty())
        {
            // WelcomeDlg -> SetupTypeDlg
            ui.Add(new XElement(XName.Get("Publish", WixNamespace),
                new XAttribute("Dialog", "WelcomeDlg"),
                new XAttribute("Control", "Next"),
                new XAttribute("Event", "NewDialog"),
                new XAttribute("Value", "SetupTypeDlg"),
                new XAttribute("Order", "3"),
                "1"));

            // SetupTypeDlg -> WelcomeDlg
            ui.Add(new XElement(XName.Get("Publish", WixNamespace),
                new XAttribute("Dialog", "SetupTypeDlg"),
                new XAttribute("Control", "Back"),
                new XAttribute("Event", "NewDialog"),
                new XAttribute("Value", "WelcomeDlg"),
                new XAttribute("Order", "3"),
                "1"));
        }

        return ui;
    }

    private XElement CreateDirectoryStructure(string pathComponentGuid)
    {
        var installDirName = !Configurations.SetupGroupName.IsStringNullOrEmpty()
            ? Configurations.SetupGroupName
            : Configurations.AppBaseName;

        var targetDir = new XElement(XName.Get("Directory", WixNamespace),
            new XAttribute("Id", "TARGETDIR"),
            new XAttribute("Name", "SourceDir"));

        // Desktop folder
        var desktopFolder = new XElement(XName.Get("Directory", WixNamespace),
            new XAttribute("Id", "DesktopFolder"),
            new XAttribute("Name", "Desktop"));

        if (!Configurations.DesktopNoDisplay)
            desktopFolder.Add(CreateDesktopShortcutComponent());

        targetDir.Add(desktopFolder);

        // Program Files folder
        var programFilesDir = new XElement(XName.Get("Directory", WixNamespace),
            new XAttribute("Id", "$(var.PlatformProgramFilesFolder)"),
            new XAttribute("Name", "PFiles"));

        var installDir = new XElement(XName.Get("Directory", WixNamespace),
            new XAttribute("Id", "INSTALLDIR"),
            new XAttribute("Name", installDirName));

        // Add components to INSTALLDIR
        installDir.Add(CreateRegistryComponent());
        installDir.Add(CreateMainExeComponent(pathComponentGuid));
        installDir.Add(CreateUninstallShortcutComponent());

        // Context Menu Component
        if (Configurations.ContextMenuIntegration)
            installDir.Add(CreateContextMenuComponent());

        // Startup Component
        if (Configurations.SetupStartOnWindowsStartup)
            installDir.Add(CreateStartupComponent());

        // Add application files
        foreach (var file in Directory.GetFiles(PublishOutputDir, "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals(AppExecName, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileComponent = CreateFileComponent(file);
            var componentId = fileComponent.Attribute("Id")?.Value;
            if (!string.IsNullOrEmpty(componentId))
                _fileComponentIds.Add(componentId);

            installDir.Add(fileComponent);
        }

        GenerateFilesSection(installDir);

        programFilesDir.Add(installDir);
        targetDir.Add(programFilesDir);

        // Program Menu folder
        var programMenuFolder = new XElement(XName.Get("Directory", WixNamespace),
            new XAttribute("Id", "ProgramMenuFolder"));

        var appProgramsFolder = new XElement(XName.Get("Directory", WixNamespace),
            new XAttribute("Id", "ApplicationProgramsFolder"),
            new XAttribute("Name", Configurations.AppFriendlyName));

        appProgramsFolder.Add(CreateStartMenuShortcutComponent());

        programMenuFolder.Add(appProgramsFolder);
        targetDir.Add(programMenuFolder);

        return targetDir;
    }

    private XElement CreateMainExeComponent(string guid)
    {
        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "MainExecutableComponent"),
            new XAttribute("Guid", guid),
            new XAttribute("Win64", "$(var.Win64)"));

        var file = new XElement(XName.Get("File", WixNamespace),
            new XAttribute("Id", "ApplicationExeFile"),
            new XAttribute("Source", Path.Combine(PublishOutputDir, AppExecName)),
            new XAttribute("KeyPath", "yes"),
            new XAttribute("Checksum", "yes"));

        component.Add(file);

        if (Configurations.AssociateFiles && !Configurations.FileExtension.IsStringNullOrEmpty())
        {
            var ext = Configurations.FileExtension.StartsWith('.')
                ? Configurations.FileExtension
                : $".{Configurations.FileExtension}";

            var progId = $"{Configurations.AppBaseName}File";

            // Registry for extension
            var extKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", ext));

            extKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", progId)));

            // Registry for ProgId
            var progIdKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", progId));

            progIdKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", $"{Configurations.AppFriendlyName} File")));

            // Registry for command
            var commandKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", $"{progId}\\shell\\open\\command"));

            commandKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", $"\"[INSTALLDIR]{AppExecName}\" \"%1\"")));

            component.Add(extKey);
            component.Add(progIdKey);
            component.Add(commandKey);
        }

        return component;
    }

    private static XElement CreateFileComponent(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var sanitizedName = Regex.Replace(fileName, @"[^\w\d\.]", "_");

        // Generate IDs and ensure they don't exceed 72 characters
        var componentId = $"File_{sanitizedName}_{Guid.NewGuid():N}";
        componentId = componentId.Substring(0, Math.Min(componentId.Length, 72));

        var fileId = $"F_{sanitizedName}";
        fileId = fileId.Substring(0, Math.Min(fileId.Length, 72));

        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", componentId),
            new XAttribute("Guid", "*"),
            new XAttribute("Win64", "$(var.Win64)"));

        component.Add(new XElement(XName.Get("File", WixNamespace),
            new XAttribute("Id", fileId),
            new XAttribute("Source", filePath),
            new XAttribute("KeyPath", "yes")));

        return component;
    }

    private XElement CreateRegistryComponent()
    {
        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "RegistryEntriesComponent"),
            new XAttribute("Guid", GenerateComponentGuid("RegistryEntries")));

        if (Configurations.SetupAdminInstall)
        {
            component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Root", "HKLM"),
                new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"),
                new XAttribute("Name", "Installed"),
                new XAttribute("Type", "integer"),
                new XAttribute("Value", "1"),
                new XAttribute("KeyPath", "yes")));
        }
        else
        {
            var regKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCU"),
                new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"));

            regKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Name", "InstallDir"),
                new XAttribute("Type", "string"),
                new XAttribute("Value", "INSTALLDIR"),
                new XAttribute("KeyPath", "yes")));

            component.Add(regKey);
        }

        return component;
    }

    private XElement CreateContextMenuComponent()
    {
        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "ContextMenuComponent"),
            new XAttribute("Guid", "*"));

        // Context menu HKCR (admin)
        var contextKey = new XElement(XName.Get("RegistryKey", WixNamespace),
            new XAttribute("Root", "HKCR"),
            new XAttribute("Key", $"*\\shell\\{Configurations.AppBaseName}"));

        contextKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Type", "string"),
            new XAttribute("Value", $"Open with {Configurations.AppFriendlyName}")));

        contextKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Name", "Icon"),
            new XAttribute("Type", "string"),
            new XAttribute("Value", $"\"[INSTALLDIR]{AppExecName}\",0")));

        // Command
        var cmdKey = new XElement(XName.Get("RegistryKey", WixNamespace),
            new XAttribute("Key", "command"));

        cmdKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Type", "string"),
            new XAttribute("Value", $"\"[INSTALLDIR]{AppExecName}\" \"%1\"")));

        contextKey.Add(cmdKey);
        component.Add(contextKey);

        // KeyPath
        component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Root", "HKLM"),
            new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"),
            new XAttribute("Name", "ContextMenu"),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes")));

        return component;
    }

    private XElement CreateStartupComponent()
    {
        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "StartupComponent"),
            new XAttribute("Guid", "*"));

        var regKey = new XElement(XName.Get("RegistryKey", WixNamespace),
            new XAttribute("Root", RegistryKeyPathRoot),
            new XAttribute("Key", "Software\\Microsoft\\Windows\\CurrentVersion\\Run"));

        regKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Type", "string"),
            new XAttribute("Name", Configurations.AppBaseName),
            new XAttribute("Value", $"\"[INSTALLDIR]{AppExecName}\"")));

        component.Add(regKey);

        component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Root", RegistryKeyPathRoot),
            new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"),
            new XAttribute("Name", "Startup"),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes")));

        return component;
    }

    private XElement CreateStartMenuShortcutComponent()
    {
        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "StartMenuShortcutComponent"),
            new XAttribute("Guid", "*"));

        var shortcut = new XElement(XName.Get("Shortcut", WixNamespace),
            new XAttribute("Id", "StartMenuShortcut"),
            new XAttribute("Name", Configurations.AppFriendlyName),
            new XAttribute("Description", $"Runs {Configurations.AppFriendlyName}"),
            new XAttribute("Target", "[!ApplicationExeFile]"),
            new XAttribute("Icon", "ProductIcon"),
            new XAttribute("WorkingDirectory", "INSTALLDIR"));

        component.Add(shortcut);

        component.Add(new XElement(XName.Get("RemoveFolder", WixNamespace),
            new XAttribute("Id", "RemoveApplicationProgramsFolder"),
            new XAttribute("On", "uninstall")));

        component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Root", "HKCU"),
            new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"),
            new XAttribute("Name", "StartMenuShortcut"),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes")));

        return component;
    }

    private XElement CreateDesktopShortcutComponent()
    {
        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "DesktopShortcutComponent"),
            new XAttribute("Guid", "*"));

        var shortcut = new XElement(XName.Get("Shortcut", WixNamespace),
            new XAttribute("Id", "DesktopShortcut"),
            new XAttribute("Name", Configurations.AppFriendlyName),
            new XAttribute("Description", $"Runs {Configurations.AppFriendlyName}"),
            new XAttribute("Target", "[!ApplicationExeFile]"),
            new XAttribute("Icon", "ProductIcon"),
            new XAttribute("WorkingDirectory", "INSTALLDIR"));

        component.Add(shortcut);

        component.Add(new XElement(XName.Get("RemoveFolder", WixNamespace),
            new XAttribute("Id", "RemoveDesktopFolder"),
            new XAttribute("On", "uninstall")));

        component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Root", "HKCU"),
            new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"),
            new XAttribute("Name", "DesktopShortcut"),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes")));

        return component;
    }

    private XElement CreateUninstallShortcutComponent()
    {
        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "UninstallShortcutComponent"),
            new XAttribute("Guid", "*"));

        var shortcut = new XElement(XName.Get("Shortcut", WixNamespace),
            new XAttribute("Id", "UninstallShortcut"),
            new XAttribute("Name", $"Uninstall {Configurations.AppFriendlyName}"),
            new XAttribute("Description", $"Uninstalls {Configurations.AppFriendlyName}"),
            new XAttribute("Target", "[System64Folder]msiexec.exe"),
            new XAttribute("Arguments", "/x [ProductCode]"));

        component.Add(shortcut);

        component.Add(new XElement(XName.Get("RemoveFolder", WixNamespace),
            new XAttribute("Id", "RemoveINSTALLDIR"),
            new XAttribute("On", "uninstall")));

        component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Root", RegistryKeyPathRoot),
            new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppFriendlyName}"),
            new XAttribute("Name", "UninstallerShortcut"),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes")));

        return component;
    }

    private XElement CreateFeatureElement()
    {
        var feature = new XElement(XName.Get("Feature", WixNamespace),
            new XAttribute("Id", "MainProgram"),
            new XAttribute("Title", "Application"),
            new XAttribute("Level", "1"),
            new XAttribute("ConfigurableDirectory", "INSTALLDIR"),
            new XAttribute("AllowAdvertise", "no"),
            new XAttribute("Display", "expand"),
            new XAttribute("Absent", "disallow"));

        if (!Configurations.AppShortSummary.IsStringNullOrEmpty())
            feature.Add(new XAttribute("Description", Configurations.AppShortSummary));

        // Main components
        feature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
            new XAttribute("Id", "MainExecutableComponent")));

        feature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
            new XAttribute("Id", "RegistryEntriesComponent")));

        // Add all file components
        foreach (var componentId in _fileComponentIds)
        {
            feature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
                new XAttribute("Id", componentId)));
        }

        // Shortcuts feature - Required
        var shortcutsFeature = new XElement(XName.Get("Feature", WixNamespace),
            new XAttribute("Id", "ShortcutsFeature"),
            new XAttribute("Title", "Shortcuts"),
            new XAttribute("Level", "1"),
            new XAttribute("Absent", "disallow"),
            new XAttribute("Description", "Desktop and Start Menu shortcuts"));

        shortcutsFeature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
            new XAttribute("Id", "StartMenuShortcutComponent")));

        if (!Configurations.DesktopNoDisplay)
        {
            shortcutsFeature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
                new XAttribute("Id", "DesktopShortcutComponent")));
        }

        shortcutsFeature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
            new XAttribute("Id", "UninstallShortcutComponent")));

        feature.Add(shortcutsFeature);

        // Context Menu Feature - Optional
        if (Configurations.ContextMenuIntegration)
        {
            var contextMenuFeature = new XElement(XName.Get("Feature", WixNamespace),
                new XAttribute("Id", "ContextMenuFeature"),
                new XAttribute("Title", "Context Menu Integration"),
                new XAttribute("Level", "1"),
                new XAttribute("Absent", "allow"),
                new XAttribute("Description", "Add 'Open with' to file context menu"));

            contextMenuFeature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
                new XAttribute("Id", "ContextMenuComponent")));

            feature.Add(contextMenuFeature);
        }

        // Startup Feature - Optional
        if (Configurations.SetupStartOnWindowsStartup)
        {
            var startupFeature = new XElement(XName.Get("Feature", WixNamespace),
                new XAttribute("Id", "StartupFeature"),
                new XAttribute("Title", "Start on Windows Startup"),
                new XAttribute("Level", "1"),
                new XAttribute("Absent", "allow"),
                new XAttribute("Description", $"Launch {Configurations.AppFriendlyName} when Windows starts"));

            startupFeature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
                new XAttribute("Id", "StartupComponent")));

            feature.Add(startupFeature);
        }

        // PATH environment variable feature
        if (!Configurations.StartCommand.IsStringNullOrEmpty())
        {
            var pathFeature = new XElement(XName.Get("Feature", WixNamespace),
                new XAttribute("Id", "PathFeature"),
                new XAttribute("Title", "PATH Environment Variable"),
                new XAttribute("Description", $"Add {Configurations.AppFriendlyName} to system PATH"),
                new XAttribute("Level", "1000"),
                new XAttribute("Absent", "allow"));

            pathFeature.Add(new XElement(XName.Get("ComponentRef", WixNamespace),
                new XAttribute("Id", "MainExecutableComponent")));

            feature.Add(pathFeature);
        }

        return feature;
    }

    private void GenerateAdvancedSetupFeatures(XElement product)
    {
        var installExec = product.Element(XName.Get("InstallExecuteSequence", WixNamespace));

        if (!Configurations.SetupPasswordEncryption.IsStringNullOrEmpty())
            Logger.LogWarning("SetupPasswordEncryption ignored - WiX v3 doesn't support Media.Password");

        if (Configurations.SetupCloseApplications)
        {
            product.Add(new XElement(XName.Get("Property", WixNamespace),
                new XAttribute("Id", "CLOSEAPPLICATIONS"),
                new XAttribute("Value", "yes")));
        }

        if (Configurations.SetupRestartIfNeeded)
        {
            installExec?.Add(new XElement(XName.Get("ScheduleReboot", WixNamespace),
                new XAttribute("After", "InstallFinalize")));
        }

        if (!Configurations.SetupUninstallScript.IsStringNullOrEmpty())
        {
            product.Add(new XElement(XName.Get("CustomAction", WixNamespace),
                new XAttribute("Id", "RunUninstallScript"),
                new XAttribute("Directory", "INSTALLDIR"),
                new XAttribute("ExeCommand", Path.GetFileName(Configurations.SetupUninstallScript)),
                new XAttribute("Execute", "deferred"),
                new XAttribute("Return", "ignore"),
                new XAttribute("Impersonate", "no")));

            var uninstallExec = product.Elements(XName.Get("InstallExecuteSequence", WixNamespace))
                .FirstOrDefault(e => e.Attribute("Id")?.Value == "Uninstall")
                ?? new XElement(XName.Get("InstallExecuteSequence", WixNamespace));

            uninstallExec.Add(new XElement(XName.Get("Custom", WixNamespace),
                new XAttribute("Action", "RunUninstallScript"),
                new XAttribute("Before", "RemoveFiles")));

            product.Add(uninstallExec);
        }
    }

    private void GenerateFilesSection(XElement installDir)
    {
        // SetupCommandPrompt batch file
        if (!Configurations.SetupCommandPrompt.IsStringNullOrEmpty())
        {
            var promptBatPath = Path.Combine(PublishOutputDir, "CommandPrompt.bat");
            var script = GenerateCommandPromptScript();
            File.WriteAllText(promptBatPath, script, Constants.Utf8WithoutBom);

            var batComponent = CreateFileComponent(promptBatPath);
            installDir.Add(batComponent);
        }

        // SetupUninstallScript file
        if (!Configurations.SetupUninstallScript.IsStringNullOrEmpty() && File.Exists(Configurations.SetupUninstallScript))
        {
            var uninstallComponent = CreateFileComponent(Configurations.SetupUninstallScript);
            installDir.Add(uninstallComponent);
        }
    }

    private string GenerateCommandPromptScript()
    {
        var title = EscapeBat(Configurations.SetupCommandPrompt);
        var cmd = EscapeBat(!Configurations.StartCommand.IsStringNullOrEmpty() ? Configurations.StartCommand : Configurations.AppBaseName);
        var echoCopy = !Configurations.PublisherCopyright.IsStringNullOrEmpty() ? $"echo {EscapeBat(Configurations.PublisherCopyright)}" : null;

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine($"start cmd /k cd /D \"%%~dp0\"");
        sb.AppendLine($"title \"{title}\"");
        sb.AppendLine($"echo {cmd} {AppVersion}");

        if (echoCopy != null)
            sb.AppendLine(echoCopy);

        sb.AppendLine("set PATH=%PATH%;%~dp0");
        sb.AppendLine(cmd ?? Configurations.AppBaseName);

        return sb.ToString();
    }

    private static string? EscapeBat(string? command)
    {
        if (command.IsStringNullOrEmpty())
            return null;

        return command!.Replace("^", "^^")
            .Replace("&", "^&")
            .Replace("<", "^<")
            .Replace(">", "^>")
            .Replace("|", "^|")
            .Replace("\"", "^\"");
    }

    #endregion

    #region Helper Methods

    private static string ConvertVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 3)
            return version + ".0";

        if (int.TryParse(parts[0], out var major) && major > 255)
            throw new InvalidOperationException("Major version cannot be greater than 255");

        if (int.TryParse(parts[1], out var minor) && minor > 255)
            throw new InvalidOperationException("Minor version cannot be greater than 255");

        if (int.TryParse(parts[2], out var patch) && patch > 65535)
            throw new InvalidOperationException("Patch version cannot be greater than 65535");

        return parts.Length == 4 ? version : $"{version}.0";
    }

    private string GenerateUpgradeCode()
    {
        if (!Configurations.MsiUpgradeCode.IsStringNullOrEmpty())
            return Guid.Parse(Configurations.MsiUpgradeCode).ToString().ToUpperInvariant();

        var hash = MD5.HashData(Constants.Utf8WithoutBom.GetBytes(Configurations.AppId));
        return new Guid(hash).ToString().ToUpperInvariant();
    }

    private string GenerateComponentGuid(string key)
    {
        var bytes = Constants.Utf8WithoutBom.GetBytes($"{Configurations.AppId}_{key}");
        var hash = MD5.HashData(bytes);
        return new Guid(hash).ToString().ToUpperInvariant();
    }

    private string ConvertLicenseToRtf(string licensePath)
    {
        var content = File.ReadAllText(licensePath);
        var rtfContent = @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033{\fonttbl{\f0\fnil\fcharset0 Calibri;}}
{\*\generator Riched20 10.0.18362}\viewkind4\uc1
\pard\sa200\sl276\slmult1\f0\fs22\lang9 " + content.Replace("\n", "\\par ") + @"\par
}";

        var rtfPath = Path.Combine(IntermediatesPath, "LICENSE.rtf");
        File.WriteAllText(rtfPath, rtfContent);
        return rtfPath;
    }

    #endregion

    #region Candle & Light Execution

    private async Task RunCandleAsync()
    {
        var packageArch = GetWindowsPackageArch();

        var candleExe = Path.Combine(WixToolsPath, CandleExe);
        var args = new List<string>
        {
            "-arch", packageArch,
            WixSourcePath,
            "-ext", Path.Combine(WixToolsPath, "WixUIExtension.dll"),
            "-ext", Path.Combine(WixToolsPath, "WixUtilExtension.dll")
        };

        Logger.LogInfo("Running candle.exe...");

        var processInfo = new ProcessStartInfo
        {
            FileName = candleExe,
            Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
            WorkingDirectory = IntermediatesPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ClearEnvironmentForWix(processInfo);

        using var process = Process.Start(processInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (Arguments.Verbose && !string.IsNullOrEmpty(output))
            Logger.LogInfo("Candle output:\n{0}", output);

        if (process.ExitCode != 0)
        {
            var errorMsg = !string.IsNullOrEmpty(error) ? error : output;
            throw new InvalidOperationException($"Candle failed with exit code {process.ExitCode}:\n{errorMsg}");
        }
    }

    private async Task RunLightAsync()
    {
        var lightExe = Path.Combine(WixToolsPath, LightExe);
        var outputFile = Path.Combine(OutputDirectory, OutputName);

        var args = new List<string>
        {
            "-o", outputFile,
            "-ext", Path.Combine(WixToolsPath, "WixUIExtension.dll"),
            "-ext", Path.Combine(WixToolsPath, "WixUtilExtension.dll"),
            "*.wixobj"
        };

        Logger.LogInfo("Running light.exe...");

        var processInfo = new ProcessStartInfo
        {
            FileName = lightExe,
            Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
            WorkingDirectory = IntermediatesPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ClearEnvironmentForWix(processInfo);

        using var process = Process.Start(processInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (Arguments.Verbose && !string.IsNullOrEmpty(output))
            Logger.LogInfo("Light output:\n{0}", output);

        if (process.ExitCode != 0)
        {
            var errorMsg = !string.IsNullOrEmpty(error) ? error : output;
            throw new InvalidOperationException($"Light failed with exit code {process.ExitCode}:\n{errorMsg}");
        }

        Logger.LogSuccess("MSI created successfully: {0}", outputFile);
    }

    private static void ClearEnvironmentForWix(ProcessStartInfo processInfo)
    {
        processInfo.Environment.Clear();

        foreach (var varName in new[] { "SYSTEMROOT", "TMP", "TEMP" })
        {
            var value = Environment.GetEnvironmentVariable(varName);
            if (!value.IsStringNullOrEmpty())
                processInfo.Environment[varName] = value;
        }

        // Remove problematic variables
        processInfo.Environment.Remove("DOTNET_STARTUP_HOOKS");
    }

    #endregion
}