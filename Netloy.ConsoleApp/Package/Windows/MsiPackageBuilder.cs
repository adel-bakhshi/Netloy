using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Windows;

public class MsiPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string WixCompiler = "wix";
    private const string WixNamespace = "http://wixtoolset.org/schemas/v4/wxs";

    #endregion

    #region Properties

    public string PublishOutputDir { get; private set; } = string.Empty;
    public string WixSourcePath { get; }

    #endregion

    public MsiPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        WixSourcePath = Path.Combine(RootDirectory, $"{Configurations.AppBaseName}.wxs");
    }

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting Windows MSI package build...", forceLog: true);

        // Publish project
        PublishOutputDir = Path.Combine(RootDirectory, "publish");
        await PublishAsync(PublishOutputDir);

        // Generate WiX source file
        Logger.LogInfo("Generating WiX source file...");
        var wixContent = GenerateWixSource();

        // Save WiX source to file
        await File.WriteAllTextAsync(WixSourcePath, wixContent, Encoding.UTF8);
        Logger.LogInfo("WiX source saved to: {0}", WixSourcePath);

        // Build MSI with WiX
        Logger.LogInfo("Building MSI with WiX Toolset...");
        CompileWixSource();

        Logger.LogSuccess("Windows MSI package build completed successfully!");
    }

    public bool Validate()
    {
        var errors = new List<string>();

        // Check if WiX is installed
        if (!IsWixInstalled())
            errors.Add($"WiX Toolset (wix.exe) not found. Please install WiX Toolset v4+ from {Constants.WixDownloadUrl}");

        // Validate wizard images
        if (!Configurations.MsiUiBanner.IsStringNullOrEmpty() && !File.Exists(Configurations.MsiUiBanner))
            errors.Add($"MSI UI banner file not found: {Configurations.MsiUiBanner}");

        if (!Configurations.MsiUiDialog.IsStringNullOrEmpty() && !File.Exists(Configurations.MsiUiDialog))
            errors.Add($"MSI UI dialog file not found: {Configurations.MsiUiDialog}");

        // File association and context menu require admin
        if ((Configurations.AssociateFiles || Configurations.ContextMenuIntegration) && !Configurations.SetupAdminInstall)
            errors.Add($"You must set {nameof(Configurations.SetupAdminInstall)} to true if you want to associate files or add context menu items.");

        var icoIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".ico", StringComparison.OrdinalIgnoreCase));
        if (icoIcon.IsStringNullOrEmpty() || !File.Exists(icoIcon))
            errors.Add($"Couldn't find icon file. Icon path: The ico file is required for building {Arguments.PackageType.ToString()?.ToUpperInvariant()} package.");
        
        if (!Configurations.MsiUpgradeCode.IsStringNullOrEmpty() && !Guid.TryParse(Configurations.MsiUpgradeCode, out _))
            errors.Add($"Invalid MsiUpgradeCode: {Configurations.MsiUpgradeCode}. Must be a valid GUID.");

        if (errors.Count > 0)
        {
            var errorMessage = $"The following errors were found:\n\n{string.Join("\n", errors)}";
            throw new InvalidOperationException(errorMessage);
        }

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

    private static bool IsWixInstalled()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = WixCompiler,
                Arguments = "--version",
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

    private string GenerateWixSource()
    {
        // Get primary icon
        var primaryIcon = MacroExpander.GetMacroValue(MacroId.PrimaryIconFilePath);

        // Create WiX XML document
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            CreateWixElement(primaryIcon!)
        );

        return doc.ToString();
    }

    private XElement CreateWixElement(string primaryIcon)
    {
        var wix = new XElement(XName.Get("Wix", WixNamespace));

        // Add Package
        wix.Add(CreatePackageElement(primaryIcon));

        // Add Fragments
        wix.Add(CreateMainComponentsFragment());
        wix.Add(CreateApplicationFilesFragment());

        if (Configurations.AssociateFiles || Configurations.ContextMenuIntegration)
            wix.Add(CreateRegistryComponentsFragment());

        if (!Configurations.StartCommand.IsStringNullOrEmpty())
            wix.Add(CreatePathEnvironmentFragment());

        return wix;
    }

    private XElement CreatePackageElement(string primaryIcon)
    {
        var package = new XElement(XName.Get("Package", WixNamespace),
            new XAttribute("Name", Configurations.AppFriendlyName),
            new XAttribute("Manufacturer", Configurations.PublisherName),
            new XAttribute("Version", AppVersion),
            new XAttribute("UpgradeCode", GenerateUpgradeCode()));

        // Language
        package.Add(new XAttribute("Language", "1033"));

        // MajorUpgrade
        package.Add(new XElement(XName.Get("MajorUpgrade", WixNamespace),
            new XAttribute("DowngradeErrorMessage", "A newer version of [ProductName] is already installed."),
            new XAttribute("AllowSameVersionUpgrades", "yes")));

        // MediaTemplate
        package.Add(new XElement(XName.Get("MediaTemplate", WixNamespace),
            new XAttribute("EmbedCab", "yes")));

        // Icon
        package.Add(new XElement(XName.Get("Icon", WixNamespace),
            new XAttribute("Id", "AppIcon"),
            new XAttribute("SourceFile", primaryIcon)));

        // Properties
        package.Add(new XElement(XName.Get("Property", WixNamespace),
            new XAttribute("Id", "ARPPRODUCTICON"),
            new XAttribute("Value", "AppIcon")));

        // Wix can't build if this property was added
        //if (Configurations.SetupAdminInstall)
        //{
        //    package.Add(new XElement(XName.Get("Property", WixNamespace),
        //        new XAttribute("Id", "ALLUSERS"),
        //        new XAttribute("Value", "1")));
        //}

        if (Configurations.SetupAdminInstall)
        {
            package.Add(new XAttribute("Scope", "perMachine"));
        }
        else
        {
            package.Add(new XAttribute("Scope", "perUser"));
        }

        if (!Configurations.PublisherLinkUrl.IsStringNullOrEmpty())
        {
            package.Add(new XElement(XName.Get("Property", WixNamespace),
                new XAttribute("Id", "ARPHELPLINK"),
                new XAttribute("Value", Configurations.PublisherLinkUrl)));
        }

        if (!Configurations.AppShortSummary.IsStringNullOrEmpty())
        {
            package.Add(new XElement(XName.Get("Property", WixNamespace),
                new XAttribute("Id", "ARPCOMMENTS"),
                new XAttribute("Value", Configurations.AppShortSummary)));
        }

        // Directory Structure
        package.Add(CreateDirectoryStructure());

        // Feature
        package.Add(CreateFeatureElement());

        // UI
        package.Add(CreateUiElement());

        // // WixVariables
        // if (!Configurations.AppLicenseFile.IsStringNullOrEmpty() && File.Exists(Configurations.AppLicenseFile))
        // {
        //     package.Add(new XElement(XName.Get("WixVariable", WixNamespace),
        //         new XAttribute("Id", "WixUILicenseRtf"),
        //         new XAttribute("Value", Configurations.AppLicenseFile)));
        // }

        if (!Configurations.MsiUiBanner.IsStringNullOrEmpty() && File.Exists(Configurations.MsiUiBanner))
        {
            package.Add(new XElement(XName.Get("WixVariable", WixNamespace),
                new XAttribute("Id", "WixUIBannerBmp"),
                new XAttribute("Value", Configurations.MsiUiBanner)));
        }

        if (!Configurations.MsiUiDialog.IsStringNullOrEmpty() && File.Exists(Configurations.MsiUiDialog))
        {
            package.Add(new XElement(XName.Get("WixVariable", WixNamespace),
                new XAttribute("Id", "WixUIDialogBmp"),
                new XAttribute("Value", Configurations.MsiUiDialog)));
        }

        return package;
    }

    private XElement CreateDirectoryStructure()
    {
        var programFilesDir = Configurations.SetupAdminInstall ? "ProgramFiles64Folder" : "LocalAppDataFolder";
        var installDirName = !Configurations.SetupGroupName.IsStringNullOrEmpty()
            ? Configurations.SetupGroupName
            : Configurations.AppBaseName;

        var standardDir = new XElement(XName.Get("StandardDirectory", WixNamespace),
            new XAttribute("Id", programFilesDir));

        var installDir = new XElement(XName.Get("Directory", WixNamespace),
            new XAttribute("Id", "INSTALLFOLDER"),
            new XAttribute("Name", installDirName));

        standardDir.Add(installDir);
        return standardDir;
    }

    private XElement CreateFeatureElement()
    {
        var feature = new XElement(XName.Get("Feature", WixNamespace),
            new XAttribute("Id", "MainFeature"),
            new XAttribute("Title", Configurations.AppFriendlyName),
            new XAttribute("Level", "1"));

        if (!Configurations.AppShortSummary.IsStringNullOrEmpty())
            feature.Add(new XAttribute("Description", Configurations.AppShortSummary));

        feature.Add(new XElement(XName.Get("ComponentGroupRef", WixNamespace),
            new XAttribute("Id", "MainComponents")));

        feature.Add(new XElement(XName.Get("ComponentGroupRef", WixNamespace),
            new XAttribute("Id", "ApplicationFiles")));

        if (Configurations.AssociateFiles || Configurations.ContextMenuIntegration)
        {
            feature.Add(new XElement(XName.Get("ComponentGroupRef", WixNamespace),
                new XAttribute("Id", "RegistryComponents")));
        }

        // Add PATH feature if StartCommand is defined
        if (!Configurations.StartCommand.IsStringNullOrEmpty())
        {
            var pathFeature = new XElement(XName.Get("Feature", WixNamespace),
                new XAttribute("Id", "PathFeature"),
                new XAttribute("Title", "Add to PATH"),
                new XAttribute("Description", $"Add {Configurations.AppFriendlyName} to system PATH"),
                new XAttribute("Level", "1000"));

            pathFeature.Add(new XElement(XName.Get("ComponentGroupRef", WixNamespace),
                new XAttribute("Id", "PathComponents")));

            feature.Add(pathFeature);
        }

        return feature;
    }

    private static XElement CreateUiElement()
    {
        // Use simple basic UI without extension
        var ui = new XElement(XName.Get("UI", WixNamespace));

        // if (!Configurations.StartCommand.IsStringNullOrEmpty())
        // {
        //     ui.Add(new XElement(XName.Get("UIRef", WixNamespace),
        //         new XAttribute("Id", "WixUI_FeatureTree")));
        // }
        // else
        // {
        //     ui.Add(new XElement(XName.Get("UIRef", WixNamespace),
        //         new XAttribute("Id", "WixUI_InstallDir")));
        //
        //     ui.Add(new XElement(XName.Get("Property", WixNamespace),
        //         new XAttribute("Id", "WIXUI_INSTALLDIR"),
        //         new XAttribute("Value", "INSTALLFOLDER")));
        // }

        return ui;
    }

    private XElement CreateMainComponentsFragment()
    {
        var fragment = new XElement(XName.Get("Fragment", WixNamespace));

        var componentGroup = new XElement(XName.Get("ComponentGroup", WixNamespace),
            new XAttribute("Id", "MainComponents"),
            new XAttribute("Directory", "INSTALLFOLDER"));

        // Main executable
        var mainComponent = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "MainExecutable"),
            new XAttribute("Guid", "*"));

        mainComponent.Add(new XElement(XName.Get("File", WixNamespace),
            new XAttribute("Id", "MainExeFile"),
            new XAttribute("Source", Path.Combine(PublishOutputDir, AppExecName)),
            new XAttribute("KeyPath", "yes")));

        componentGroup.Add(mainComponent);

        // Shortcuts
        if (!Configurations.DesktopNoDisplay)
        {
            var shortcutsComponent = new XElement(XName.Get("Component", WixNamespace),
                new XAttribute("Id", "ApplicationShortcuts"),
                new XAttribute("Guid", "*"));

            shortcutsComponent.Add(new XElement(XName.Get("Shortcut", WixNamespace),
                new XAttribute("Id", "StartMenuShortcut"),
                new XAttribute("Name", Configurations.AppFriendlyName),
                new XAttribute("Target", $"[INSTALLFOLDER]{AppExecName}"),
                new XAttribute("WorkingDirectory", "INSTALLFOLDER"),
                new XAttribute("Icon", "AppIcon"),
                new XAttribute("Directory", "ProgramMenuFolder")));

            shortcutsComponent.Add(new XElement(XName.Get("Shortcut", WixNamespace),
                new XAttribute("Id", "DesktopShortcut"),
                new XAttribute("Name", Configurations.AppFriendlyName),
                new XAttribute("Target", $"[INSTALLFOLDER]{AppExecName}"),
                new XAttribute("WorkingDirectory", "INSTALLFOLDER"),
                new XAttribute("Icon", "AppIcon"),
                new XAttribute("Directory", "DesktopFolder")));

            shortcutsComponent.Add(new XElement(XName.Get("RemoveFolder", WixNamespace),
                new XAttribute("Id", "RemoveProgramMenuFolder"),
                new XAttribute("Directory", "ProgramMenuFolder"),
                new XAttribute("On", "uninstall")));

            shortcutsComponent.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Root", "HKCU"),
                new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppBaseName}"),
                new XAttribute("Name", "installed"),
                new XAttribute("Type", "integer"),
                new XAttribute("Value", "1"),
                new XAttribute("KeyPath", "yes")));

            componentGroup.Add(shortcutsComponent);
        }

        fragment.Add(componentGroup);

        // Add StandardDirectories for shortcuts
        fragment.Add(new XElement(XName.Get("StandardDirectory", WixNamespace),
            new XAttribute("Id", "ProgramMenuFolder")));

        fragment.Add(new XElement(XName.Get("StandardDirectory", WixNamespace),
            new XAttribute("Id", "DesktopFolder")));

        return fragment;
    }

    private XElement CreateApplicationFilesFragment()
    {
        var fragment = new XElement(XName.Get("Fragment", WixNamespace));

        var componentGroup = new XElement(XName.Get("ComponentGroup", WixNamespace),
            new XAttribute("Id", "ApplicationFiles"),
            new XAttribute("Directory", "INSTALLFOLDER"));

        var files = Directory.GetFiles(PublishOutputDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals(AppExecName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var fileIndex = 0;
        foreach (var file in files)
        {
            var componentId = $"File_{SanitizeId(Path.GetFileNameWithoutExtension(file))}_{fileIndex}";
            var fileId = $"F_{SanitizeId(Path.GetFileNameWithoutExtension(file))}_{fileIndex}";

            var component = new XElement(XName.Get("Component", WixNamespace),
                new XAttribute("Id", componentId),
                new XAttribute("Guid", "*"));

            component.Add(new XElement(XName.Get("File", WixNamespace),
                new XAttribute("Id", fileId),
                new XAttribute("Source", file),
                new XAttribute("KeyPath", "yes")));

            componentGroup.Add(component);
            fileIndex++;
        }

        fragment.Add(componentGroup);
        return fragment;
    }

    private XElement CreateRegistryComponentsFragment()
    {
        var fragment = new XElement(XName.Get("Fragment", WixNamespace));

        var componentGroup = new XElement(XName.Get("ComponentGroup", WixNamespace),
            new XAttribute("Id", "RegistryComponents"),
            new XAttribute("Directory", "INSTALLFOLDER"));

        // File Association
        if (Configurations.AssociateFiles && !Configurations.FileExtension.IsStringNullOrEmpty())
        {
            var ext = Configurations.FileExtension.StartsWith(".")
                ? Configurations.FileExtension
                : $".{Configurations.FileExtension}";
            var progId = $"{Configurations.AppBaseName}File";

            var component = new XElement(XName.Get("Component", WixNamespace),
                new XAttribute("Id", "FileAssociation"),
                new XAttribute("Guid", "*"));

            var extKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", ext));
            extKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", progId)));

            var progIdKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", progId));
            progIdKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", $"{Configurations.AppFriendlyName} File")));

            var commandKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", $"{progId}\\shell\\open\\command"));
            commandKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", $"\"[INSTALLFOLDER]{AppExecName}\" \"%1\"")));

            component.Add(extKey);
            component.Add(progIdKey);
            component.Add(commandKey);

            component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Root", "HKCU"),
                new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppBaseName}"),
                new XAttribute("Name", "FileAssoc"),
                new XAttribute("Type", "integer"),
                new XAttribute("Value", "1"),
                new XAttribute("KeyPath", "yes")));

            componentGroup.Add(component);
        }

        // Context Menu
        if (Configurations.ContextMenuIntegration)
        {
            var menuText = Configurations.ContextMenuText.IsStringNullOrEmpty()
                ? $"Open with {Configurations.AppFriendlyName}"
                : Configurations.ContextMenuText;

            var component = new XElement(XName.Get("Component", WixNamespace),
                new XAttribute("Id", "ContextMenu"),
                new XAttribute("Guid", "*"));

            var contextKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", $"*\\shell\\{Configurations.AppBaseName}"));
            contextKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", menuText)));

            var commandKey = new XElement(XName.Get("RegistryKey", WixNamespace),
                new XAttribute("Root", "HKCR"),
                new XAttribute("Key", $"*\\shell\\{Configurations.AppBaseName}\\command"));
            commandKey.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Type", "string"),
                new XAttribute("Value", $"\"[INSTALLFOLDER]{AppExecName}\" \"%1\"")));

            component.Add(contextKey);
            component.Add(commandKey);

            component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
                new XAttribute("Root", "HKCU"),
                new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppBaseName}"),
                new XAttribute("Name", "ContextMenu"),
                new XAttribute("Type", "integer"),
                new XAttribute("Value", "1"),
                new XAttribute("KeyPath", "yes")));

            componentGroup.Add(component);
        }

        fragment.Add(componentGroup);
        return fragment;
    }

    private XElement CreatePathEnvironmentFragment()
    {
        var fragment = new XElement(XName.Get("Fragment", WixNamespace));

        var componentGroup = new XElement(XName.Get("ComponentGroup", WixNamespace),
            new XAttribute("Id", "PathComponents"),
            new XAttribute("Directory", "INSTALLFOLDER"));

        var component = new XElement(XName.Get("Component", WixNamespace),
            new XAttribute("Id", "PathEnvironment"),
            new XAttribute("Guid", "*"));

        component.Add(new XElement(XName.Get("Environment", WixNamespace),
            new XAttribute("Id", "PATH_Main"),
            new XAttribute("Name", "PATH"),
            new XAttribute("Value", "[INSTALLFOLDER]"),
            new XAttribute("Permanent", "no"),
            new XAttribute("Part", "last"),
            new XAttribute("Action", "set"),
            new XAttribute("System", Configurations.SetupAdminInstall ? "yes" : "no")));

        component.Add(new XElement(XName.Get("RegistryValue", WixNamespace),
            new XAttribute("Root", "HKCU"),
            new XAttribute("Key", $"Software\\{Configurations.PublisherName}\\{Configurations.AppBaseName}"),
            new XAttribute("Name", "PathAdded"),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes")));

        componentGroup.Add(component);
        fragment.Add(componentGroup);

        return fragment;
    }

    private string GenerateUpgradeCode()
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Configurations.AppId));
        return new Guid(hash).ToString().ToUpperInvariant();
    }

    private string SanitizeId(string id)
    {
        return new string(id.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray());
    }

    private void CompileWixSource()
    {
        var outputFile = Path.Combine(OutputDirectory, OutputName);
        var arch = GetPackageArch();

        var processInfo = new ProcessStartInfo
        {
            FileName = WixCompiler,
            Arguments = $"build -arch {arch} \"{WixSourcePath}\" -o \"{outputFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Clear problematic environment variables
        processInfo.EnvironmentVariables.Remove("DOTNET_STARTUP_HOOKS");

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start WiX compiler.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (Arguments.Verbose && !output.IsStringNullOrEmpty())
            Logger.LogInfo("WiX output:\n{0}", output);

        if (process.ExitCode == 0)
            return;

        var errorMsg = !error.IsStringNullOrEmpty() ? error : output;
        throw new InvalidOperationException($"WiX compilation failed with exit code {process.ExitCode}:\n{errorMsg}");
    }
}