using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;
using System.Text;
using Netloy.ConsoleApp.Helpers;

namespace Netloy.ConsoleApp.Configuration;

public class ConfigurationParser
{
    #region Private Fields

    private readonly Arguments _arguments;
    private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Public Methods

    #region Constructor

    public ConfigurationParser(Arguments arguments)
    {
        _arguments = arguments;
    }

    #endregion

    public async Task<Configurations> ParseAsync()
    {
        ResolveConfigPath(_arguments.ConfigPath);

        if (!File.Exists(Constants.ConfigFilePath))
            throw new FileNotFoundException($"Netloy configuration file not found: {Constants.ConfigFilePath}");

        Logger.LogInfo("Loading configuration from: {0}", Constants.ConfigFilePath);

        await ParseFileAsync(Constants.ConfigFilePath);

        var config = MapToConfiguration();

        await ValidateConfigurationAsync(config);

        Logger.LogSuccess("Configuration loaded successfully");

        return config;
    }

    public string GetDefaultConfigContent()
    {
        var defaultConfig = GetDefaultConfiguration();
        return GenerateConfigurationFile(defaultConfig, includeUpgradeHeader: false, includeComments: _arguments.Verbose);
    }

    public async Task CreateDefaultConfigFileAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            Logger.LogWarning("Configuration file already exists: {0}", filePath);
            if (!Confirm.ShowConfirm("Overwrite?"))
            {
                Logger.LogInfo("Operation cancelled");
                return;
            }
        }

        var content = GetDefaultConfigContent();

        await File.WriteAllTextAsync(filePath, content);

        Logger.LogSuccess("Configuration file created: {0}", filePath);
        Logger.LogInfo("Edit this file and update the values for your application");
    }

    public async Task UpgradeConfigurationAsync(Arguments arguments)
    {
        ResolveConfigPath(arguments.ConfigPath);

        Logger.LogInfo("Checking configuration file: {0}", Constants.ConfigFilePath);

        await ParseFileAsync(Constants.ConfigFilePath);

        var currentVersion = GetValue("ConfigVersion");
        var latestVersion = Constants.Version;

        if (!currentVersion.IsStringNullOrEmpty() && currentVersion.Equals(latestVersion))
        {
            Logger.LogSuccess("Configuration file is already up-to-date (version {0})", latestVersion);
            return;
        }

        if (currentVersion.IsStringNullOrEmpty())
        {
            Logger.LogWarning("Configuration file does not have a version. This might be an old format.");
        }
        else
        {
            Logger.LogInfo("Current version: {0}", currentVersion);
            Logger.LogInfo("Latest version: {0}", latestVersion);
        }

        if (!arguments.SkipAll && !Confirm.ShowConfirm("Upgrade configuration file?"))
        {
            Logger.LogInfo("Operation cancelled");
            return;
        }

        if (!arguments.Verbose)
            Logger.LogInfo("Configuration File will NOT have document comments (use with --verbose to include comments)");

        Logger.LogInfo("Upgrading configuration file...");

        var backupPath = $"{Constants.ConfigFilePath}.backup.{DateTime.Now:yyyyMMdd_HHmmss}";
        File.Copy(Constants.ConfigFilePath, backupPath);
        Logger.LogSuccess("Backup created: {0}", backupPath);

        var existingConfig = MapToConfiguration();
        var newContent = GenerateConfigurationFile(existingConfig, includeUpgradeHeader: true, includeComments: arguments.Verbose);

        await File.WriteAllTextAsync(Constants.ConfigFilePath, newContent);

        Logger.LogSuccess("Configuration file upgraded to version {0}", latestVersion);
        Logger.LogInfo("Original file backed up to: {0}", backupPath);
    }

    #endregion

    #region Private Methods - Parsing

    private static void ResolveConfigPath(string? configPath)
    {
        // If config path is explicitly provided, use it
        if (!configPath.IsStringNullOrEmpty())
        {
            var fullPath = Path.GetFullPath(configPath!);
            if (!File.Exists(fullPath))
            {
                fullPath = Path.Combine(Directory.GetCurrentDirectory(), configPath!);
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"Configuration file not found at specified path: {configPath}");
            }

            Constants.ConfigFilePath = fullPath;
            return;
        }

        // Search for .netloy files in current directory
        var currentDir = Directory.GetCurrentDirectory();
        Logger.LogDebug("Searching for .netloy configuration files in: {0}", currentDir);

        var netloyFiles = Directory.GetFiles(currentDir, $"*.{Constants.NetloyConfigFileExt}", SearchOption.TopDirectoryOnly);

        switch (netloyFiles.Length)
        {
            case 0:
            {
                throw new FileNotFoundException(
                    $"No .netloy configuration file found in current directory: {currentDir}\n" +
                    "Please create one using 'netloy --new conf' or specify path with --config-path");
            }

            case > 1:
            {
                Logger.LogWarning("Multiple .netloy files found in directory. Using: {0}", Path.GetFileName(netloyFiles[0]));
                Logger.LogInfo("To use a specific file, specify it with --config-path");
                break;
            }
        }

        var selectedFile = netloyFiles[0];
        Logger.LogDebug("Found configuration file: {0}", Path.GetFileName(selectedFile));

        Constants.ConfigFilePath = selectedFile;
    }

    private async Task ParseFileAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        var multiLineValue = new StringBuilder();
        string? currentKey = null;
        var inMultiLineString = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.IsStringNullOrEmpty() || line.StartsWith('#'))
                continue;

            // Handle multi-line strings (""" ... """)
            if (inMultiLineString)
            {
                if (line.Contains("\"\"\""))
                {
                    // End of multi-line string
                    var endIndex = line.IndexOf("\"\"\"", StringComparison.Ordinal);
                    multiLineValue.AppendLine(line.Substring(0, endIndex));
                    _settings[currentKey!] = multiLineValue.ToString().Trim();

                    multiLineValue.Clear();
                    currentKey = null;
                    inMultiLineString = false;
                }
                else
                {
                    multiLineValue.AppendLine(line);
                }

                continue;
            }

            // Check for key = value
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex == -1)
            {
                Logger.LogWarning("Invalid line {0}: No '=' separator found. Skipping.", i + 1);
                continue;
            }

            var key = line.Substring(0, separatorIndex).Trim();
            var value = line.Substring(separatorIndex + 1).Trim();

            // Handle multi-line string start
            if (value.StartsWith("\"\"\""))
            {
                if (value.EndsWith("\"\"\"") && value.Length > 6)
                {
                    // Single line with """value"""
                    value = value.Substring(3, value.Length - 6).Trim();
                    _settings[key] = value;
                }
                else
                {
                    // Start of multi-line string
                    inMultiLineString = true;
                    currentKey = key;

                    // Get content after opening """
                    var content = value.Substring(3).Trim();
                    if (!content.IsStringNullOrEmpty())
                        multiLineValue.AppendLine(content);
                }
            }
            else
            {
                _settings[key] = value;
            }
        }

        Logger.LogDebug("Parsed {0} configuration entries", _settings.Count);
    }

    private Configurations MapToConfiguration()
    {
        var config = new Configurations();

        // APP PREAMBLE
        config.AppBaseName = GetValue(nameof(config.AppBaseName));
        config.AppFriendlyName = GetValue(nameof(config.AppFriendlyName));
        config.AppId = GetValue(nameof(config.AppId));
        config.AppVersionRelease = GetValue(nameof(config.AppVersionRelease));
        config.AppShortSummary = GetValue(nameof(config.AppShortSummary));
        config.AppDescription = GetValue(nameof(config.AppDescription));
        config.AppLicenseId = GetValue(nameof(config.AppLicenseId));
        config.AppLicenseFile = GetValue(nameof(config.AppLicenseFile));
        config.AppChangeFile = GetValue(nameof(config.AppChangeFile));

        // PUBLISHER
        config.PublisherName = GetValue(nameof(config.PublisherName));
        config.PublisherId = GetValue(nameof(config.PublisherId));
        config.PublisherCopyright = GetValue(nameof(config.PublisherCopyright));
        config.PublisherLinkName = GetValue(nameof(config.PublisherLinkName));
        config.PublisherLinkUrl = GetValue(nameof(config.PublisherLinkUrl));
        config.PublisherEmail = GetValue(nameof(config.PublisherEmail));

        // DESKTOP INTEGRATION
        config.DesktopNoDisplay = GetBoolValue(nameof(config.DesktopNoDisplay));
        config.DesktopTerminal = GetBoolValue(nameof(config.DesktopTerminal));
        config.DesktopFile = GetValue(nameof(config.DesktopFile));
        config.StartCommand = GetValue(nameof(config.StartCommand));
        config.PrimeCategory = GetValue(nameof(config.PrimeCategory));
        config.MetaFile = GetValue(nameof(config.MetaFile));
        config.IconFiles = GetValue(nameof(config.IconFiles));
        config.AutoGenerateIcons = GetBoolValue(nameof(config.AutoGenerateIcons));

        // DOTNET PUBLISH
        config.DotnetProjectPath = GetValue(nameof(config.DotnetProjectPath));
        config.DotnetPublishArgs = GetValue(nameof(config.DotnetPublishArgs));
        config.DotnetPostPublish = GetValue(nameof(config.DotnetPostPublish));
        config.DotnetPostPublishOnWindows = GetValue(nameof(config.DotnetPostPublishOnWindows));
        config.DotnetPostPublishArguments = GetValue(nameof(config.DotnetPostPublishArguments));

        // PACKAGE OUTPUT
        config.PackageName = GetValue(nameof(config.PackageName));
        config.OutputDirectory = GetValue(nameof(config.OutputDirectory));

        // APPIMAGE OPTIONS
        config.AppImageArgs = GetValue(nameof(config.AppImageArgs));

        // FLATPAK OPTIONS
        config.FlatpakPlatformRuntime = GetValue(nameof(config.FlatpakPlatformRuntime));
        config.FlatpakPlatformSdk = GetValue(nameof(config.FlatpakPlatformSdk));
        config.FlatpakPlatformVersion = GetValue(nameof(config.FlatpakPlatformVersion));
        config.FlatpakFinishArgs = GetValue(nameof(config.FlatpakFinishArgs));
        config.FlatpakBuilderArgs = GetValue(nameof(config.FlatpakBuilderArgs));

        // RPM OPTIONS
        config.RpmAutoReq = GetBoolValue(nameof(config.RpmAutoReq));
        config.RpmAutoProv = GetBoolValue(nameof(config.RpmAutoProv));
        config.RpmRequires = GetValue(nameof(config.RpmRequires));

        // DEBIAN OPTIONS
        config.DebianRecommends = GetValue(nameof(config.DebianRecommends));

        // WINDOWS SETUP OPTIONS
        config.SetupGroupName = GetValue(nameof(config.SetupGroupName));
        config.SetupAdminInstall = GetBoolValue(nameof(config.SetupAdminInstall));
        config.SetupCommandPrompt = GetValue(nameof(config.SetupCommandPrompt));
        config.SetupMinWindowsVersion = GetValue(nameof(config.SetupMinWindowsVersion));
        config.SetupSignTool = GetValue(nameof(config.SetupSignTool));
        config.SetupUninstallScript = GetValue(nameof(config.SetupUninstallScript));

        // CONFIGURATION OPTIONS
        config.ConfigVersion = GetValue(nameof(config.ConfigVersion));

        return config;
    }

    private string GetValue(string key)
    {
        return _settings.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private bool GetBoolValue(string key)
    {
        return _settings.TryGetValue(key, out var value) && value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ValidateConfigurationAsync(Configurations config)
    {
        // TODO: Complete this method
        var errors = new List<string>();

        if (config.AppBaseName.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.AppBaseName)} is required");

        if (config.AppFriendlyName.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.AppFriendlyName)} is required");

        if (config.AppId.IsStringNullOrEmpty() || !config.AppId.CheckDnsPattern())
        {
            var message = config.AppId.IsStringNullOrEmpty()
                ? $"{nameof(config.AppId)} is required"
                : $"{nameof(config.AppId)} '{config.AppId}' doesn't follow reverse domain notation (e.g., com.example.app)";

            errors.Add(message);
        }

        if (config.AppVersionRelease.IsStringNullOrEmpty() || !config.AppVersionRelease.CheckLongVersion())
        {
            var message = config.AppVersionRelease.IsStringNullOrEmpty()
                ? $"{nameof(config.AppVersionRelease)} is required"
                : $"Invalid {nameof(config.AppVersionRelease)} format: '{config.AppVersionRelease}'. Expected format: 1.0.0 or 1.0.0[1]";

            errors.Add(message);
        }

        if (config.AppShortSummary.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.AppShortSummary)} is required");

        if (config.AppDescription.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.AppDescription)} is required");

        if (config.AppLicenseId.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.AppLicenseId)} is required");

        ValidatePath(config, config.AppLicenseFile, nameof(config.AppLicenseFile), errors, isDirectory: false, required: false);
        ValidatePath(config, config.AppChangeFile, nameof(config.AppChangeFile), errors, isDirectory: false, required: false);

        if (config.PublisherName.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.PublisherName)} is required");

        if (!config.PublisherId.IsStringNullOrEmpty() && !config.PublisherId.CheckDnsPattern())
            errors.Add($"{nameof(config.PublisherId)} '{config.PublisherId}' doesn't follow reverse domain notation (e.g., com.example.app)");

        if (config.PublisherCopyright.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.PublisherCopyright)} is required");

        if (!config.PublisherLinkUrl.IsStringNullOrEmpty())
        {
            if (config.PublisherLinkName.IsStringNullOrEmpty())
                errors.Add($"{nameof(config.PublisherLinkName)} is required");

            if (!config.PublisherLinkUrl.CheckUrlValidation())
                errors.Add($"{nameof(config.PublisherLinkUrl)} '{config.PublisherLinkUrl}' is not a valid URL");
        }

        if (!config.PublisherEmail.CheckEmailValidation())
            Logger.LogWarning("Invalid {0} '{1}'", nameof(config.PublisherEmail), config.PublisherEmail);

        await ValidateIconsAsync(config, errors);

        ValidateDotnetProjectPath(config);

        ValidatePath(config, config.DotnetPostPublish, nameof(config.DotnetPostPublish), errors, isDirectory: false, required: false);
        ValidatePath(config, config.DotnetPostPublishOnWindows, nameof(config.DotnetPostPublishOnWindows), errors, isDirectory: false, required: false);

        if (config.PackageName.IsStringNullOrEmpty() && _arguments.OutputPath.IsStringNullOrEmpty())
            errors.Add($"{nameof(config.PackageName)} is required");

        ValidatePath(config, config.OutputDirectory, nameof(config.OutputDirectory), errors, isDirectory: true, required: true);

        if (!double.TryParse(config.SetupMinWindowsVersion, out _))
        {
            if (!_arguments.SkipAll)
            {
                if (!Confirm.ShowConfirm($"{nameof(config.SetupMinWindowsVersion)} is not a valid version number. Do you want to continue with 10?"))
                    throw new OperationCanceledException("Operation canceled by user.");
            }
            else
            {
                Logger.LogWarning("{0} is not a valid version number. Setting it to 10...", nameof(config.SetupMinWindowsVersion));
            }

            config.SetupMinWindowsVersion = "10";
        }

        ValidatePath(config, config.SetupUninstallScript, nameof(config.SetupUninstallScript), errors, isDirectory: false, required: false);

        if (errors.Count == 0)
            return;

        var errorMessage = "Configuration validation failed:\n  - " + string.Join("\n  - ", errors);
        throw new InvalidOperationException(errorMessage);
    }

    private static async Task ValidateIconsAsync(Configurations config, List<string> errors)
    {
        if (config.IconFiles.IsStringNullOrEmpty())
        {
            errors.Add("IconFiles is required");
            return;
        }

        var iconFiles = config.IconFiles.Split('\n')
            .Where(ico => !ico.IsStringNullOrEmpty())
            .Select(ico => ico.Trim().NormalizePath())
            .ToList();

        if (iconFiles.Count <= 0)
        {
            errors.Add("No icon files specified");
            return;
        }

        foreach (var iconFile in iconFiles)
        {
            var iconPath = iconFile;
            if (!iconPath.IsAbsolutePath())
                iconPath = Path.Combine(Constants.ConfigFileDirectory, iconPath);

            if (!File.Exists(iconPath))
            {
                errors.Add($"Couldn't find icon. Icon path: {iconPath}");
                continue;
            }

            var ext = Path.GetExtension(iconPath).ToLowerInvariant();
            if (!ext.Equals(".svg") && !ext.Equals(".ico") && !ext.Equals(".icns") && !ext.Equals(".png"))
                errors.Add("Only SVG, ICO, ICNS and PNG icon formats are supported. File path: " + iconPath);

            var sections = Path.GetFileNameWithoutExtension(iconPath).Split(".");
            if (ext.Equals("png"))
            {
                if (sections.Length != 3)
                    errors.Add("PNG icon file name should be in the format: <name>.<width>x<height>.png. File path: " + iconPath);

                var sizeSection = sections[1].Split("x");
                if (sizeSection.Length != 2)
                    errors.Add("PNG icon file name should be in the format: <name>.<width>x<height>.png. File path: " + iconPath);

                var isWidthValid = int.TryParse(sizeSection[0], out var width);
                var isHeightValid = int.TryParse(sizeSection[1], out var height);
                if (!isWidthValid || !isHeightValid)
                    errors.Add("PNG icon file name should be in the format: <name>.<width>x<height>.png. File path: " + iconPath);

                if (width != height)
                    errors.Add("PNG icon size should be square. Correct format: <name>.<size>x<size>.png. File path: " + iconPath);

                var actualSize = await IconHelper.GetImageSizeAsync(iconPath);
                if (actualSize.Width != actualSize.Height)
                {
                    var message = $"PNG icon file '{iconPath}' is not square. It's recommended to use square icons for better visual results. " +
                                  $"Current size: {actualSize.Width}x{actualSize.Height}";

                    Logger.LogWarning(message);
                }
            }

            config.IconsCollection.Add(iconPath);
        }

        if (config.IconsCollection.Count > 0)
            return;

        errors.Add("No valid icon files specified");
    }

    private void ValidateDotnetProjectPath(Configurations config)
    {
        var projectPath = config.DotnetProjectPath.NormalizePath();
        if (!projectPath.IsAbsolutePath())
            projectPath = Path.Combine(Constants.ConfigFileDirectory, projectPath);

        projectPath = Path.GetFullPath(projectPath);
        if (projectPath.IsStringNullOrEmpty())
        {
            if (_arguments.ProjectPath.IsStringNullOrEmpty())
                throw new InvalidOperationException("Project path is not defined. You should specify it either in the configuration file or as a command line argument");

            return;
        }

        if (File.Exists(projectPath))
        {
            config.DotnetProjectPath = projectPath;
        }
        else if (Directory.Exists(projectPath))
        {
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
            switch (csprojFiles.Length)
            {
                case 0:
                    throw new FileNotFoundException($"No project file found in the specified directory. Directory path: {projectPath}");

                case 1:
                {
                    projectPath = csprojFiles[0];
                    break;
                }

                case > 1:
                {
                    Logger.LogWarning("Multiple project files found in the specified directory. Directory path: {0}", projectPath);

                    if (!_arguments.SkipAll)
                    {
                        if (!Confirm.ShowConfirm("Multiple project files found. Do you want to use the first project file found?"))
                            throw new OperationCanceledException("Operation canceled by user.");
                    }
                    else
                    {
                        Logger.LogInfo("Using first project file found. Project path: {0}", csprojFiles[0]);
                    }

                    projectPath = csprojFiles[0];
                    break;
                }
            }

            config.DotnetProjectPath = projectPath;
        }
        else
        {
            if (!_arguments.ProjectPath.IsStringNullOrEmpty())
                return;

            var message = $"Project file not found. File path: {projectPath}\n" +
                          "You should specify the path to the .NET project file in the configuration file or as a command line argument";

            throw new FileNotFoundException(message);
        }
    }

    private void ValidatePath(Configurations config, string value, string name, List<string> errors, bool isDirectory, bool required)
    {
        if (value.IsStringNullOrEmpty())
        {
            if (required)
                errors.Add($"{name} is required");

            return;
        }

        value = value.NormalizePath();
        if (!value.IsAbsolutePath())
            value = Path.Combine(Constants.ConfigFileDirectory, value);

        switch (isDirectory)
        {
            case false when !File.Exists(value):
            {
                errors.Add($"{name} file not found: {value}");
                return;
            }

            case true when !Directory.Exists(value):
            {
                Logger.LogWarning("Directory not found: {0}", value);

                if (!_arguments.SkipAll)
                {
                    if (!Confirm.ShowConfirm($"Directory not found: {value}. Do you want to create it?"))
                        throw new OperationCanceledException("Operation canceled by user.");
                }
                else
                {
                    Logger.LogInfo("Creating directory: {0}", value);
                }

                Directory.CreateDirectory(value);
                break;
            }
        }

        var property = config.GetType().GetProperty(name);
        if (property?.CanWrite == true)
            property.SetValue(config, value);
    }

    #endregion

    #region Private Methods - Generation

    private static string GenerateConfigurationFile(Configurations config, bool includeUpgradeHeader, bool includeComments)
    {
        var sb = new StringBuilder(4096);

        // Header
        if (includeComments)
        {
            sb.AppendLine("################################################################################");
            sb.AppendLine($"# Netloy {Constants.Version} - .NET Application Packaging Tool");
        }

        if (includeUpgradeHeader)
            sb.AppendLine($"# Configuration upgraded on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        if (includeComments)
            sb.AppendLine("################################################################################");

        sb.AppendLine();

        // APP PREAMBLE
        AppendAppPreambleSection(sb, config, includeComments);

        // PUBLISHER
        AppendPublisherSection(sb, config, includeComments);

        // DESKTOP INTEGRATION
        AppendDesktopIntegrationSection(sb, config, includeComments);

        // DOTNET PUBLISH
        AppendDotnetPublishSection(sb, config, includeComments);

        // PACKAGE OUTPUT
        AppendPackageOutputSection(sb, config, includeComments);

        // APPIMAGE OPTIONS
        AppendAppImageSection(sb, config, includeComments);

        // FLATPAK OPTIONS
        AppendFlatpakSection(sb, config, includeComments);

        // RPM OPTIONS
        AppendRpmSection(sb, config, includeComments);

        // DEBIAN OPTIONS
        AppendDebianSection(sb, config, includeComments);

        // WINDOWS SETUP OPTIONS
        AppendWindowsSetupSection(sb, config, includeComments);

        // CONFIGURATION OPTIONS
        AppendConfigurationOptionsSection(sb, includeComments);

        return sb.ToString().Trim();
    }

    private static void AppendAppPreambleSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "APP PREAMBLE");

            AppendComment(sb, "Mandatory application base name. This MUST BE the base name of the main executable file. It should NOT");
            AppendComment(sb, "include any directory part or extension, i.e. do not append '.exe' or '.dll'. It should not contain");
            AppendComment(sb, "spaces or invalid filename characters.");
        }

        AppendKeyValue(sb, nameof(config.AppBaseName), config.AppBaseName);
        sb.AppendLine();

        if (includeComments)
            AppendComment(sb, "Mandatory application friendly name.");

        AppendKeyValue(sb, nameof(config.AppFriendlyName), config.AppFriendlyName);
        sb.AppendLine();

        if (includeComments)
            AppendComment(sb, "Mandatory application ID in reverse DNS form. This should stay constant for lifetime of the software.");

        AppendKeyValue(sb, nameof(config.AppId), config.AppId);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Mandatory application version and package release of form: 'VERSION[RELEASE]'. Use optional square");
            AppendComment(sb, "brackets to denote package release, i.e. '1.2.3[1]'. Release refers to a change to the deployment");
            AppendComment(sb, "package, rather the application. If release part is absent (i.e. '1.2.3'), the release value defaults");
            AppendComment(sb, "to '1'. Note that the version-release value given here may be overridden from the command line.");
        }

        AppendKeyValue(sb, nameof(config.AppVersionRelease), config.AppVersionRelease);
        sb.AppendLine();

        if (includeComments)
            AppendComment(sb, "Mandatory single line application summary text in default (English) language.");

        AppendKeyValue(sb, nameof(config.AppShortSummary), config.AppShortSummary);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Multi-line (surround with triple \"\"\" quotes) application description which provides longer explanation");
            AppendComment(sb, "than AppShortSummary in default language. Optional but it is recommended to specify this. Text");
            AppendComment(sb, "separated by an empty line will be treated as separate paragraphs. Avoid complex formatting, and do not");
            AppendComment(sb, "use HTML or markdown, other than list items begining with \"* \", \"+ \" or \"- \". This content is");
            AppendComment(sb, "used by package builders where supported, including RPM and DEB, and is used to populate the");
            AppendComment(sb, "${APPSTREAM_DESCRIPTION_XML} element used within AppStream metadata.");
        }

        AppendMultiLineValue(sb, nameof(config.AppDescription), config.AppDescription);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Mandatory application license ID. This should be one of the recognized SPDX license");
            AppendComment(sb, "identifiers, such as: 'MIT', 'GPL-3.0-or-later' or 'Apache-2.0'. For a proprietary or");
            AppendComment(sb, "custom license, use 'LicenseRef-Proprietary' or 'LicenseRef-LICENSE'.");
        }

        AppendKeyValue(sb, nameof(config.AppLicenseId), config.AppLicenseId);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional path to application copyright/license text file. If provided, it will be packaged with the");
            AppendComment(sb, "application and used with package builders where supported.");
        }

        AppendKeyValue(sb, nameof(config.AppLicenseFile), config.AppLicenseFile);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional path to application changelog file. IMPORTANT. If given, this file should contain version");
            AppendComment(sb, "information in a predefined format. Namely, it should contain one or more version headings of form:");
            AppendComment(sb, "'+ VERSION;DATE', under which are to be listed change items of form: '- Change description'. Formatted");
            AppendComment(sb, "information will be parsed and used to expand the ${APPSTREAM_CHANGELOG_XML} macro used");
            AppendComment(sb, "for AppStream metadata (superfluous text is ignored, so the file may also contain README information).");
            AppendComment(sb, "The given file will also be packaged with the application verbatim.");
        }

        AppendKeyValue(sb, nameof(config.AppChangeFile), config.AppChangeFile);
        sb.AppendLine();
    }

    private static void AppendPublisherSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "PUBLISHER");
            AppendComment(sb, "Mandatory publisher, group or creator.");
        }

        AppendKeyValue(sb, nameof(config.PublisherName), config.PublisherName);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, $"Publisher ID in reverse DNS form. Invariably, this would be the same as {nameof(config.AppId)}, excluding the app leaf");
            AppendComment(sb, "name. The value populates the ${PUBLISHER_ID} macro used AppStream metainfo. If omitted, defaults to");
            AppendComment(sb, $"the leading parts of {nameof(config.AppId)}. It is highly recommended to specify the value explicitly.");
        }

        AppendKeyValue(sb, nameof(config.PublisherId), config.PublisherId);
        sb.AppendLine();

        if (includeComments)
            AppendComment(sb, "Optional copyright statement.");

        AppendKeyValue(sb, nameof(config.PublisherCopyright), config.PublisherCopyright);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional publisher or application web-link name. Note that Windows Setup packages");
            AppendComment(sb, "require both PublisherLinkName and PublisherLinkUrl in order to include the link as");
            AppendComment(sb, "an item in program menu entries. Do not modify name, as may leave old entries in updated installations.");
        }

        AppendKeyValue(sb, nameof(config.PublisherLinkName), config.PublisherLinkName);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Publisher or application web-link URL. Although optional, it should be considered mandatory if using");
            AppendComment(sb, "MetaFile");
        }

        AppendKeyValue(sb, nameof(config.PublisherLinkUrl), config.PublisherLinkUrl);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Publisher or maintainer email contact. Although optional, some package builders (i.e. DEB) require it");
            AppendComment(sb, "and may warn or fail unless provided.");
        }

        AppendKeyValue(sb, nameof(config.PublisherEmail), config.PublisherEmail);
        sb.AppendLine();
    }

    private static void AppendDesktopIntegrationSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "DESKTOP INTEGRATION");

            AppendComment(sb, "Boolean (true or false) which indicates whether the application is hidden on the desktop. It is used to");
            AppendComment(sb, "populate the 'NoDisplay' field of the .desktop file. The default is false. Setting to true will also");
            AppendComment(sb, "cause the main application start menu entry to be omitted for Windows Setup.");
        }

        AppendKeyValue(sb, nameof(config.DesktopNoDisplay), config.DesktopNoDisplay);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Boolean (true or false) which indicates whether the application runs in the terminal, rather than");
            AppendComment(sb, "providing a GUI. It is used to populate the 'Terminal' field of the .desktop file.");
        }

        AppendKeyValue(sb, nameof(config.DesktopTerminal), config.DesktopTerminal);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional path to a Linux desktop file. If empty (default), one will be generated automatically from");
            AppendComment(sb, "the information in this file. Supplying a custom file, however, allows for mime-types and");
            AppendComment(sb, "internationalisation. If supplied, the file MUST contain the line: 'Exec=${INSTALL_EXEC}'");
            AppendComment(sb, "in order to use the correct install location. Other macros may be used to help automate the content.");
            AppendComment(sb, "Note. Netloy can generate you a desktop file. Use --help and 'netloy --help macro' for reference.");
            AppendComment(sb, "See: https://specifications.freedesktop.org/desktop-entry-spec/desktop-entry-spec-latest.html");
        }

        AppendKeyValue(sb, nameof(config.DesktopFile), config.DesktopFile);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional command name to start the application from the terminal. If, for example, AppBaseName is");
            AppendComment(sb, "'Com.Example.HelloWorld', the value here may be set to a simpler and/or lower-case variant such as");
            AppendComment(sb, "'helloworld'. It must not contain spaces or invalid filename characters. Do not add any extension such");
            AppendComment(sb, "as '.exe'. If empty, the application will not be in the path and cannot be started from the command line.");
            AppendComment(sb, "For Windows Setup packages, see also SetupCommandPrompt. StartCommand is not");
            AppendComment(sb, "supported for all packages kinds (i.e. Flatpak). Default is empty (none).");
        }

        AppendKeyValue(sb, nameof(config.StartCommand), config.StartCommand);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional category for the application. The value should be one of the recognized Freedesktop top-level");
            AppendComment(sb, "categories, such as: Audio, Development, Game, Office, Utility etc. Only a single value should be");
            AppendComment(sb, "provided here which will be used, where supported, to populate metadata. The default is empty.");
            AppendComment(sb, "See: https://specifications.freedesktop.org/menu-spec/latest/apa.html");
        }

        AppendKeyValue(sb, nameof(config.PrimeCategory), config.PrimeCategory);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Path to AppStream metadata file. It is optional, but recommended as it is used by software centers.");
            AppendComment(sb, "Note. The contents of the files may use macro variables. Use 'netloy --help macro' for reference.");
            AppendComment(sb, "See: https://docs.appimage.org/packaging-guide/optional/appstream.html");
        }

        AppendKeyValue(sb, nameof(config.MetaFile), config.MetaFile);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional icon file paths. The value may include multiple filenames separated with semicolon or given");
            AppendComment(sb, "in multi-line form. Valid types are SVG, PNG and ICO (ICO ignored on Linux). Note that the inclusion");
            AppendComment(sb, "of a scalable SVG is preferable on Linux, whereas PNGs must be one of the standard sizes and MUST");
            AppendComment(sb, "include the size in the filename in the form: name.32x32.png' or 'name.32.png'.");
            AppendComment(sb, "Example with specific sizes:");
            AppendComment(sb, "IconFiles = \"\"\"");
            AppendComment(sb, "    Deploy/app-logo.16x16.png");
            AppendComment(sb, "    Deploy/app-logo.24x24.png");
            AppendComment(sb, "    Deploy/app-logo.32x32.png");
            AppendComment(sb, "    Deploy/app-logo.48x48.png");
            AppendComment(sb, "    Deploy/app-logo.64x64.png");
            AppendComment(sb, "    Deploy/app-logo.svg");
            AppendComment(sb, "    Deploy/app-logo.ico");
            AppendComment(sb, "    Deploy/app-logo.icns");
            AppendComment(sb, "\"\"\"");
        }

        AppendMultiLineValue(sb, nameof(config.IconFiles), config.IconFiles);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Boolean (true or false) which enables automatic icon generation. When set to true, you only need to");
            AppendComment(sb, "provide a source PNG image (1024x1024 recommended) file in IconFiles, and Netloy will");
            AppendComment(sb, "automatically generate all required PNG icon sizes for different platforms (16x16, 24x24, 32x32,");
            AppendComment(sb, "48x48, 64x64, 96x96, 128x128, 256x256, 512x512). Note: SVG (Linux), ICO (Windows) and ICNS (macOS) formats are NOT");
            AppendComment(sb, "generated automatically and must be provided separately by the user. This feature simplifies PNG icon");
            AppendComment(sb, "management for Linux packages (DEB, RPM, AppImage, Flatpak) while maintaining consistent sizing.");
            AppendComment(sb, "Default is false.");
            AppendComment(sb, "Example usage:");
            AppendComment(sb, "  AutoGenerateIcons = true");
            AppendComment(sb, "  IconFiles = \"\"\"");
            AppendComment(sb, "      Deploy/app-logo.1024x1024.png");
            AppendComment(sb, "      Deploy/app-logo.svg");
            AppendComment(sb, "      Deploy/app-logo.ico");
            AppendComment(sb, "      Deploy/app-logo.icns");
            AppendComment(sb, "  \"\"\"");
        }

        AppendKeyValue(sb, nameof(config.AutoGenerateIcons), config.AutoGenerateIcons);
        sb.AppendLine();
    }

    private static void AppendDotnetPublishSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "DOTNET PUBLISH");

            AppendComment(sb, "Optional path relative to this file in which to find the dotnet project (.csproj) file, or the");
            AppendComment(sb, "directory containing it. If empty (default), a single project file is expected under the same");
            AppendComment(sb, "directory as this file. IMPORTANT. If set to 'NONE', dotnet publish is disabled");
            AppendComment(sb, "(i.e. not called). Instead, only DotnetPostPublish is called.");
        }

        AppendKeyValue(sb, nameof(config.DotnetProjectPath), config.DotnetProjectPath);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional arguments supplied to 'dotnet publish'. Do NOT include '-r' (runtime), or '-c' (configuration)");
            AppendComment(sb, "here as they will be added according to command line arguments. Typically you want as a minimum:");
            AppendComment(sb, "'-p:Version=${APP_VERSION} --self-contained true'. Additional useful arguments include:");
            AppendComment(sb, "'-p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=true -p:PublishReadyToRun=true");
            AppendComment(sb, "-p:PublishTrimmed=true -p:TrimMode=link'. Note. This value may use macro variables. Use 'netloy --help macro'");
            AppendComment(sb, "for reference. See: https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish");
        }

        AppendKeyValue(sb, nameof(config.DotnetPublishArgs), config.DotnetPublishArgs);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Post-publish (or standalone build) command on Linux (ignored on Windows). It is called after dotnet");
            AppendComment(sb, "publish, but before the final output is built. This could, for example, be a script which copies");
            AppendComment(sb, "additional files into the build directory given by ${BUILD_APP_BIN}. The working directory will be");
            AppendComment(sb, "the location of this file. This value is optional, but becomes mandatory if DotnetProjectPath equals");
            AppendComment(sb, "'NONE'. Note. This value may use macro variables. Additionally, scripts may use these as environment");
            AppendComment(sb, "variables. Use 'netloy --help macro' for reference.");
        }

        AppendKeyValue(sb, nameof(config.DotnetPostPublish), config.DotnetPostPublish);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Post-publish (or standalone build) command on Windows (ignored on Linux). This should perform");
            AppendComment(sb, "the equivalent operation, as required, as DotnetPostPublish, but using DOS commands and batch");
            AppendComment(sb, "scripts. Multiple commands may be specified, separated by semicolon or given in multi-line form.");
            AppendComment(sb, "Note. This value may use macro variables. Additionally, scripts may use these as environment");
            AppendComment(sb, "variables. Use 'netloy --help macro' for reference.");
        }

        AppendKeyValue(sb, nameof(config.DotnetPostPublishOnWindows), config.DotnetPostPublishOnWindows);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional arguments supplied to the post-publish command. This value is optional and is only used if");
            AppendComment(sb, "DotnetPostPublish or DotnetPostPublishOnWindows is specified. Note. This value may use macro variables.");
            AppendComment(sb, "Use 'netloy --help macro' for reference.");
        }

        AppendKeyValue(sb, nameof(config.DotnetPostPublishArguments), config.DotnetPostPublishArguments);
        sb.AppendLine();
    }

    private static void AppendPackageOutputSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "PACKAGE OUTPUT");

            AppendComment(sb, "Optional package name (excludes version etc.). If empty, defaults to AppBaseName. However, it is");
            AppendComment(sb, "used not only to specify the base output filename, but to identify the application in DEB and RPM");
            AppendComment(sb, "packages. You may wish, therefore, to ensure that the value represents a unique name. Naming");
            AppendComment(sb, "requirements are strict and must contain only alpha-numeric and '-', '+' and '.' characters.");
        }

        AppendKeyValue(sb, nameof(config.PackageName), config.PackageName);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Output directory, or subdirectory relative to this file. It will be created if it does not exist and");
            AppendComment(sb, "will contain the final deploy output files. If empty, it defaults to the location of this file.");
        }

        AppendKeyValue(sb, nameof(config.OutputDirectory), config.OutputDirectory);
        sb.AppendLine();
    }

    private static void AppendAppImageSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "APPIMAGE OPTIONS");

            AppendComment(sb, "Additional arguments for use with appimagetool. Useful for signing. Default is empty.");
        }

        AppendKeyValue(sb, nameof(config.AppImageArgs), config.AppImageArgs);
        sb.AppendLine();
    }

    private static void AppendFlatpakSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "FLATPAK OPTIONS");

            AppendComment(sb, "The runtime platform. Invariably for .NET (inc. Avalonia), this should be 'org.freedesktop.Platform'.");
            AppendComment(sb, "Refer: https://docs.flatpak.org/en/latest/available-runtimes.html");
        }

        AppendKeyValue(sb, nameof(config.FlatpakPlatformRuntime), config.FlatpakPlatformRuntime);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "The platform SDK. Invariably for .NET (inc. Avalonia applications) this should be 'org.freedesktop.Sdk'.");
            AppendComment(sb, "The SDK must be installed on the build system.");
        }

        AppendKeyValue(sb, nameof(config.FlatpakPlatformSdk), config.FlatpakPlatformSdk);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "The platform runtime version. The latest available version may change periodically.");
            AppendComment(sb, "Refer to Flatpak documentation.");
        }

        AppendKeyValue(sb, nameof(config.FlatpakPlatformVersion), config.FlatpakPlatformVersion);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Flatpak manifest 'finish-args' sandbox permissions. Optional, but if empty, the application will have");
            AppendComment(sb, "extremely limited access to the host environment. This option may be used to grant required");
            AppendComment(sb, "application permissions. Values here should be prefixed with '--' and separated by semicolon or given");
            AppendComment(sb, "in multi-line form. Refer: https://docs.flatpak.org/en/latest/sandbox-permissions.html");
        }

        AppendMultiLineValue(sb, nameof(config.FlatpakFinishArgs), config.FlatpakFinishArgs);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Additional arguments for use with flatpak-builder. Useful for signing. Default is empty.");
            AppendComment(sb, "See flatpak-builder --help.");
        }

        AppendKeyValue(sb, nameof(config.FlatpakBuilderArgs), config.FlatpakBuilderArgs);
        sb.AppendLine();
    }

    private static void AppendRpmSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "RPM OPTIONS");

            AppendComment(sb, "Boolean (true or false) which specifies whether to build the RPM package with 'AutoReq' equal to yes or no.");
            AppendComment(sb, "For dotnet application, the value should typically be false, but see RpmRequires below.");
            AppendComment(sb, "Refer: https://rpm-software-management.github.io/rpm/manual/spec.html");
        }

        AppendKeyValue(sb, nameof(config.RpmAutoReq), config.RpmAutoReq);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Boolean (true or false) which specifies whether to build the RPM package with 'AutoProv' equal to yes or no.");
            AppendComment(sb, "Refer: https://rpm-software-management.github.io/rpm/manual/spec.html");
        }

        AppendKeyValue(sb, nameof(config.RpmAutoProv), config.RpmAutoProv);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional list of RPM dependencies. The list may include multiple values separated with semicolon or given");
            AppendComment(sb, "in multi-line form. If empty, a self-contained dotnet package will successfully run on many (but not all)");
            AppendComment(sb, "Linux distros. In some cases, it will be necessary to explicitly specify additional dependencies.");
            AppendComment(sb, "Default values are recommended for use with dotnet and RPM packages at the time of writing.");
            AppendComment(sb, "For updated information, see: https://learn.microsoft.com/en-us/dotnet/core/install/linux-rhel#dependencies");
        }

        AppendMultiLineValue(sb, nameof(config.RpmRequires), config.RpmRequires);
        sb.AppendLine();
    }

    private static void AppendDebianSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "DEBIAN OPTIONS");

            AppendComment(sb, "Optional list of Debian dependencies. The list may include multiple values separated with semicolon or given");
            AppendComment(sb, "in multi-line form. If empty, a self-contained dotnet package will successfully run on many (but not all)");
            AppendComment(sb, "Linux distros. In some cases, it will be necessary to explicitly specify additional dependencies.");
            AppendComment(sb, "Default values are recommended for use with dotnet and Debian packages at the time of writing.");
            AppendComment(sb, "For updated information, see: https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu#dependencies");
        }

        AppendMultiLineValue(sb, nameof(config.DebianRecommends), config.DebianRecommends);
        sb.AppendLine();
    }

    private static void AppendWindowsSetupSection(StringBuilder sb, Configurations config, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "WINDOWS SETUP OPTIONS");

            AppendComment(sb, "Optional application group name used as the Start Menu folder and install directory under Program Files.");
            AppendComment(sb, "Specifically, it is used to define the InnoSetup DefaultGroupName and DefaultDirName parameters.");
            AppendComment(sb, "If empty (default), suitable values are used based on your application.");
            AppendComment(sb, "See: https://jrsoftware.org/ishelp/index.php?topic=setup_defaultgroupname");
        }

        AppendKeyValue(sb, nameof(config.SetupGroupName), config.SetupGroupName);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Boolean (true or false) which specifies whether the application is to be installed in administrative");
            AppendComment(sb, "mode, or per-user. Default is false. See: https://jrsoftware.org/ishelp/topic_admininstallmode.htm");
        }

        AppendKeyValue(sb, nameof(config.SetupAdminInstall), config.SetupAdminInstall);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional command prompt title. The Windows installer will NOT add your application to the path. However,");
            AppendComment(sb, "if your package contains a command-line utility, setting this value will ensure that a 'Command Prompt'");
            AppendComment(sb, "program menu entry is added (with this title) which, when launched, will open a dedicated command");
            AppendComment(sb, "window with your application directory in its path. Default is empty. See also StartCommand.");
        }

        AppendKeyValue(sb, nameof(config.SetupCommandPrompt), config.SetupCommandPrompt);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Mandatory value which specifies minimum version of Windows that your software runs on. Windows 8 = 6.2,");
            AppendComment(sb, "Windows 10/11 = 10. Default: 10. See: https://jrsoftware.org/ishelp/topic_setup_minversion.htm");
        }

        AppendKeyValue(sb, nameof(config.SetupMinWindowsVersion), config.SetupMinWindowsVersion);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional name and parameters of the Sign Tool to be used to digitally sign: the installer,");
            AppendComment(sb, "uninstaller, and contained exe and dll files. If empty, files will not be signed.");
            AppendComment(sb, "See: https://jrsoftware.org/ishelp/topic_setup_signtool.htm");
        }

        AppendKeyValue(sb, nameof(config.SetupSignTool), config.SetupSignTool);
        sb.AppendLine();

        if (includeComments)
        {
            AppendComment(sb, "Optional name of a script to run before uninstall with InnoSetup. The file is relative to the directory of the");
            AppendComment(sb, "application and must have a default file association. This binds to the `[UninstallRun]` section of InnoSetup.");
        }

        AppendKeyValue(sb, nameof(config.SetupUninstallScript), config.SetupUninstallScript);
        sb.AppendLine();
    }

    private static void AppendConfigurationOptionsSection(StringBuilder sb, bool includeComments)
    {
        if (includeComments)
        {
            AppendSection(sb, "CONFIGURATION OPTIONS");

            AppendComment(sb, "WARNING: DO NOT MODIFY THIS SECTION!");
            AppendComment(sb, "This section is managed by Netloy and contains metadata about the configuration file itself.");
            AppendComment(sb, "Modifying these values may cause compatibility issues or prevent Netloy from processing this file.");
            sb.AppendLine();

            AppendComment(sb, "Configuration file format version. This value is used by Netloy to ensure compatibility with");
            AppendComment(sb, "the configuration file format. It is automatically set when creating a new configuration file");
            AppendComment(sb, "and should NOT be modified manually. If you need to upgrade the configuration format, use:");
            AppendComment(sb, "'netloy --upgrade-config'");
        }

        AppendKeyValue(sb, "ConfigVersion", Constants.Version);
        sb.AppendLine();
    }

    private static Configurations GetDefaultConfiguration()
    {
        var sb = new StringBuilder();

        sb.AppendLine("A detailed description of your application.");
        sb.AppendLine("You can use multiple lines here.");
        sb.AppendLine("Describe the features and functionality of your software.");

        var appDescription = sb.ToString();

        sb.Clear();

        sb.Append("-p:Version=${APP_VERSION} -p:FileVersion=${APP_VERSION} -p:AssemblyVersion=${APP_VERSION} ");
        sb.Append("--self-contained true -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=true");

        var dotnetPublishArgs = sb.ToString();

        sb.Clear();

        sb.AppendLine("--socket=wayland");
        sb.AppendLine("--socket=x11");
        sb.AppendLine("--filesystem=host");
        sb.AppendLine("--share=network");

        var flatpackFinishArgs = sb.ToString();

        sb.Clear();

        sb.AppendLine("krb5-libs");
        sb.AppendLine("libicu");
        sb.AppendLine("openssl-libs");
        sb.AppendLine("zlib");

        var rpmRequires = sb.ToString();

        sb.Clear();

        sb.AppendLine("libc6");
        sb.AppendLine("libgcc1");
        sb.AppendLine("libgssapi-krb5-2");
        sb.AppendLine("libicu");
        sb.AppendLine("libssl");
        sb.AppendLine("zlib1g");

        var debianRecommends = sb.ToString();

        return new Configurations
        {
            AppBaseName = "MyApp",
            AppFriendlyName = "My Application",
            AppId = "com.example.myapp",
            AppVersionRelease = "1.0.0[1]",
            AppShortSummary = "A brief description of your application",
            AppDescription = appDescription,
            AppLicenseId = "MIT",
            PublisherName = "Your Name or Company",
            PublisherCopyright = "Copyright (C) Your Company 2025",
            PublisherLinkName = "Home Page",
            PublisherLinkUrl = "https://example.com",
            PublisherEmail = "contact@example.com",
            DesktopTerminal = false,
            PrimeCategory = "Utility",
            DotnetPublishArgs = dotnetPublishArgs,
            PackageName = "MyApp",
            OutputDirectory = "Deploy/OUT",
            FlatpakPlatformRuntime = "org.freedesktop.Platform",
            FlatpakPlatformSdk = "org.freedesktop.Sdk",
            FlatpakPlatformVersion = "23.08",
            FlatpakFinishArgs = flatpackFinishArgs,
            RpmAutoProv = true,
            RpmRequires = rpmRequires,
            DebianRecommends = debianRecommends,
            SetupMinWindowsVersion = "10",
            ConfigVersion = Constants.Version
        };
    }

    #endregion

    #region Helper Methods

    private static void AppendSection(StringBuilder sb, string sectionName)
    {
        sb.AppendLine("########################################");
        sb.AppendLine($"# {sectionName}");
        sb.AppendLine("########################################");
        sb.AppendLine();
    }

    private static void AppendComment(StringBuilder sb, string comment)
    {
        sb.AppendLine($"# {comment}");
    }

    private static void AppendKeyValue(StringBuilder sb, string key, string value)
    {
        sb.AppendLine($"{key} = {value}");
    }

    private static void AppendKeyValue(StringBuilder sb, string key, bool value)
    {
        sb.AppendLine($"{key} = {value.ToString().ToLower()}");
    }

    private static void AppendMultiLineValue(StringBuilder sb, string key, string value)
    {
        if (value.IsStringNullOrEmpty())
        {
            sb.AppendLine($"{key} = ");
        }
        else
        {
            sb.AppendLine($"{key} = \"\"\"");
            var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                sb.AppendLine($"    {line.Trim()}");
            }

            sb.AppendLine("\"\"\"");
        }
    }

    #endregion
}