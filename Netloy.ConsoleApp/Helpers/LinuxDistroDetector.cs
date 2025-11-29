using System.Runtime.InteropServices;

namespace Netloy.ConsoleApp.Helpers;

public enum LinuxDistroType
{
    Unknown,
    Debian, // Ubuntu, Mint, Debian, etc.
    RedHat // Fedora, RHEL, openSUSE, etc.
}

public static class LinuxDistroDetector
{
    private static LinuxDistroType? _cachedType;

    public static LinuxDistroType GetDistroType()
    {
        if (_cachedType.HasValue)
            return _cachedType.Value;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _cachedType = LinuxDistroType.Unknown;
            return LinuxDistroType.Unknown;
        }

        // Check /etc/os-release file
        const string osReleaseFile = "/etc/os-release";
        if (File.Exists(osReleaseFile))
        {
            var content = File.ReadAllText(osReleaseFile).ToLowerInvariant();

            // Check for Debian-based distros
            if (content.Contains("id=ubuntu") ||
                content.Contains("id=debian") ||
                content.Contains("id=linuxmint") ||
                content.Contains("id_like=debian") ||
                content.Contains("id_like=ubuntu"))
            {
                _cachedType = LinuxDistroType.Debian;
                return LinuxDistroType.Debian;
            }

            // Check for RPM-based distros
            if (content.Contains("id=fedora") ||
                content.Contains("id=rhel") ||
                content.Contains("id=centos") ||
                content.Contains("id=rocky") ||
                content.Contains("id=alma") ||
                content.Contains("id=opensuse") ||
                content.Contains("id_like=fedora") ||
                content.Contains("id_like=rhel"))
            {
                _cachedType = LinuxDistroType.RedHat;
                return LinuxDistroType.RedHat;
            }
        }

        _cachedType = LinuxDistroType.Unknown;
        return LinuxDistroType.Unknown;
    }
}