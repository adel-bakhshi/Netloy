using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Package;

namespace Netloy.ConsoleApp.Helpers;

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
            return (_arguments.PackageType is PackageType.AppBundle or PackageType.Dmg) ? GetMacOSCategoryType(value) : value;
        }
        else
        {
            return value.IsStringNullOrEmpty() ? string.Empty : value;
        }
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

    private static string GetMacOSCategoryType(string category)
    {
        // Map common categories to macOS application category types
        return category.ToLowerInvariant() switch
        {
            "development" => "public.app-category.developer-tools",
            "graphics" => "public.app-category.graphics-design",
            "network" => "public.app-category.networking",
            "utility" => "public.app-category.utilities",
            "game" => "public.app-category.games",
            "office" => "public.app-category.productivity",
            "audiovideo" => "public.app-category.music",
            "education" => "public.app-category.education",
            "finance" => "public.app-category.finance",
            _ => "public.app-category.utilities"
        };
    }
}