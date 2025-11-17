using Netloy.ConsoleApp.Extensions;

namespace Netloy.ConsoleApp;

public static class Confirm
{
    public static bool ShowConfirm(string? message)
    {
        if (message.IsStringNullOrEmpty())
            return false;

        Console.Write($"{message} (y/N) ");
        var result = Console.ReadLine();
        return result?.ToLowerInvariant() == "y" || result?.ToLowerInvariant() == "yes";
    }
}