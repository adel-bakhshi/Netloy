using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.Helpers;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package.Mac;

public class AppBundlePackageBuilder : PackageBuilderBase, IPackageBuilder
{
    #region Constants

    private const string InfoPlistFileName = "Info.plist";
    private const string PkgInfoFileName = "PkgInfo";
    private const string EntitlementsFileName = "Entitlements.plist";

    #endregion

    #region Properties

    public string PublishOutputDir { get; }
    public string AppBundlePath { get; }
    public string ContentsDirectory { get; }
    public string MacOSDirectory { get; }
    public string ResourcesDirectory { get; }
    public string InfoPlistPath { get; }
    public string EntitlementsPath { get; }

    #endregion

    public AppBundlePackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        var appBundleName = $"{Configurations.AppFriendlyName}.app";
        AppBundlePath = Path.Combine(RootDirectory, appBundleName);
        ContentsDirectory = Path.Combine(AppBundlePath, "Contents");
        MacOSDirectory = Path.Combine(ContentsDirectory, "MacOS");
        ResourcesDirectory = Path.Combine(ContentsDirectory, "Resources");
        InfoPlistPath = Path.Combine(ContentsDirectory, InfoPlistFileName);
        EntitlementsPath = Path.Combine(ResourcesDirectory, EntitlementsFileName);
        PublishOutputDir = MacOSDirectory;
    }

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting macOS App Bundle package build...", forceLog: true);

        // Create App Bundle structure
        Logger.LogInfo("Creating .app bundle structure...");
        CreateAppBundleStructure();

        // Publish project
        await PublishAsync(PublishOutputDir, ".icns");

        // Copy icon to Resources directory
        Logger.LogInfo("Copying application icon...");
        CopyApplicationIcon();

        // Generate or copy Info.plist
        Logger.LogInfo("Processing Info.plist...");
        await ProcessInfoPlistAsync();

        // Generate or copy Entitlements
        Logger.LogInfo("Processing Entitlements...");
        await ProcessEntitlementsAsync();

        // Generate PkgInfo
        Logger.LogInfo("Generating PkgInfo...");
        GeneratePkgInfo();

        // Set executable permissions
        Logger.LogInfo("Setting executable permissions...");
        SetExecutablePermissions();

        // Create zip archive
        Logger.LogInfo("Creating zip archive...");
        CreateZipArchive();

        Logger.LogSuccess("macOS App Bundle package build completed successfully!");
    }

    public bool Validate()
    {
        var errors = new List<string>();

        // Validate icon file
        var icnsIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".icns", StringComparison.OrdinalIgnoreCase));
        if (icnsIcon.IsStringNullOrEmpty())
            errors.Add("No .icns icon file found. macOS App Bundle requires an .icns icon file.");

        // Validate Info.plist file if specified
        if (Configurations.MacOsInfoPlist.IsStringNullOrEmpty() || !File.Exists(Configurations.MacOsInfoPlist))
            errors.Add($"Info.plist file not found: {Configurations.MacOsInfoPlist}");

        // Validate Entitlements file if specified
        if (Configurations.MacOsEntitlements.IsStringNullOrEmpty() || !File.Exists(Configurations.MacOsEntitlements))
            errors.Add($"Entitlements file not found: {Configurations.MacOsEntitlements}");

        if (errors.Count > 0)
        {
            var errorMessage = $"The following errors were found:\n\n{string.Join("\n", errors)}";
            throw new InvalidOperationException(errorMessage);
        }

        return true;
    }

    public void Clear()
    {
        try
        {
            Logger.LogInfo("Cleaning up '{0}'...", RootDirectory);
            Directory.Delete(RootDirectory, true);
            Logger.LogSuccess("Cleanup completed successfully!");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            throw;
        }
    }

    #region App Bundle Structure

    private void CreateAppBundleStructure()
    {
        // Clean previous build
        if (Directory.Exists(AppBundlePath))
            Directory.Delete(AppBundlePath, true);

        // Create directory structure
        Directory.CreateDirectory(AppBundlePath);
        Directory.CreateDirectory(ContentsDirectory);
        Directory.CreateDirectory(MacOSDirectory);
        Directory.CreateDirectory(ResourcesDirectory);

        Logger.LogInfo("App bundle structure created at: {0}", AppBundlePath);
    }

    #endregion

    #region File Operations

    private void CopyApplicationIcon()
    {
        var icnsIcon = Configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(".icns", StringComparison.OrdinalIgnoreCase));
        if (icnsIcon.IsStringNullOrEmpty())
        {
            Logger.LogWarning("No .icns icon found. App will use default icon.");
            return;
        }

        var iconFileName = $"{Configurations.AppBaseName}.icns";
        var destPath = Path.Combine(ResourcesDirectory, iconFileName);

        File.Copy(icnsIcon!, destPath, overwrite: true);
        Logger.LogInfo("Icon copied to Resources directory: {0}", iconFileName);
    }

    private void SetExecutablePermissions()
    {
        // Set executable permission for main binary
        var mainExecutable = Path.Combine(MacOSDirectory, AppExecName);

        if (!File.Exists(mainExecutable))
        {
            Logger.LogWarning("Main executable not found: {0}", mainExecutable);
            return;
        }

        try
        {
            // Use chmod to set executable permissions (Unix-like systems only)
            var processInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{mainExecutable}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    Logger.LogInfo("Executable permissions set for: {0}", Path.GetFileName(mainExecutable));
                }
                else
                {
                    Logger.LogWarning("Failed to set executable permissions. Exit code: {0}", process.ExitCode);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not set executable permissions: {0}", ex.Message);
        }

        // Set read permissions recursively
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"-R a+r \"{AppBundlePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not set read permissions: {0}", ex.Message);
        }
    }

    #endregion

    #region Info.plist Processing

    private async Task ProcessInfoPlistAsync()
    {
        string plistContent;

        // Read user-provided Info.plist and expand macros
        Logger.LogInfo("Using custom Info.plist from: {0}", Configurations.MacOsInfoPlist);
        var rawContent = await File.ReadAllTextAsync(Configurations.MacOsInfoPlist);
        plistContent = MacroExpander.ExpandMacros(rawContent);

        // Write to Info.plist
        await File.WriteAllTextAsync(InfoPlistPath, plistContent, Encoding.UTF8);
        Logger.LogInfo("Info.plist saved at: {0}", InfoPlistPath);
    }

    private XDocument CreateDefaultInfoPlistXml()
    {
        var iconFileName = $"{Configurations.AppBaseName}.icns";
        var buildNumber = DateTime.UtcNow.ToString("yyyyMMdd.HHmmss");
        var minimumSystemVersion = Configurations.MacOsMinimumSystemVersion.IsStringNullOrEmpty()
            ? "10.13"
            : Configurations.MacOsMinimumSystemVersion;

        var dict = new XElement("dict",
            CreatePlistKeyValue("CFBundleDevelopmentRegion", "en"),
            CreatePlistKeyValue("CFBundleDisplayName", Configurations.AppFriendlyName),
            CreatePlistKeyValue("CFBundleExecutable", Path.GetFileNameWithoutExtension(AppExecName)),
            CreatePlistKeyValue("CFBundleIconFile", iconFileName),
            CreatePlistKeyValue("CFBundleIdentifier", Configurations.AppId),
            CreatePlistKeyValue("CFBundleInfoDictionaryVersion", "6.0"),
            CreatePlistKeyValue("CFBundleName", Configurations.AppFriendlyName),
            CreatePlistKeyValue("CFBundlePackageType", "APPL"),
            CreatePlistKeyValue("CFBundleShortVersionString", AppVersion),
            CreatePlistKeyValue("CFBundleVersion", buildNumber),
            CreatePlistKeyValue("LSMinimumSystemVersion", minimumSystemVersion),
            CreatePlistKeyValue("NSHighResolutionCapable", true),
            CreatePlistKeyValue("NSPrincipalClass", "NSApplication"),
            CreatePlistKeyValue("LSRequiresCarbon", true),
            CreatePlistKeyValue("CSResourcesFileMapped", true)
        );

        // Add copyright if available
        if (!Configurations.PublisherCopyright.IsStringNullOrEmpty())
        {
            dict.Add(CreatePlistKeyValue("NSHumanReadableCopyright", Configurations.PublisherCopyright));
        }

        // Add category if available
        if (!Configurations.PrimeCategory.IsStringNullOrEmpty())
        {
            var categoryType = GetMacOSCategoryType(Configurations.PrimeCategory);
            dict.Add(CreatePlistKeyValue("LSApplicationCategoryType", categoryType));
        }

        // Add file associations if configured
        if (Configurations.AssociateFiles && !Configurations.FileExtension.IsStringNullOrEmpty())
        {
            var docTypesArray = CreateFileAssociations();
            dict.Add(new XElement("key", "CFBundleDocumentTypes"));
            dict.Add(docTypesArray);
        }

        var plist = new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist",
                new XAttribute("version", "1.0"),
                dict
            )
        );

        return plist;
    }

    private XElement[] CreatePlistKeyValue(string key, string value)
    {
        return new[]
        {
            new XElement("key", key),
            new XElement("string", value)
        };
    }

    private XElement[] CreatePlistKeyValue(string key, bool value)
    {
        return new[]
        {
            new XElement("key", key),
            new XElement(value ? "true" : "false")
        };
    }

    private XElement CreateFileAssociations()
    {
        var ext = Configurations.FileExtension.StartsWith('.')
            ? Configurations.FileExtension
            : $".{Configurations.FileExtension}";

        var docTypesArray = new XElement("array",
            new XElement("dict",
                new XElement("key", "CFBundleTypeExtensions"),
                new XElement("array",
                    new XElement("string", ext.TrimStart('.'))
                ),
                new XElement("key", "CFBundleTypeName"),
                new XElement("string", $"{Configurations.AppFriendlyName} File"),
                new XElement("key", "CFBundleTypeRole"),
                new XElement("string", "Editor"),
                new XElement("key", "LSHandlerRank"),
                new XElement("string", "Owner"),
                new XElement("key", "LSTypeIsPackage"),
                new XElement("false")
            )
        );

        return docTypesArray;
    }

    private string GetMacOSCategoryType(string category)
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

    #endregion

    #region Entitlements Processing

    private async Task ProcessEntitlementsAsync()
    {
        string entitlementsContent;

        if (!Configurations.MacOsEntitlement.IsStringNullOrEmpty() &&
            File.Exists(Configurations.MacOsEntitlement))
        {
            // Read user-provided Entitlements and expand macros
            Logger.LogInfo("Using custom Entitlements from: {0}", Configurations.MacOsEntitlement);
            var rawContent = await File.ReadAllTextAsync(Configurations.MacOsEntitlement);
            entitlementsContent = MacroExpander.ExpandMacros(rawContent);
        }
        else
        {
            // Generate default Entitlements
            Logger.LogInfo("Generating default Entitlements...");
            entitlementsContent = CreateDefaultEntitlements();
        }

        // Write to Entitlements.plist
        await File.WriteAllTextAsync(EntitlementsPath, entitlementsContent, Encoding.UTF8);
        Logger.LogInfo("Entitlements saved at: {0}", EntitlementsPath);
    }

    private string CreateDefaultEntitlements()
    {
        var entitlements = new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist",
                new XAttribute("version", "1.0"),
                new XElement("dict",
                    // Basic entitlements for .NET applications
                    new XElement("key", "com.apple.security.app-sandbox"),
                    new XElement("false"),

                    new XElement("key", "com.apple.security.cs.allow-jit"),
                    new XElement("true"),

                    new XElement("key", "com.apple.security.cs.allow-unsigned-executable-memory"),
                    new XElement("true"),

                    new XElement("key", "com.apple.security.cs.allow-dyld-environment-variables"),
                    new XElement("true"),

                    new XElement("key", "com.apple.security.cs.disable-library-validation"),
                    new XElement("true"),

                    // Network access
                    new XElement("key", "com.apple.security.network.client"),
                    new XElement("true"),

                    new XElement("key", "com.apple.security.network.server"),
                    new XElement("true"),

                    // File access
                    new XElement("key", "com.apple.security.files.user-selected.read-write"),
                    new XElement("true"),

                    // Allow incoming connections
                    new XElement("key", "com.apple.security.network.incoming-connections"),
                    new XElement("true")
                )
            )
        );

        using var stringWriter = new StringWriter();
        using var xmlWriter = System.Xml.XmlWriter.Create(stringWriter, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        });

        entitlements.WriteTo(xmlWriter);
        xmlWriter.Flush();
        return stringWriter.ToString();
    }

    #endregion

    #region PkgInfo Generation

    private void GeneratePkgInfo()
    {
        var pkgInfoPath = Path.Combine(ContentsDirectory, PkgInfoFileName);

        // PkgInfo contains 8 bytes: 4 for type code (APPL) and 4 for creator code
        // Using ???? as creator code (unknown/generic)
        File.WriteAllText(pkgInfoPath, "APPL????", Encoding.ASCII);

        Logger.LogInfo("PkgInfo generated at: {0}", pkgInfoPath);
    }

    #endregion

    #region Zip Archive Creation

    private void CreateZipArchive()
    {
        var zipPath = Path.Combine(OutputDirectory, OutputName);

        // Delete existing zip if exists
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        try
        {
            // Use ditto command for better macOS compatibility (preserves metadata)
            if (IsDittoAvailable())
            {
                CreateZipWithDitto(zipPath);
            }
            else
            {
                // Fallback to .NET zip
                CreateZipWithDotNet(zipPath);
            }

            Logger.LogInfo("Zip archive created at: {0}", zipPath);

            var fileInfo = new FileInfo(zipPath);
            Logger.LogInfo("Archive size: {0:F2} MB", fileInfo.Length / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to create zip archive: {0}", forceLog: true, ex.Message);
            throw;
        }
    }

    private bool IsDittoAvailable()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "ditto",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void CreateZipWithDitto(string zipPath)
    {
        var appBundleName = Path.GetFileName(AppBundlePath);
        var appBundleParent = Path.GetDirectoryName(AppBundlePath);

        var processInfo = new ProcessStartInfo
        {
            FileName = "ditto",
            Arguments = $"-c -k --sequesterRsrc --keepParent \"{appBundleName}\" \"{zipPath}\"",
            WorkingDirectory = appBundleParent,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start ditto command.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ditto command failed: {error}");

        Logger.LogInfo("Archive created using ditto (preserves macOS metadata).");
    }

    private void CreateZipWithDotNet(string zipPath)
    {
        // Create temporary directory for the .app bundle
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var appBundleName = Path.GetFileName(AppBundlePath);
            var tempAppPath = Path.Combine(tempDir, appBundleName);

            // Copy .app bundle to temp directory
            CopyDirectory(AppBundlePath, tempAppPath);

            // Create zip from temp directory
            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            Logger.LogInfo("Archive created using .NET compression.");
        }
        finally
        {
            // Cleanup temp directory
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    #endregion
}