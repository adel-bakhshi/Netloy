using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;

namespace Netloy.ConsoleApp.Extensions;

public static class MacroExtensions
{
    private static List<string> _macros =
    [
        "${APP_BASE_NAME}",
    ];

    public static string? ReplaceMacros(this string? input, Configurations configs, Arguments arguments)
    {
        if (input.IsStringNullOrEmpty())
            return null;

        return null;
    }
}