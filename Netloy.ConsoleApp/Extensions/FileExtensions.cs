using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Netloy.ConsoleApp.Extensions;

public static partial class FileExtensions
{
    public static string NormalizePath(this string path)
    {
        if (path.IsStringNullOrEmpty())
            return path;

        // Remove quotes and trim whitespace
        var normalized = path.Replace("\"", "").Trim();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Standardize path separators
            normalized = normalized.Replace("/", "\\");

            // Remove duplicate separators
            return NormalizePathRegex().Replace(normalized, "\\");
        }
        else
        {
            return normalized.Replace("\\", "/");
        }
    }

    public static bool IsAbsolutePath(this string path)
    {
        return Path.IsPathRooted(path);
    }

    [GeneratedRegex(@"\\+")]
    private static partial Regex NormalizePathRegex();
}