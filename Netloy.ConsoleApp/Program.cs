using CommandLine;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Macro;
using Netloy.ConsoleApp.NetloyFiles;
using Netloy.ConsoleApp.NetloyLogger;
using Netloy.ConsoleApp.Package;
using System.Runtime.InteropServices;

namespace Netloy.ConsoleApp;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var argumentParser = new ArgumentParser(args);
            var arguments = argumentParser.Parse();

            if (argumentParser.Errors?.Count > 0)
            {
                if (argumentParser.Errors.IsVersion())
                {
                    argumentParser.ShowVersion();
                }
                else
                {
                    argumentParser.ShowHelps(argumentParser.Errors);
                }

                return 1;
            }

            if (arguments.ShowVersion)
            {
                argumentParser.ShowVersion();
                return 0;
            }

            if (arguments.ShowHelp)
            {
                switch (arguments.Help?.ToLowerInvariant())
                {
                    case "conf":
                    {
                        argumentParser.ShowConfigHelps();
                        break;
                    }

                    case "macro":
                    {
                        MacroExpander.ShowMacroHelps(arguments.Verbose);
                        break;
                    }

                    default:
                    {
                        argumentParser.ShowHelps();
                        break;
                    }
                }

                return 0;
            }

            Logger.LogInfo("Netloy started successfully");

            if (arguments.NewType != null)
            {
                var fileCreator = new FileCreator(arguments);
                await fileCreator.CreateNewFileAsync();
                return 0;
            }

            var configParser = new ConfigurationParser(arguments);

            if (arguments.UpgradeConfiguration)
            {
                await configParser.UpgradeConfigurationAsync(arguments);
                return 0;
            }

            var config = await configParser.ParseAsync() ?? throw new InvalidCastException("Failed to parse configuration file");

            if (!Constants.Version.Equals(config.ConfigVersion))
            {
                var message = $"Configuration file version ({config.ConfigVersion}) is not compatible with the current version ({Constants.Version}) of Netloy. " +
                              "Please upgrade your configuration file.";

                Logger.LogWarning(message, forceLog: true);
            }

            var builderFactory = new PackageBuilderFactory(arguments, config);
            if (builderFactory.CanCreatePackage())
            {
                var builder = builderFactory.CreatePackageBuilder();
                var isValid = builder.Validate();
                if (!isValid)
                {
                    Logger.LogError("Failed to validate builder");
                    return 1;
                }

                await builder.BuildAsync();

                if (arguments.CleanProject)
                    builder.Clear();

                return 0;
            }

            var currentRuntime = RuntimeInformation.RuntimeIdentifier;
            Logger.LogError("Can't create {0} package in os {1} with architecture {2}",
                arguments.PackageType?.ToString().ToUpperInvariant() ?? "UNKNOWN", currentRuntime, arguments.Runtime);

            return 1;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            return -1;
        }
    }
}