using System.Text.RegularExpressions;

namespace Netloy.ConsoleApp.Extensions;

public static partial class CommonExtensions
{
    public static bool IsStringNullOrEmpty(this string? value)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value);
    }

    public static bool CheckLongVersion(this string version)
    {
        return LongVersionPatternRegex().IsMatch(version);
    }

    public static bool CheckShortVersion(this string version)
    {
        return ShortVersionCheckerRegex().IsMatch(version);
    }

    public static bool CheckDnsPattern(this string appId)
    {
        return DnsPatternRegex().IsMatch(appId);
    }

    public static bool CheckUrlValidation(this string? url)
    {
        return !url.IsStringNullOrEmpty() && Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    public static bool CheckEmailValidation(this string? email)
    {
        return !email.IsStringNullOrEmpty() && EmailPatternRegex().IsMatch(email!);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(\[\d+\])?$")]
    private static partial Regex LongVersionPatternRegex();

    [GeneratedRegex(@"^(\d+\.)?(\d+\.)?(\*|\d+)$")]
    private static partial Regex ShortVersionCheckerRegex();

    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$")]
    private static partial Regex DnsPatternRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPatternRegex();
}