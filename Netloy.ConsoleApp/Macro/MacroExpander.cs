using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;
using Netloy.ConsoleApp.Package;
using System.Text;

namespace Netloy.ConsoleApp.Macro;

public class MacroExpander
{
    #region Private Fields

    private readonly Dictionary<MacroId, string> _macros;
    private readonly Arguments _arguments;

    #endregion Private Fields

    public MacroExpander(Arguments arguments)
    {
        _macros = [];
        _arguments = arguments;
    }

    public static string GetMacroVariable(MacroId id)
    {
        return id switch
        {
            MacroId.ConfFileDirectory => "${CONF_FILE_DIRECTORY}",
            MacroId.AppBaseName => "${APP_BASE_NAME}",
            MacroId.AppFriendlyName => "${APP_FRIENDLY_NAME}",
            MacroId.AppId => "${APP_ID}",
            MacroId.AppShortSummary => "${APP_SHORT_SUMMARY}",
            MacroId.AppLicenseId => "${APP_LICENSE_ID}",
            MacroId.AppExecName => "${APP_EXEC_NAME}",
            MacroId.PublisherName => "${PUBLISHER_NAME}",
            MacroId.PublisherId => "${PUBLISHER_ID}",
            MacroId.PublisherCopyright => "${PUBLISHER_COPYRIGHT}",
            MacroId.PublisherLinkName => "${PUBLISHER_LINK_NAME}",
            MacroId.PublisherLinkUrl => "${PUBLISHER_LINK_URL}",
            MacroId.PublisherEmail => "${PUBLISHER_EMAIL}",
            MacroId.DesktopNoDisplay => "${DESKTOP_NODISPLAY}",
            MacroId.DesktopIntegrate => "${DESKTOP_INTEGRATE}",
            MacroId.DesktopTerminal => "${DESKTOP_TERMINAL}",
            MacroId.PrimeCategory => "${PRIME_CATEGORY}",
            MacroId.AppVersion => "${APP_VERSION}",
            MacroId.PackageRelease => "${PACKAGE_RELEASE}",
            MacroId.PackageType => "${PACKAGE_TYPE}",
            MacroId.DotnetRuntime => "${DOTNET_RUNTIME}",
            MacroId.PackageArch => "${PACKAGE_ARCH}",
            MacroId.PublishOutputDirectory => "${PUBLISH_OUTPUT_DIRECTORY}",
            MacroId.AppStreamDescriptionXml => "${APPSTREAM_DESCRIPTION_XML}",
            MacroId.AppStreamChangelogXml => "${APPSTREAM_CHANGELOG_XML}",
            MacroId.PrimaryIconFileName => "${PRIMARY_ICON_FILE_NAME}",
            MacroId.PrimaryIconFilePath => "${PRIMARY_ICON_FILE_PATH}",
            MacroId.InstallExec => "${INSTALL_EXEC}",
            _ => throw new ArgumentException("Unknown macro " + id)
        };
    }

    public void SetMacroValue(MacroId id, string value)
    {
        // Add or update the macro value in dictionary
        if (!_macros.TryAdd(id, value))
            _macros[id] = value;

        // Set as environment variable
        SetMacroAsEnvironmentVariable(id, value);
    }

    public string GetMacroValue(MacroId id)
    {
        if (!_macros.TryGetValue(id, out var value))
            return string.Empty;

        if (id == MacroId.PrimeCategory)
        {
            return _arguments.PackageType switch
            {
                PackageType.App or PackageType.Dmg => GetMacOsCategoryType(value),
                PackageType.AppImage or PackageType.Flatpak or PackageType.Rpm or PackageType.Deb or PackageType.Pacman => GetLinuxCategoryType(value),
                _ => value
            };
        }

        return value.IsStringNullOrEmpty() ? string.Empty : value;
    }

    public string ExpandMacros(string input)
    {
        var ids = Enum.GetValues<MacroId>();
        foreach (var id in ids)
        {
            var macro = GetMacroVariable(id);
            input = input.Replace(macro, GetMacroValue(id));
        }

        return input;
    }

    public static void ShowMacroHelps(bool verbose)
    {
        var sb = new StringBuilder();

        if (!verbose)
        {
            sb.AppendLine("Use --verbose to see detailed macro descriptions");
            sb.AppendLine();
        }

        // Header
        sb.AppendLine("=".PadRight(80, '='));
        sb.AppendLine("NETLOY MACRO VARIABLES");
        sb.AppendLine("=".PadRight(80, '='));
        sb.AppendLine();

        if (verbose)
        {
            sb.AppendLine("Macro variables are placeholders that get replaced with actual values during the");
            sb.AppendLine("build process. They can be used in configuration files, scripts, and custom templates.");
            sb.AppendLine("Use the format ${MACRO_NAME} in your files to reference these values.");
            sb.AppendLine();
        }

        // Configuration & Path Macros
        AppendMacroSection(sb, "CONFIGURATION & PATH MACROS", verbose);

        AppendMacro(sb, MacroId.ConfFileDirectory,
            "Directory containing the Netloy configuration file",
            verbose ? "This macro points to the absolute path of the directory where your .netloy configuration\n" +
                "file is located. Useful for referencing other files relative to the config file location.\n" +
                "Example: /home/user/myproject/Deploy" : null,
            verbose);

        AppendMacro(sb, MacroId.PublishOutputDirectory,
            "Directory containing published application binaries",
            verbose ? "This macro is set during the publish phase and points to the directory where dotnet publish\n" +
                "outputs your compiled application. Use this in post-publish scripts to access build artifacts.\n" +
                "Note: This macro is only available in DotnetPostPublish scripts and after publish completes.\n" +
                "Example: /tmp/netloy/MyApp/linux-x64/publish" : null,
            verbose);

        sb.AppendLine();

        // Application Information Macros
        AppendMacroSection(sb, "APPLICATION INFORMATION MACROS", verbose);

        AppendMacro(sb, MacroId.AppBaseName,
            "Base name of the main executable file",
            verbose ? "The core application name without any extension or path. This should match the main\n" +
                "executable filename. Defined in configuration file as 'AppBaseName'.\n" +
                "Example: MyApp (for MyApp.exe or MyApp)" : null,
            verbose);

        AppendMacro(sb, MacroId.AppFriendlyName,
            "Human-readable application name",
            verbose ? "The display name of your application as shown to users in installers, desktop shortcuts,\n" +
                "and application menus. Defined in configuration file as 'AppFriendlyName'.\n" +
                "Example: My Awesome Application" : null,
            verbose);

        AppendMacro(sb, MacroId.AppId,
            "Unique application identifier in reverse DNS format",
            verbose ? "A unique identifier for your application following reverse domain notation. This should\n" +
                "remain constant throughout the lifetime of your software. Defined in configuration file.\n" +
                "Example: com.example.myapp" : null,
            verbose);

        AppendMacro(sb, MacroId.AppShortSummary,
            "Brief one-line description of the application",
            verbose ? "A concise summary of your application in one sentence. Used in package metadata and\n" +
                "software centers. Defined in configuration file as 'AppShortSummary'.\n" +
                "Example: A powerful tool for image editing" : null,
            verbose);

        AppendMacro(sb, MacroId.AppLicenseId,
            "Application license identifier (SPDX format)",
            verbose ? "The license under which your application is distributed. Should be a recognized SPDX\n" +
                "license identifier. Defined in configuration file as 'AppLicenseId'.\n" +
                "Examples: MIT, GPL-3.0-or-later, Apache-2.0, LicenseRef-Proprietary" : null,
            verbose);

        AppendMacro(sb, MacroId.AppExecName,
            "Main executable filename with extension",
            verbose ? "The complete filename of the main executable including platform-specific extension.\n" +
                "Automatically determined based on AppBaseName and target runtime.\n" +
                "Examples: MyApp.exe (Windows), MyApp (Linux/macOS)" : null,
            verbose);

        AppendMacro(sb, MacroId.AppVersion,
            "Application version number",
            verbose ? "The version number of your application in semantic versioning format. Can be specified\n" +
                "via --app-version argument or from configuration file 'AppVersionRelease'.\n" +
                "Example: 1.2.3" : null,
            verbose);

        AppendMacro(sb, MacroId.PackageRelease,
            "Package release number",
            verbose ? "The release/revision number of the package, separate from application version. Used to\n" +
                "track packaging changes without changing the app version. Defined in configuration file.\n" +
                "Example: 1 (from AppVersionRelease: 1.2.3[1])" : null,
            verbose);

        sb.AppendLine();

        // Publisher Information Macros
        AppendMacroSection(sb, "PUBLISHER INFORMATION MACROS", verbose);

        AppendMacro(sb, MacroId.PublisherName,
            "Name of the publisher, company, or creator",
            verbose ? "The name of the individual, organization, or company publishing the application.\n" +
                "Defined in configuration file as 'PublisherName'.\n" +
                "Example: Acme Corporation" : null,
            verbose);

        AppendMacro(sb, MacroId.PublisherId,
            "Publisher identifier in reverse DNS format",
            verbose ? "A unique identifier for the publisher following reverse domain notation. If not specified\n" +
                "in configuration, defaults to the parent domain from AppId (excluding the app name).\n" +
                "Example: com.example (derived from com.example.myapp)" : null,
            verbose);

        AppendMacro(sb, MacroId.PublisherCopyright,
            "Copyright statement for the application",
            verbose ? "The copyright notice for your application. Defined in configuration file.\n" +
                "Example: Copyright © 2025 Acme Corporation" : null,
            verbose);

        AppendMacro(sb, MacroId.PublisherLinkName,
            "Display name for publisher's website link",
            verbose ? "The text label for the publisher's website link shown in installers and menus.\n" +
                "Defined in configuration file as 'PublisherLinkName'.\n" +
                "Example: Visit Our Website" : null,
            verbose);

        AppendMacro(sb, MacroId.PublisherLinkUrl,
            "URL to publisher's website",
            verbose ? "The web address of the publisher or application homepage. Defined in configuration file.\n" +
                "Example: https://example.com" : null,
            verbose);

        AppendMacro(sb, MacroId.PublisherEmail,
            "Contact email address for the publisher",
            verbose ? "The email address for contacting the publisher or maintainer. Required for some package\n" +
                "formats like DEB. Defined in configuration file as 'PublisherEmail'.\n" +
                "Example: contact@example.com" : null,
            verbose);

        sb.AppendLine();

        // Desktop Integration Macros
        AppendMacroSection(sb, "DESKTOP INTEGRATION MACROS", verbose);

        AppendMacro(sb, MacroId.DesktopNoDisplay,
            "Whether application should be hidden from desktop (true/false)",
            verbose ? "Boolean value indicating if the application should be hidden in desktop menus and launchers.\n" +
                "Set to 'true' for background services or utilities. Defined in configuration file.\n" +
                "Values: true, false" : null,
            verbose);

        AppendMacro(sb, MacroId.DesktopIntegrate,
            "Whether to integrate with desktop environment (true/false)",
            verbose ? "Boolean value indicating if desktop integration should be enabled. This is the inverse of\n" +
                "DesktopNoDisplay. When true, creates shortcuts and menu entries.\n" +
                "Values: true, false" : null,
            verbose);

        AppendMacro(sb, MacroId.DesktopTerminal,
            "Whether application runs in terminal (true/false)",
            verbose ? "Boolean value indicating if the application requires a terminal to run. Set to 'true' for\n" +
                "command-line applications. Defined in configuration file as 'DesktopTerminal'.\n" +
                "Values: true, false" : null,
            verbose);

        AppendMacro(sb, MacroId.PrimeCategory,
            "Primary application category",
            verbose ? "The main category for the application used in desktop environments and app stores. Should be\n" +
                "one of the Freedesktop categories. Automatically converted to platform-specific format:\n" +
                "  - Linux: Development, Graphics, Office, Utility, Game, etc.\n" +
                "  - macOS: public.app-category.developer-tools, etc.\n" +
                "Defined in configuration file as 'PrimeCategory'.\n" +
                "Example: Development" : null,
            verbose);

        AppendMacro(sb, MacroId.InstallExec,
            "Installation path for executable (Linux only)",
            verbose ? "The absolute installation path where the application executable will be installed on Linux.\n" +
                "This macro is only populated during Linux package builds (DEB, RPM, AppImage, Flatpak, Pacman).\n" +
                "Use this in desktop files and scripts to reference the installed executable location.\n" +
                "Example: /usr/bin/myapp or /opt/myapp/bin/myapp" : null,
            verbose);

        sb.AppendLine();

        // Package Build Macros
        AppendMacroSection(sb, "PACKAGE BUILD MACROS", verbose);

        AppendMacro(sb, MacroId.PackageType,
            "Type of package being built",
            verbose ? "The target package format specified via --package-type or -t argument. Used to determine\n" +
                "platform-specific build steps and output format.\n" +
                "Values: exe, msi, app, dmg, appimage, deb, rpm, flatpak, pacman, portable" : null,
            verbose);

        AppendMacro(sb, MacroId.PackageArch,
            "Target runtime identifier for the package",
            verbose ? "The runtime identifier (RID) specified via --runtime or -r argument. Defines the target\n" +
                "platform and architecture for the build.\n" +
                "Examples: linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64" : null,
            verbose);

        AppendMacro(sb, MacroId.DotnetRuntime,
            ".NET runtime description",
            verbose ? "The description of the .NET runtime being used for the build. Automatically detected from\n" +
                "the build environment.\n" +
                "Example: .NET 8.0.0" : null,
            verbose);

        sb.AppendLine();

        // Icon & Resource Macros
        AppendMacroSection(sb, "ICON & RESOURCE MACROS", verbose);

        AppendMacro(sb, MacroId.PrimaryIconFileName,
            "Filename of the primary application icon",
            verbose ? "The filename (without path) of the main icon file selected for the current package type.\n" +
                "Automatically selected based on package format:\n" +
                "  - Windows (EXE/MSI): .ico file\n" +
                "  - macOS (APP/DMG): .icns file\n" +
                "  - Linux: .svg file or largest .png file\n" +
                "Set automatically during build; empty if no suitable icon is found.\n" +
                "Example: myapp.ico" : null,
            verbose);

        AppendMacro(sb, MacroId.PrimaryIconFilePath,
            "Full path to the primary application icon",
            verbose ? "The absolute file path to the main icon file selected for the current package type.\n" +
                "Use this to reference the icon file in templates and scripts.\n" +
                "Set automatically during build; empty if no suitable icon is found.\n" +
                "Example: /tmp/netloy/MyApp/icons/myapp.256x256.png" : null,
            verbose);

        sb.AppendLine();

        // AppStream Metadata Macros
        AppendMacroSection(sb, "APPSTREAM METADATA MACROS (Linux)", verbose);

        AppendMacro(sb, MacroId.AppStreamDescriptionXml,
            "AppStream-formatted XML description",
            verbose ? "The application description formatted as AppStream XML markup. Automatically generated from\n" +
                "the 'AppDescription' field in configuration file. Used in AppStream metainfo files for\n" +
                "Linux software centers and package repositories.\n" +
                "Generated during build; contains properly formatted <p> and <ul> tags." : null,
            verbose);

        AppendMacro(sb, MacroId.AppStreamChangelogXml,
            "AppStream-formatted XML changelog",
            verbose ? "The application changelog formatted as AppStream XML markup. Automatically generated from\n" +
                "the 'AppChangeFile' in configuration. Parses version headers and change items into proper\n" +
                "AppStream <release> elements. Used in AppStream metainfo files.\n" +
                "Generated during build; empty if no changelog file is specified." : null,
            verbose);

        sb.AppendLine();

        // Usage Examples
        if (verbose)
        {
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine("USAGE EXAMPLES");
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine();

            sb.AppendLine("1. Using macros in configuration file (DotnetPublishArgs):");
            sb.AppendLine("   DotnetPublishArgs: -p:Version=${APP_VERSION} --self-contained true");
            sb.AppendLine();

            sb.AppendLine("2. Using macros in custom desktop file:");
            sb.AppendLine("   Name=${APP_FRIENDLY_NAME}");
            sb.AppendLine("   Exec=${INSTALL_EXEC}");
            sb.AppendLine("   Icon=${APP_ID}");
            sb.AppendLine("   Categories=${PRIME_CATEGORY};");
            sb.AppendLine();

            sb.AppendLine("3. Using macros in post-publish script:");
            sb.AppendLine("   echo \"Building version ${APP_VERSION}\"");
            sb.AppendLine("   cp additional-files/* ${PUBLISH_OUTPUT_DIRECTORY}/");
            sb.AppendLine();

            sb.AppendLine("4. Using macros in AppStream metainfo file:");
            sb.AppendLine("   <id>${APP_ID}</id>");
            sb.AppendLine("   <name>${APP_FRIENDLY_NAME}</name>");
            sb.AppendLine("   <summary>${APP_SHORT_SUMMARY}</summary>");
            sb.AppendLine("   <description>");
            sb.AppendLine("     ${APPSTREAM_DESCRIPTION_XML}");
            sb.AppendLine("   </description>");
            sb.AppendLine();
        }

        // Environment Variables in Scripts
        if (verbose)
        {
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine("USING MACROS IN POST-PUBLISH SCRIPTS");
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine();

            sb.AppendLine("Netloy provides TWO ways to use macro values in post-publish scripts:");
            sb.AppendLine();

            sb.AppendLine("METHOD 1: String Replacement (${MACRO_NAME})");
            sb.AppendLine("-".PadRight(80, '-'));
            sb.AppendLine("Netloy reads your script file, replaces all ${MACRO_NAME} occurrences with actual");
            sb.AppendLine("values, and then executes the modified script. This works for any file content.");
            sb.AppendLine();
            sb.AppendLine("Example in bash script:");
            sb.AppendLine("  echo \"Building version ${APP_VERSION}\"");
            sb.AppendLine("  # After replacement: echo \"Building version 1.2.3\"");
            sb.AppendLine();
            sb.AppendLine("Example in batch script:");
            sb.AppendLine("  echo Building version ${APP_VERSION}");
            sb.AppendLine("  REM After replacement: echo Building version 1.2.3");
            sb.AppendLine();

            sb.AppendLine("METHOD 2: Environment Variables");
            sb.AppendLine("-".PadRight(80, '-'));
            sb.AppendLine("All macros are also set as environment variables that can be accessed directly");
            sb.AppendLine("in your scripts using platform-specific syntax:");
            sb.AppendLine();
            sb.AppendLine("  Windows (Batch):  %MACRO_NAME%");
            sb.AppendLine("    Example: echo %APP_VERSION%");
            sb.AppendLine("    Example: set OUTPUT=%PUBLISH_OUTPUT_DIRECTORY%");
            sb.AppendLine();
            sb.AppendLine("  Windows (PowerShell):  $env:MACRO_NAME");
            sb.AppendLine("    Example: Write-Host $env:APP_VERSION");
            sb.AppendLine("    Example: $output = $env:PUBLISH_OUTPUT_DIRECTORY");
            sb.AppendLine();
            sb.AppendLine("  Linux/macOS (Bash):  $MACRO_NAME or ${MACRO_NAME}");
            sb.AppendLine("    Example: echo $APP_VERSION");
            sb.AppendLine("    Example: OUTPUT=$PUBLISH_OUTPUT_DIRECTORY");
            sb.AppendLine();

            sb.AppendLine("WHICH METHOD TO USE?");
            sb.AppendLine("-".PadRight(80, '-'));
            sb.AppendLine("• Use METHOD 1 (${...}) when you want Netloy to replace values in static content,");
            sb.AppendLine("  configuration files, or when you need guaranteed value substitution.");
            sb.AppendLine();
            sb.AppendLine("• Use METHOD 2 (Environment Variables) when writing dynamic scripts that need to");
            sb.AppendLine("  access values in conditional logic, loops, or when calling other programs that");
            sb.AppendLine("  expect environment variables.");
            sb.AppendLine();
            sb.AppendLine("• You can mix both methods in the same script!");
            sb.AppendLine();

            sb.AppendLine("IMPORTANT NOTES:");
            sb.AppendLine("-".PadRight(80, '-'));
            sb.AppendLine("1. String replacement (${...}) happens BEFORE script execution");
            sb.AppendLine("2. Environment variables are available DURING script execution");
            sb.AppendLine();
        }

        // Footer
        sb.AppendLine("=".PadRight(80, '='));
        if (!verbose)
            sb.AppendLine("Use 'netloy --help macro --verbose' for detailed descriptions and examples");

        sb.AppendLine();

        Console.WriteLine(sb.ToString());
    }

    private static void AppendMacroSection(StringBuilder sb, string sectionName, bool verbose)
    {
        if (verbose)
            sb.AppendLine("-".PadRight(80, '-'));

        sb.AppendLine(sectionName);

        if (verbose)
            sb.AppendLine("-".PadRight(80, '-'));

        sb.AppendLine();
    }

    private static void AppendMacro(StringBuilder sb, MacroId macroId, string shortDescription, string? detailedDescription, bool verbose)
    {
        var macroVar = GetMacroVariable(macroId);

        if (verbose)
        {
            sb.AppendLine($"Macro: {macroVar}");
            sb.AppendLine($"Description: {shortDescription}");
            if (!detailedDescription.IsStringNullOrEmpty())
            {
                sb.AppendLine();
                foreach (var line in detailedDescription!.Split('\n'))
                    sb.AppendLine($"  {line}");
            }

            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"  {macroVar.PadRight(35)} {shortDescription}");
        }
    }

    private static string GetMacOsCategoryType(string category)
    {
        // Map common categories to macOS application category types
        return category.ToLowerInvariant() switch
        {
            "development" => "public.app-category.developer-tools",
            "graphics" => "public.app-category.graphics-design",
            "network" => "public.app-category.networking",
            "utility" => "public.app-category.utilities",
            "game" => "public.app-category.games",
            "office" or "productivity" => "public.app-category.productivity",
            "audiovideo" or "audio" or "video" or "music" => "public.app-category.music",
            "education" => "public.app-category.education",
            "finance" => "public.app-category.finance",
            "business" => "public.app-category.business",
            "entertainment" => "public.app-category.entertainment",
            "health" => "public.app-category.healthcare-fitness",
            "lifestyle" => "public.app-category.lifestyle",
            "news" => "public.app-category.news",
            "photo" => "public.app-category.photography",
            "reference" => "public.app-category.reference",
            "social" => "public.app-category.social-networking",
            "sports" => "public.app-category.sports",
            "travel" => "public.app-category.travel",
            "weather" => "public.app-category.weather",
            _ => "public.app-category.utilities"
        };
    }

    private static string GetLinuxCategoryType(string category)
    {
        // Map common categories to freedesktop.org registered categories
        // Reference: https://specifications.freedesktop.org/menu-spec/latest/apa.html
        return category.ToLowerInvariant() switch
        {
            // Main Categories
            "development" => "Development",
            "graphics" => "Graphics",
            "network" => "Network",
            "utility" => "Utility",
            "game" => "Game",
            "office" or "productivity" => "Office",
            "audiovideo" or "audio" or "video" or "music" => "AudioVideo",
            "education" => "Education",
            "science" => "Science",
            "settings" => "Settings",
            "system" => "System",

            // Additional Categories (mapped to their most appropriate main category)
            "building" => "Development",
            "debugger" => "Development",
            "ide" => "Development",
            "profiling" => "Development",
            "translation" => "Development",
            "webdevelopment" => "Development",

            "2dgraphics" => "Graphics",
            "3dgraphics" => "Graphics",
            "scanning" => "Graphics",
            "photography" => "Graphics",
            "rastergraphics" => "Graphics",
            "vectorgraphics" => "Graphics",
            "viewer" => "Graphics",
            "photo" => "Graphics",

            "biology" => "Science",
            "chemistry" => "Science",
            "math" => "Science",
            "astronomy" => "Science",
            "physics" => "Science",

            "engineering" => "Science",
            "electronics" => "Science",
            "geography" => "Science",
            "geology" => "Science",

            "audiovideoediting" => "AudioVideo",
            "player" => "AudioVideo",
            "recorder" => "AudioVideo",
            "discburning" => "AudioVideo",

            "email" => "Network",
            "instantmessaging" => "Network",
            "chat" => "Network",
            "irc" => "Network",
            "telephony" => "Network",
            "webbrowser" => "Network",
            "p2p" => "Network",
            "filetransfer" => "Network",
            "dialup" => "Network",

            "calendar" => "Office",
            "contactmanagement" => "Office",
            "database" => "Office",
            "dictionary" => "Office",
            "chart" => "Office",
            "finance" => "Office",
            "flowchart" => "Office",
            "pda" => "Office",
            "projectmanagement" => "Office",
            "presentation" => "Office",
            "spreadsheet" => "Office",
            "wordprocessor" => "Office",
            "publishing" => "Office",

            "boardgame" => "Game",
            "cardgame" => "Game",
            "arcadegame" => "Game",
            "actiongame" => "Game",
            "adventuregame" => "Game",
            "simulation" => "Game",
            "sportsgame" => "Game",
            "strategygame" => "Game",
            "roleplaying" => "Game",

            "archiving" => "Utility",
            "compression" => "Utility",
            "filetools" => "Utility",
            "calculator" => "Utility",
            "clock" => "Utility",
            "texteditor" => "Utility",

            "accessibility" => "Settings",
            "desktopsettings" => "Settings",
            "hardwaresettings" => "Settings",
            "packagesettings" => "Settings",
            "security" => "Settings",

            "emulator" => "System",
            "filesystem" => "System",
            "monitor" => "System",
            "terminalemulator" => "System",

            "languages" => "Education",
            "kids" => "Education",

            // Social and Communication
            "social" => "Network",
            "news" => "Network",

            // Entertainment
            "entertainment" => "AudioVideo",
            "tv" => "AudioVideo",

            // Health and Lifestyle
            "health" => "Education",
            "lifestyle" => "Utility",
            "sports" => "Game",
            "travel" => "Utility",
            "weather" => "Utility",

            // Business
            "business" => "Office",
            "reference" => "Office",

            // Default fallback
            _ => "Utility"
        };
    }

    private static void SetMacroAsEnvironmentVariable(MacroId id, string value)
    {
        try
        {
            // Get the macro variable name (e.g., "${APP_VERSION}")
            var macroVar = GetMacroVariable(id);

            // Extract the environment variable name by removing ${ and }
            // Result: "APP_VERSION"
            var envVarName = macroVar.Replace("${", "").Replace("}", "");

            // Skip setting empty values to avoid polluting environment
            if (value.IsStringNullOrEmpty())
            {
                // Optionally remove the variable if it was previously set
                Environment.SetEnvironmentVariable(envVarName, null, EnvironmentVariableTarget.Process);
                return;
            }

            // Set the environment variable for current process and child processes
            Environment.SetEnvironmentVariable(envVarName, value, EnvironmentVariableTarget.Process);

            Logger.LogDebug("Set environment variable: {0}", envVarName);
        }
        catch (Exception ex)
        {
            // Log warning but don't throw - this shouldn't break the build
            Logger.LogWarning("Failed to set environment variable for macro {0}: {1}", id, ex.Message);
        }
    }
}