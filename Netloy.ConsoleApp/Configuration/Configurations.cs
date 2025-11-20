namespace Netloy.ConsoleApp.Configuration;

public class Configurations
{
    #region Properties

    public string AppBaseName { get; set; } = string.Empty;
    public string AppFriendlyName { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string AppVersionRelease { get; set; } = string.Empty;
    public string AppShortSummary { get; set; } = string.Empty;
    public string AppDescription { get; set; } = string.Empty;
    public string AppLicenseId { get; set; } = string.Empty;
    public string AppLicenseFile { get; set; } = string.Empty;
    public string AppChangeFile { get; set; } = string.Empty;

    public string PublisherName { get; set; } = string.Empty;
    public string PublisherId { get; set; } = string.Empty;
    public string PublisherCopyright { get; set; } = string.Empty;
    public string PublisherLinkName { get; set; } = string.Empty;
    public string PublisherLinkUrl { get; set; } = string.Empty;
    public string PublisherEmail { get; set; } = string.Empty;

    public bool DesktopNoDisplay { get; set; }
    public bool DesktopTerminal { get; set; }
    public string DesktopFile { get; set; } = string.Empty;
    public string StartCommand { get; set; } = string.Empty;
    public string PrimeCategory { get; set; } = string.Empty;
    public string MetaFile { get; set; } = string.Empty;
    public string IconFiles { get; set; } = string.Empty;
    public bool AutoGenerateIcons { get; set; }

    public string DotnetProjectPath { get; set; } = string.Empty;
    public string DotnetPublishArgs { get; set; } = string.Empty;
    public string DotnetPostPublish { get; set; } = string.Empty;
    public string DotnetPostPublishOnWindows { get; set; } = string.Empty;
    public string DotnetPostPublishArguments { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;

    public string AppImageArgs { get; set; } = string.Empty;

    public string FlatpakPlatformRuntime { get; set; } = string.Empty;
    public string FlatpakPlatformSdk { get; set; } = string.Empty;
    public string FlatpakPlatformVersion { get; set; } = string.Empty;
    public string FlatpakFinishArgs { get; set; } = string.Empty;
    public string FlatpakBuilderArgs { get; set; } = string.Empty;

    public bool RpmAutoReq { get; set; }
    public bool RpmAutoProv { get; set; }
    public string RpmRequires { get; set; } = string.Empty;

    public string DebianRecommends { get; set; } = string.Empty;

    public string MacOsInfoPlist { get; set; } = string.Empty;
    public string MacOsEntitlements { get; set; } = string.Empty;

    public string SetupGroupName { get; set; } = string.Empty;
    public bool SetupAdminInstall { get; set; }
    public string SetupCommandPrompt { get; set; } = string.Empty;
    public string SetupMinWindowsVersion { get; set; } = "10";
    public string SetupSignTool { get; set; } = string.Empty;
    public string MsiUpgradeCode { get; set; } = string.Empty;
    public string SetupUninstallScript { get; set; } = string.Empty;
    public string SetupPasswordEncryption { get; set; } = string.Empty;
    public string ExeWizardImageFile { get; set; } = string.Empty;
    public string ExeWizardSmallImageFile { get; set; } = string.Empty;
    public string MsiUiBanner { get; set; } = string.Empty;
    public string MsiUiDialog { get; set; } = string.Empty;
    public bool SetupCloseApplications { get; set; } = true;
    public bool SetupRestartIfNeeded { get; set; }
    public string SetupUninstallDisplayName { get; set; } = string.Empty;
    public string ExeVersionInfoCompany { get; set; } = string.Empty;
    public string ExeVersionInfoDescription { get; set; } = string.Empty;
    public bool AssociateFiles { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public bool ContextMenuIntegration { get; set; }
    public string ContextMenuText { get; set; } = string.Empty;
    public bool SetupStartOnWindowsStartup { get; set; }

    public string ConfigVersion { get; set; } = string.Empty;

    public List<string> IconsCollection { get; set; } = [];

    #endregion
}