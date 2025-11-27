using Netloy.ConsoleApp.Extensions;
using System.Text;
using System.Text.RegularExpressions;

namespace Netloy.ConsoleApp.Helpers;

/// <summary>
/// Helper class for generating AppStream metadata XML content
/// </summary>
public static partial class AppStreamMetadataHelper
{
    /// <summary>
    /// Generates AppStream description XML from plain text description
    /// </summary>
    /// <param name="description">Plain text description with paragraphs separated by empty lines</param>
    /// <returns>XML formatted description with p tags</returns>
    public static string GenerateDescriptionXml(string description)
    {
        if (description.IsStringNullOrEmpty())
            return string.Empty;

        var result = new StringBuilder();

        // Split by empty lines to get paragraphs
        var paragraphs = description
            .Split(["\r\n\r\n", "\n\n", "\r\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !p.IsStringNullOrEmpty())
            .ToList();

        foreach (var paragraph in paragraphs)
        {
            // Clean up whitespace and normalize line breaks
            var cleanedParagraph = NormalizeWhitespace(paragraph);

            // Check if paragraph contains list items
            if (ContainsListItems(cleanedParagraph))
            {
                result.AppendLine(GenerateListXml(cleanedParagraph));
            }
            else
            {
                // Regular paragraph
                var escapedText = XmlEscape(cleanedParagraph);
                result.AppendLine($"    <p>{escapedText}</p>");
            }
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// Generates AppStream changelog XML from CHANGES file content
    /// </summary>
    /// <param name="changelogFilePath">Path to the CHANGES file</param>
    /// <param name="maxReleases">Maximum number of releases to include (default: 5)</param>
    /// <returns>XML formatted changelog with releases</returns>
    public static async Task<string> GenerateChangelogXmlAsync(string changelogFilePath, int maxReleases = 5)
    {
        if (changelogFilePath.IsStringNullOrEmpty() || !File.Exists(changelogFilePath))
            return string.Empty;

        var content = await File.ReadAllTextAsync(changelogFilePath);
        return GenerateChangelogXmlFromContent(content, maxReleases);
    }

    /// <summary>
    /// Generates AppStream changelog XML from changelog content string
    /// </summary>
    /// <param name="changelogContent">Content of the changelog</param>
    /// <param name="maxReleases">Maximum number of releases to include (default: 5)</param>
    /// <returns>XML formatted changelog with releases</returns>
    public static string GenerateChangelogXmlFromContent(string changelogContent, int maxReleases = 5)
    {
        if (changelogContent.IsStringNullOrEmpty())
            return string.Empty;

        var releases = ParseChangelog(changelogContent);

        if (releases.Count == 0)
            return string.Empty;

        var result = new StringBuilder();
        foreach (var release in releases.Take(maxReleases))
        {
            result.AppendLine($"    <release version=\"{XmlEscape(release.Version)}\" date=\"{release.Date:yyyy-MM-dd}\">");
            result.AppendLine("      <description>");

            // Group changes by category if they have prefixes
            var groupedChanges = GroupChangesByCategory(release.Changes);

            if (groupedChanges.Count > 1)
            {
                // Multiple categories - use separate lists
                foreach (var group in groupedChanges)
                {
                    if (!string.IsNullOrEmpty(group.Key))
                        result.AppendLine($"        <p>{XmlEscape(group.Key)}:</p>");

                    result.AppendLine("        <ul>");
                    foreach (var change in group.Value)
                        result.AppendLine($"          <li>{XmlEscape(change)}</li>");

                    result.AppendLine("        </ul>");
                }
            }
            else
            {
                // Single category or no categories - use one list
                result.AppendLine("        <ul>");
                foreach (var change in release.Changes)
                    result.AppendLine($"          <li>{XmlEscape(change)}</li>");

                result.AppendLine("        </ul>");
            }

            result.AppendLine("      </description>");
            result.AppendLine("    </release>");
        }

        return result.ToString().TrimEnd();
    }

    #region Private Helper Methods

    private static List<ReleaseInfo> ParseChangelog(string content)
    {
        var releases = new List<ReleaseInfo>();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.None);

        ReleaseInfo? currentRelease = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Check if this is a version header line: + Version 0.12.0; 2025-10-29
            if (trimmedLine.StartsWith('+') && trimmedLine.Contains("Version"))
            {
                // Save previous release if exists
                if (currentRelease?.Changes.Count > 0)
                    releases.Add(currentRelease);

                // Parse new release
                currentRelease = ParseReleaseHeader(trimmedLine);
            }
            // Check if this is a change item line: - ...
            else if (trimmedLine.StartsWith('-') && currentRelease != null)
            {
                var changeText = trimmedLine.Substring(1).Trim();
                if (!string.IsNullOrWhiteSpace(changeText))
                    currentRelease.Changes.Add(changeText);
            }
        }

        // Add last release
        if (currentRelease?.Changes.Count > 0)
            releases.Add(currentRelease);

        return releases;
    }

    private static ReleaseInfo? ParseReleaseHeader(string line)
    {
        // Format: + Version 0.12.0; 2025-10-29
        var match = ReleaseHeaderRegex().Match(line);

        if (match.Success)
        {
            var version = match.Groups[1].Value;
            var year = int.Parse(match.Groups[2].Value);
            var month = int.Parse(match.Groups[3].Value);
            var day = int.Parse(match.Groups[4].Value);

            return new ReleaseInfo
            {
                Version = version,
                Date = new DateTime(year, month, day),
                Changes = []
            };
        }

        return null;
    }

    private static Dictionary<string, List<string>> GroupChangesByCategory(List<string> changes)
    {
        var groups = new Dictionary<string, List<string>>();
        var currentCategory = string.Empty;

        foreach (var change in changes)
        {
            // Check if this line is a category header (ends with colon)
            if (change.EndsWith(':') && !change.Contains('.'))
            {
                currentCategory = change.TrimEnd(':');
                if (!groups.ContainsKey(currentCategory))
                    groups[currentCategory] = [];
            }
            else
            {
                // This is a regular change item
                if (!groups.ContainsKey(currentCategory))
                    groups[currentCategory] = [];

                groups[currentCategory].Add(change);
            }
        }

        return groups;
    }

    private static bool ContainsListItems(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return lines.Any(line =>
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith("* ") ||
                   trimmed.StartsWith("+ ") ||
                   trimmed.StartsWith("- ");
        });
    }

    private static string GenerateListXml(string text)
    {
        var result = new StringBuilder();
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var inList = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("* ") || trimmed.StartsWith("+ ") || trimmed.StartsWith("- "))
            {
                if (!inList)
                {
                    result.AppendLine("    <ul>");
                    inList = true;
                }

                var itemText = trimmed.Substring(2).Trim();
                result.AppendLine($"      <li>{XmlEscape(itemText)}</li>");
            }
            else if (inList)
            {
                result.AppendLine("    </ul>");
                result.AppendLine($"    <p>{XmlEscape(trimmed)}</p>");
                inList = false;
            }
            else
            {
                result.AppendLine($"    <p>{XmlEscape(trimmed)}</p>");
            }
        }

        if (inList)
        {
            result.AppendLine("    </ul>");
        }

        return result.ToString().TrimEnd();
    }

    private static string NormalizeWhitespace(string text)
    {
        // Replace multiple whitespace with single space
        text = NormalizedWhiteSpaceRegex().Replace(text, " ");

        // Normalize line breaks
        text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

        return text.Trim();
    }

    private static string XmlEscape(string text)
    {
        if (text.IsStringNullOrEmpty())
            return text;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    #endregion

    #region Helper Classes

    [GeneratedRegex(@"\s+")]
    private static partial Regex NormalizedWhiteSpaceRegex();

    [GeneratedRegex(@"\+\s*Version\s+([\d.]+)\s*;\s*(\d{4})-(\d{2})-(\d{2})")]
    private static partial Regex ReleaseHeaderRegex();

    #endregion
}

internal class ReleaseInfo
{
    public string Version { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public List<string> Changes { get; set; } = [];
}