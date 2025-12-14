using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;
using System.Text;

namespace Netloy.ConsoleApp.NetloyFiles;

public class FileCreator
{
    #region Private Fields

    private readonly Arguments _arguments;

    #endregion Private Fields

    #region Constructor

    public FileCreator(Arguments arguments)
    {
        _arguments = arguments;
    }

    #endregion Constructor

    #region Public Methods

    public async Task CreateNewFileAsync()
    {
        string directoryPath;
        var outputPath = _arguments.OutputPath;
        var currentDir = Directory.GetCurrentDirectory();

        if (!outputPath.IsStringNullOrEmpty())
        {
            var combinedPath = outputPath!.IsAbsolutePath()
                ? outputPath
                : Path.Combine(currentDir, outputPath!);

            directoryPath = Path.GetDirectoryName(combinedPath)
                            ?? throw new InvalidOperationException("Cannot detect directory from output path.");
        }
        else
        {
            directoryPath = currentDir;
        }

        if (directoryPath.IsStringNullOrEmpty())
            throw new InvalidOperationException("Couldn't find directory to create file(s).");

        Logger.LogInfo($"Creating file(s) in \"{directoryPath}\"");

        var fileName = Path.GetFileName(outputPath) ?? string.Empty;
        if (fileName.IsStringNullOrEmpty())
            fileName = "app";

        if (!_arguments.SkipAll)
        {
            var confirmMsg = $"Are you sure you want to create file(s) in \"{directoryPath}\" with name \"{fileName}\"?";
            if (!Confirm.ShowConfirm(confirmMsg))
                throw new OperationCanceledException("Operation cancelled by user");
        }
        else
        {
            var isMultiple = _arguments.NewType == NewFileType.All;
            var message = $"File name{(isMultiple ? "s" : "")} {(isMultiple ? "are" : "is")} " +
                          $"set to \"{fileName}\" and {(isMultiple ? "they" : "it")} will be saved in \"{directoryPath}\"";

            Logger.LogWarning(message);
        }

        if (!Directory.Exists(directoryPath))
            Directory.CreateDirectory(directoryPath);

        switch (_arguments.NewType)
        {
            case NewFileType.All:
            {
                await GenerateConfigurationFileAsync(directoryPath, fileName);
                await GenerateDesktopFileAsync(directoryPath, fileName);
                await GenerateMetaFileAsync(directoryPath, fileName);
                await GeneratePlistFileAsync(directoryPath, fileName);
                await GenerateEntitlementsFileAsync(directoryPath, fileName);
                Logger.LogSuccess("All configuration files created successfully");
                break;
            }

            case NewFileType.Conf:
            {
                await GenerateConfigurationFileAsync(directoryPath, fileName);
                Logger.LogSuccess("Netloy configuration file successfully created");
                break;
            }

            case NewFileType.Desktop:
            {
                await GenerateDesktopFileAsync(directoryPath, fileName);
                Logger.LogSuccess("Linux desktop file successfully created");
                break;
            }

            case NewFileType.Meta:
            {
                await GenerateMetaFileAsync(directoryPath, fileName);
                Logger.LogSuccess("AppImage meta file successfully created");
                break;
            }

            case NewFileType.Plist:
            {
                await GeneratePlistFileAsync(directoryPath, fileName);
                Logger.LogSuccess("macOS plist file successfully created");
                break;
            }

            case NewFileType.Entitle:
            {
                await GenerateEntitlementsFileAsync(directoryPath, fileName);
                Logger.LogSuccess("macOS entitlements file successfully created");
                break;
            }

            default:
                throw new InvalidOperationException("File type not specified. Use --new <type>");
        }
    }

    #endregion Public Methods

    #region File Generator

    private async Task GenerateConfigurationFileAsync(string directoryPath, string fileName)
    {
        var configFilePath = Path.Combine(directoryPath, $"{fileName}{Constants.NetloyConfigFileExt}");
        var configParser = new ConfigurationParser(_arguments);
        await configParser.CreateDefaultConfigFileAsync(configFilePath);
    }

    private async Task GenerateDesktopFileAsync(string directoryPath, string fileName)
    {
        var desktopFilePath = Path.Combine(directoryPath, $"{fileName}.desktop");

        if (File.Exists(desktopFilePath))
        {
            if (!_arguments.SkipAll)
            {
                Logger.LogWarning("Desktop file already exists: {0}", desktopFilePath);
                if (!Confirm.ShowConfirm("Overwrite?"))
                {
                    Logger.LogInfo("Operation cancelled");
                    return;
                }
            }
            else
            {
                Logger.LogWarning("Desktop file will be overwritten: {0}", desktopFilePath);
            }
        }

        var sb = new StringBuilder();

        sb.AppendLine("[Desktop Entry]");
        sb.AppendLine("Type=Application");
        sb.AppendLine("Name=${APP_FRIENDLY_NAME}");
        sb.AppendLine("GenericName=${APP_FRIENDLY_NAME}");
        sb.AppendLine("Icon=${APP_ID}");
        sb.AppendLine("Comment=${APP_SHORT_SUMMARY}");
        sb.AppendLine("Exec=${INSTALL_EXEC}");
        sb.AppendLine("TryExec=${INSTALL_EXEC}");
        sb.AppendLine("StartupWMClass=${APP_BASE_NAME}");
        sb.AppendLine("NoDisplay=${DESKTOP_NODISPLAY}");
        sb.AppendLine("X-AppImage-Integrate=${DESKTOP_INTEGRATE}");
        sb.AppendLine("Terminal=${DESKTOP_TERMINAL}");
        sb.AppendLine("Categories=${PRIME_CATEGORY};");

        await File.WriteAllTextAsync(desktopFilePath, sb.ToString());
        Logger.LogSuccess("Desktop file created: {0}", desktopFilePath);
    }

    private async Task GenerateMetaFileAsync(string directoryPath, string fileName)
    {
        var metaFilePath = Path.Combine(directoryPath, $"{fileName}.metainfo.xml");

        if (File.Exists(metaFilePath))
        {
            if (!_arguments.SkipAll)
            {
                Logger.LogWarning("Meta file already exists: {0}", metaFilePath);
                if (!Confirm.ShowConfirm("Overwrite?"))
                {
                    Logger.LogInfo("Operation cancelled");
                    return;
                }
            }
            else
            {
                Logger.LogWarning("Meta file will be overwritten: {0}", metaFilePath);
            }
        }

        var sb = new StringBuilder();

        // XML Declaration
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!-- ${PUBLISHER_COPYRIGHT} -->");
        sb.AppendLine("<component type=\"desktop-application\">");
        sb.AppendLine("  <metadata_license>${APP_LICENSE_ID}</metadata_license>");
        sb.AppendLine();

        // Note about macros
        sb.AppendLine("  <!-- All ${MACRO} placeholders will be replaced with actual values during build -->");
        sb.AppendLine();

        // Basic metadata
        sb.AppendLine("  <id>${APP_ID}</id>");
        sb.AppendLine("  <name>${APP_FRIENDLY_NAME}</name>");
        sb.AppendLine("  <summary>${APP_SHORT_SUMMARY}</summary>");

        sb.AppendLine("  <developer id=\"${PUBLISHER_ID}\">");
        sb.AppendLine("    <name>${PUBLISHER_NAME}</name>");
        sb.AppendLine("  </developer>");

        sb.AppendLine("  <url type=\"homepage\">${PUBLISHER_LINK_URL}</url>");
        sb.AppendLine("  <project_license>${APP_LICENSE_ID}</project_license>");
        sb.AppendLine("  <content_rating type=\"oars-1.1\" />");
        sb.AppendLine();

        // Launchable
        sb.AppendLine("  <launchable type=\"desktop-id\">${APP_ID}.desktop</launchable>");
        sb.AppendLine();

        // Description
        sb.AppendLine("  <description>");
        sb.AppendLine("    <!-- See AppDescription in configuration -->");
        sb.AppendLine("    ${APPSTREAM_DESCRIPTION_XML}");
        sb.AppendLine("  </description>");
        sb.AppendLine();

        // Categories
        sb.AppendLine("  <!-- Freedesktop Categories -->");
        sb.AppendLine("  <categories>");
        sb.AppendLine("    <!-- Add appropriate categories for your app -->");
        sb.AppendLine("    <category>${PRIME_CATEGORY}</category>");
        sb.AppendLine("  </categories>");
        sb.AppendLine();

        // Keywords (commented)
        sb.AppendLine("  <!-- Uncomment and add keywords for better discoverability -->");
        sb.AppendLine("  <!-- <keywords> -->");
        sb.AppendLine("  <!--   <keyword>example</keyword> -->");
        sb.AppendLine("  <!--   <keyword>app</keyword> -->");
        sb.AppendLine("  <!-- </keywords> -->");
        sb.AppendLine();

        // Screenshots (commented)
        sb.AppendLine("  <!-- Uncomment and add screenshots -->");
        sb.AppendLine("  <!-- <screenshots> -->");
        sb.AppendLine("  <!--   <screenshot type=\"default\"> -->");
        sb.AppendLine("  <!--     <image>https://example.com/screenshot1.png</image> -->");
        sb.AppendLine("  <!--     <caption>Main window</caption> -->");
        sb.AppendLine("  <!--   </screenshot> -->");
        sb.AppendLine("  <!-- </screenshots> -->");
        sb.AppendLine();

        // Releases
        sb.AppendLine("  <releases>");
        sb.AppendLine("      <!-- See AppChangeFile in configuration -->");
        sb.AppendLine("      ${APPSTREAM_CHANGELOG_XML}");
        sb.AppendLine("      <!-- Or, uncomment below and delete macro directly above to specify changes yourself");
        sb.AppendLine("      <release version=\"1.0.0\" date=\"2023-05-04\">");
        sb.AppendLine("          <description>");
        sb.AppendLine("              <ul>");
        sb.AppendLine("                  <li>Added feature 1</li>");
        sb.AppendLine("                  <li>Added feature 2</li>");
        sb.AppendLine("              </ul>");
        sb.AppendLine("          </description>");
        sb.AppendLine("      </release>");
        sb.AppendLine("      -->");
        sb.AppendLine("  </releases>");
        sb.AppendLine();

        // Closing tag
        sb.AppendLine("</component>");

        await File.WriteAllTextAsync(metaFilePath, sb.ToString());
        Logger.LogSuccess("AppStream metadata file created: {0}", metaFilePath);
    }

    private async Task GeneratePlistFileAsync(string directoryPath, string fileName)
    {
        var plistFilePath = Path.Combine(directoryPath, $"{fileName}.plist");

        if (File.Exists(plistFilePath))
        {
            if (!_arguments.SkipAll)
            {
                Logger.LogWarning("Plist file already exists: {0}", plistFilePath);
                if (!Confirm.ShowConfirm("Overwrite?"))
                {
                    Logger.LogInfo("Operation cancelled");
                    return;
                }
            }
            else
            {
                Logger.LogWarning("Plist file will be overwritten: {0}", plistFilePath);
            }
        }

        var sb = new StringBuilder();

        // XML Declaration and DOCTYPE
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("<dict>");

        // Basic App Information
        sb.AppendLine("    <!-- Basic App Information -->");
        sb.AppendLine("    <key>CFBundleName</key>");
        sb.AppendLine("    <string>${APP_BASE_NAME}</string>");
        sb.AppendLine("    <key>CFBundleDisplayName</key>");
        sb.AppendLine("    <string>${APP_FRIENDLY_NAME}</string>");
        sb.AppendLine("    <key>CFBundleIdentifier</key>");
        sb.AppendLine("    <string>${APP_ID}</string>");
        sb.AppendLine("    <key>CFBundleVersion</key>");
        sb.AppendLine("    <string>${APP_VERSION}</string>");
        sb.AppendLine("    <key>CFBundleShortVersionString</key>");
        sb.AppendLine("    <string>${APP_VERSION}</string>");
        sb.AppendLine("    <key>CFBundleDevelopmentRegion</key>");
        sb.AppendLine("    <string>English</string>");
        sb.AppendLine("    <key>CFBundleInfoDictionaryVersion</key>");
        sb.AppendLine("    <string>6.0</string>");
        sb.AppendLine("    <key>LSMinimumSystemVersion</key>");
        sb.AppendLine("    <string>10.13</string>");
        sb.AppendLine();

        // Bundle Configuration
        sb.AppendLine("    <!-- Bundle Configuration -->");
        sb.AppendLine("    <key>CFBundlePackageType</key>");
        sb.AppendLine("    <string>APPL</string>");
        sb.AppendLine("    <key>CFBundleExecutable</key>");
        sb.AppendLine("    <string>${APP_EXEC_NAME}</string>");
        sb.AppendLine();

        // App Icon
        sb.AppendLine("    <!-- App Icon -->");
        sb.AppendLine("    <key>CFBundleIconFile</key>");
        sb.AppendLine("    <string>${PRIMARY_ICON_FILE_NAME}</string>");
        sb.AppendLine();

        // High Resolution Support
        sb.AppendLine("    <!-- High Resolution Support -->");
        sb.AppendLine("    <key>NSHighResolutionCapable</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine();

        // Application Principal Class
        sb.AppendLine("    <!-- Principal Class -->");
        sb.AppendLine("    <key>NSPrincipalClass</key>");
        sb.AppendLine("    <string>NSApplication</string>");
        sb.AppendLine();

        // Carbon
        sb.AppendLine("    <!-- Carbon (legacy support) -->");
        sb.AppendLine("    <key>LSRequiresCarbon</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine();

        // Resource File Map
        sb.AppendLine("    <!-- Resource File Map -->");
        sb.AppendLine("    <key>CSResourcesFileMapped</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine();

        // Copyright
        sb.AppendLine("    <!-- Copyright -->");
        sb.AppendLine("    <key>NSHumanReadableCopyright</key>");
        sb.AppendLine("    <string>${PUBLISHER_COPYRIGHT}</string>");
        sb.AppendLine();

        // Category
        sb.AppendLine("    <!-- Category (macOS category id or your own string) -->");
        sb.AppendLine("    <key>LSApplicationCategoryType</key>");
        sb.AppendLine("    <string>${PRIME_CATEGORY}</string>");
        sb.AppendLine();

        // UI Mode
        sb.AppendLine("    <!-- UI Mode (false = normal app, true = menu bar app) -->");
        sb.AppendLine("    <key>LSUIElement</key>");
        sb.AppendLine("    <false/>");
        sb.AppendLine();

        // Security and Permissions
        sb.AppendLine("    <!-- Security and Permissions -->");
        sb.AppendLine("    <key>NSAppleEventsUsageDescription</key>");
        sb.AppendLine("    <string>This app needs AppleEvents permissions for system integration</string>");
        sb.AppendLine();

        // File System Access (optional, for sandboxed apps)
        sb.AppendLine("    <!-- File System Access (used mostly for App Store / sandboxed apps) -->");
        sb.AppendLine("    <!-- Uncomment and adjust these if you plan to sandbox your app -->");
        sb.AppendLine("    <!-- <key>NSDocumentsFolderUsageDescription</key> -->");
        sb.AppendLine("    <!-- <string>This app needs access to your Documents folder</string> -->");
        sb.AppendLine("    <!-- <key>NSDownloadsFolderUsageDescription</key> -->");
        sb.AppendLine("    <!-- <string>This app needs access to your Downloads folder</string> -->");
        sb.AppendLine("    <!-- <key>NSDesktopFolderUsageDescription</key> -->");
        sb.AppendLine("    <!-- <string>This app needs access to your Desktop folder</string> -->");
        sb.AppendLine("    <!-- <key>NSFileProviderDomainUsageDescription</key> -->");
        sb.AppendLine("    <!-- <string>This app needs to access files</string> -->");
        sb.AppendLine();

        // File Associations (commented by default)
        sb.AppendLine("    <!-- File Associations -->");
        sb.AppendLine("    <!-- Uncomment this section if you want your app to handle specific file types -->");
        sb.AppendLine("    <!-- Replace .ext with your file extension and adjust the settings as needed -->");
        sb.AppendLine("    <!-- <key>CFBundleDocumentTypes</key> -->");
        sb.AppendLine("    <!-- <array> -->");
        sb.AppendLine("    <!--     <dict> -->");
        sb.AppendLine("    <!--         <key>CFBundleTypeExtensions</key> -->");
        sb.AppendLine("    <!--         <array> -->");
        sb.AppendLine("    <!--             <string>ext</string> -->");
        sb.AppendLine("    <!--         </array> -->");
        sb.AppendLine("    <!--         <key>CFBundleTypeName</key> -->");
        sb.AppendLine("    <!--         <string>${APP_FRIENDLY_NAME} File</string> -->");
        sb.AppendLine("    <!--         <key>CFBundleTypeIconFile</key> -->");
        sb.AppendLine("    <!--         <string>${PRIMARY_ICON_FILE_NAME}</string> -->");
        sb.AppendLine("    <!--         <key>CFBundleTypeRole</key> -->");
        sb.AppendLine("    <!--         <string>Editor</string> -->");
        sb.AppendLine("    <!--         <key>LSHandlerRank</key> -->");
        sb.AppendLine("    <!--         <string>Owner</string> -->");
        sb.AppendLine("    <!--         <key>LSTypeIsPackage</key> -->");
        sb.AppendLine("    <!--         <false/> -->");
        sb.AppendLine("    <!--     </dict> -->");
        sb.AppendLine("    <!-- </array> -->");
        sb.AppendLine();

        // Placeholders for user customization
        sb.AppendLine("    <!--");
        sb.AppendLine("        Add your own keys here if needed, for example:");
        sb.AppendLine("        - NSCameraUsageDescription");
        sb.AppendLine("        - NSMicrophoneUsageDescription");
        sb.AppendLine("        - NSLocationWhenInUseUsageDescription");
        sb.AppendLine("        - NSContactsUsageDescription");
        sb.AppendLine("        - NSCalendarsUsageDescription");
        sb.AppendLine("        - NSPhotoLibraryUsageDescription");
        sb.AppendLine("        See: https://developer.apple.com/documentation/bundleresources/information_property_list");
        sb.AppendLine("    -->");

        // Closing tags
        sb.AppendLine("</dict>");
        sb.AppendLine("</plist>");

        await File.WriteAllTextAsync(plistFilePath, sb.ToString());
        Logger.LogSuccess("macOS Info.plist file created: {0}", plistFilePath);
    }

    private async Task GenerateEntitlementsFileAsync(string directoryPath, string fileName)
    {
        var entitlementsPath = Path.Combine(directoryPath, $"{fileName}.entitlements");

        if (File.Exists(entitlementsPath))
        {
            if (!_arguments.SkipAll)
            {
                Logger.LogWarning("Entitlements file already exists: {0}", entitlementsPath);
                if (!Confirm.ShowConfirm("Overwrite?"))
                {
                    Logger.LogInfo("Operation cancelled");
                    return;
                }
            }
            else
            {
                Logger.LogWarning("Entitlements file will be overwritten: {0}", entitlementsPath);
            }
        }

        var sb = new StringBuilder();

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("<dict>");

        // Network access
        sb.AppendLine("    <!-- Network access -->");
        sb.AppendLine("    <key>com.apple.security.network.client</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine("    <key>com.apple.security.network.server</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine();

        // File system access
        sb.AppendLine("    <!-- File system access (user-selected read/write) -->");
        sb.AppendLine("    <key>com.apple.security.files.user-selected.read-write</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine();

        // JIT compilation
        sb.AppendLine("    <!-- Allow JIT compilation (.NET runtime requirement) -->");
        sb.AppendLine("    <key>com.apple.security.cs.allow-jit</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine();

        // Add Apple Events to fix an error that shows up in Console.app
        sb.AppendLine("    <!-- Add Apple Events to fix an error that shows up in Console.app -->");
        sb.AppendLine("    <key>com.apple.security.automation.apple-events</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine();

        // Additional entitlements (commented by default)
        sb.AppendLine("    <!--");
        sb.AppendLine("        Additional entitlements that are often useful for .NET apps");
        sb.AppendLine("        (uncomment only if you understand the security implications)");
        sb.AppendLine("    -->");
        sb.AppendLine("    <!-- Disable App Sandbox for full access (not recommended for App Store) -->");
        sb.AppendLine("    <!-- <key>com.apple.security.app-sandbox</key> -->");
        sb.AppendLine("    <!-- <false/> -->");
        sb.AppendLine();
        sb.AppendLine("    <!-- Allow unsigned executable memory (.NET runtime requirement) -->");
        sb.AppendLine("    <!-- <key>com.apple.security.cs.allow-unsigned-executable-memory</key> -->");
        sb.AppendLine("    <!-- <true/> -->");
        sb.AppendLine();
        sb.AppendLine("    <!-- Allow DYLD environment variables (.NET runtime requirement) -->");
        sb.AppendLine("    <!-- <key>com.apple.security.cs.allow-dyld-environment-variables</key> -->");
        sb.AppendLine("    <!-- <true/> -->");
        sb.AppendLine();
        sb.AppendLine("    <!-- Disable library validation (.NET runtime requirement) -->");
        sb.AppendLine("    <!-- <key>com.apple.security.cs.disable-library-validation</key> -->");
        sb.AppendLine("    <!-- <true/> -->");
        sb.AppendLine();
        sb.AppendLine("    <!-- Allow incoming network connections -->");
        sb.AppendLine("    <!-- <key>com.apple.security.network.incoming-connections</key> -->");
        sb.AppendLine("    <!-- <true/> -->");

        sb.AppendLine("</dict>");
        sb.AppendLine("</plist>");

        await File.WriteAllTextAsync(entitlementsPath, sb.ToString());
        Logger.LogSuccess("macOS entitlements file created: {0}", entitlementsPath);
    }

    #endregion File Generator
}