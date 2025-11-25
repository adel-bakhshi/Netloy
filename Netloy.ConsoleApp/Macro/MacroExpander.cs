using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Package;

namespace Netloy.ConsoleApp.Macro;

public class MacroExpander
{
    #region Private Fields

    private readonly Dictionary<MacroId, string> _macros;
    private readonly Arguments _arguments;

    #endregion

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
        if (!_macros.TryAdd(id, value))
            _macros[id] = value;
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
                PackageType.AppImage or PackageType.Flatpak or PackageType.Rpm or PackageType.Deb => GetLinuxCategoryType(value),
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
}