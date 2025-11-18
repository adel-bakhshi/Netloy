using System.Reflection;
using Netloy.ConsoleApp.Extensions;

namespace Netloy.ConsoleApp;

public static class Constants
{
    private static string? _configFilePath;

    public const string NetloyConfigFileExt = ".netloy";
    public const string ProjectUrl = "https://github.com/adel-bakhshi/Netloy";
    public const string ProjectLicense = "AGPL-3.0";
    public const string InnoSetupDownloadUrl = "https://jrsoftware.org/isdl.php";
    public const string WixDownloadUrl = "https://docs.firegiant.com/wix/using-wix/#command-line-net-tool";

    public static string Copyright { get; }
    public static string Version { get; }

    public static string ConfigFilePath
    {
        get => _configFilePath.IsStringNullOrEmpty() ? throw new InvalidOperationException("Config file path is not defined.") : _configFilePath!;
        set => _configFilePath = value;
    }

    public static string ConfigFileDirectory => Path.GetDirectoryName(ConfigFilePath) ?? throw new InvalidOperationException("Config file path is not defined.");

    static Constants()
    {
        Copyright = $"Copyright © Adel Bakhshi 2025-{DateTime.Now:yy}";
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }
}