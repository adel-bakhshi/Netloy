using CommandLine;
using Netloy.ConsoleApp.NetloyFiles;
using Netloy.ConsoleApp.NetloyLogger;
using Netloy.ConsoleApp.Package;

namespace Netloy.ConsoleApp.Argument;

public class Arguments
{
    [Option('t', "package-type")]
    public PackageType? PackageType { get; set; }

    [Option('r', "runtime")]
    public string? Runtime { get; set; }

    [Option('y', "skip-all")]
    public bool SkipAll { get; set; }

    [Option('o', "output-path")]
    public string? OutputPath { get; set; }

    [Option('l', "log-level", Default = LogLevel.Debug)]
    public LogLevel LogLevel { get; set; }

    [Option("version")]
    public bool ShowVersion { get; set; }

    [Option('h', "help")]
    public string? Help { get; set; }

    [Option("verbose")]
    public bool Verbose { get; set; }

    [Option('n', "new")]
    public NewFileType? NewType { get; set; }

    // TODO: Overwrite configuration project path when provided
    [Option('p', "project-path")]
    public string? ProjectPath { get; set; }

    [Option("upgrade-config")]
    public bool UpgradeConfiguration { get; set; }

    [Option('c', "publish-config")]
    public string PublishConfiguration { get; set; } = "Release";

    [Option("clean")]
    public bool CleanProject { get; set; }

    [Option('v', "app-version")]
    public string? AppVersion { get; set; }

    [Option("config-path")]
    public string? ConfigPath { get; set; }

    public bool ShowHelp { get; set; }
}