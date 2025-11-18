using System.Runtime.InteropServices;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;
using Netloy.ConsoleApp.Package.Linux;
using Netloy.ConsoleApp.Package.Mac;
using Netloy.ConsoleApp.Package.Windows;

namespace Netloy.ConsoleApp.Package;

public class PackageBuilderFactory
{
    #region Private Fields

    private readonly Arguments _arguments;
    private readonly Configurations _configurations;

    #endregion

    public PackageBuilderFactory(Arguments arguments, Configurations configurations)
    {
        _arguments = arguments;
        _configurations = configurations;
    }

    public bool CanCreatePackage()
    {
        Logger.LogInfo("Checking if package can be created");

        _arguments.Runtime = _arguments.Runtime?.ToLowerInvariant();

        return _arguments.PackageType switch
        {
            PackageType.Exe or PackageType.Msi => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsWindowsRuntimeValid(),
            PackageType.AppBundle or PackageType.Dmg => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && IsMacOsRuntimeValid(),
            PackageType.AppImage or PackageType.Deb or PackageType.Rpm or PackageType.Flatpack => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && IsLinuxRuntimeValid(),
            PackageType.Zip => IsZipRuntimeValid(),
            _ => false
        };
    }

    public IPackageBuilder CreatePackageBuilder()
    {
        return _arguments.PackageType switch
        {
            PackageType.Exe => new ExePackageBuilder(_arguments, _configurations),
            PackageType.Msi => new MsiPackageBuilder(_arguments, _configurations),
            PackageType.AppBundle => new AppBundlePackageBuilder(_arguments, _configurations),
            PackageType.Dmg => new DmgPackageBuilder(_arguments, _configurations),
            PackageType.AppImage => new AppImagePackageBuilder(_arguments, _configurations),
            PackageType.Deb => new DebPackageBuilder(_arguments, _configurations),
            PackageType.Rpm => new RpmPackageBuilder(_arguments, _configurations),
            PackageType.Flatpack => new FlatpackPackageBuilder(_arguments, _configurations),
            PackageType.Zip => new ZipPackageBuilder(_arguments, _configurations),
            _ => throw new ArgumentOutOfRangeException(nameof(_arguments.PackageType), _arguments.PackageType, null)
        };
    }

    private bool IsWindowsRuntimeValid()
    {
        if (!_arguments.Runtime.IsStringNullOrEmpty())
        {
            return _arguments.Runtime switch
            {
                "win-x64" => true,
                "win-x86" => true,
                "win-arm64" => true,
                _ => false
            };
        }

        SetWindowsRuntime();

        Logger.LogDebug("Runtime set to {0}", _arguments.Runtime);

        return true;
    }

    private bool IsMacOsRuntimeValid()
    {
        if (!_arguments.Runtime.IsStringNullOrEmpty())
        {
            return _arguments.Runtime switch
            {
                "osx-x64" => true,
                "osx-arm64" => true,
                _ => false
            };
        }

        SetMacOsRuntime();

        Logger.LogDebug("Runtime set to {0}", _arguments.Runtime);

        return true;
    }

    private bool IsLinuxRuntimeValid()
    {
        // TODO: Check for linux-arm or linux-arm32
        if (!_arguments.Runtime.IsStringNullOrEmpty())
        {
            return _arguments.Runtime switch
            {
                "linux-x64" => true,
                "linux-x86" => true,
                "linux-arm64" => true,
                _ => false
            };
        }

        SetLinuxRuntime();

        Logger.LogDebug("Runtime set to {0}", _arguments.Runtime);

        return true;
    }

    private bool IsZipRuntimeValid()
    {
        if (!_arguments.Runtime.IsStringNullOrEmpty())
        {
            return _arguments.Runtime switch
            {
                "win-x64" => true,
                "win-x86" => true,
                "win-arm64" => true,
                "osx-x64" => true,
                "osx-arm64" => true,
                "linux-x64" => true,
                "linux-x86" => true,
                "linux-arm64" => true,
                _ => false
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetWindowsRuntime();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetMacOsRuntime();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            SetLinuxRuntime();
        }
        else
        {
            throw new PlatformNotSupportedException();
        }

        return true;
    }

    private void SetWindowsRuntime()
    {
        _arguments.Runtime = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void SetMacOsRuntime()
    {
        _arguments.Runtime = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "osx-x64",
            Architecture.Arm64 => "osx-arm64",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void SetLinuxRuntime()
    {
        _arguments.Runtime = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.X86 => "linux-x86",
            Architecture.Arm64 => "linux-arm64",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}