using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Package;

public class PortablePackageBuilder : PackageBuilderBase, IPackageBuilder
{
    public string PublishOutputDir { get; }

    public PortablePackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
        PublishOutputDir = Path.Combine(RootDirectory, "publish");
    }

    public async Task BuildAsync()
    {
        Logger.LogInfo("Starting Portable package build...");

        await PublishAsync(PublishOutputDir);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            await SetExecutePermissionsAsync();

        var outputPath = Path.Combine(OutputDirectory, OutputName);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            CreateZipArchive(outputPath);
        }
        else
        {
            await CreateTarGzArchiveAsync(outputPath);
        }

        Logger.LogSuccess("Portable package created successfully at: {0}", outputPath);
    }

    public bool Validate()
    {
        return true;
    }

    public void Clear()
    {
        Logger.LogInfo("Cleaning up temporary directory: {0}", RootDirectory);
        Directory.Delete(RootDirectory, true);
    }

    private async Task SetExecutePermissionsAsync()
    {
        Logger.LogInfo("Setting permissions for all files...");

        foreach (var file in Directory.GetFiles(PublishOutputDir, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);

            // Set executable permission for the main executable
            if (fileName == AppExecName)
            {
                Logger.LogInfo("Setting execute permissions for: {0}", fileName);
                var exitCode = await ExecuteChmodAsync(file, "+x");
                if (exitCode != 0)
                    throw new InvalidOperationException($"Failed to set execute permissions on {file}");
            }
            else
            {
                // Set read permission for all other files
                Logger.LogInfo("Setting read permissions for: {0}", fileName);
                var exitCode = await ExecuteChmodAsync(file, "a+rx");
                if (exitCode != 0)
                    throw new InvalidOperationException($"Failed to set read permissions on {file}");
            }
        }

        Logger.LogSuccess("Permissions set successfully for all files!");
    }

    private async Task<int> ExecuteChmodAsync(string filePath, string mode)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"{mode} \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start chmod process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (!output.IsStringNullOrEmpty() && Arguments.Verbose)
            Logger.LogInfo("chmod output: {0}", forceLog: true, output);

        if (!error.IsStringNullOrEmpty() && process.ExitCode != 0)
            Logger.LogError("chmod error: {0}", forceLog: true, error);

        return process.ExitCode;
    }

    private void CreateZipArchive(string outputPath)
    {
        Logger.LogInfo("Creating ZIP archive...");

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        // Use SmallestSize for maximum compression
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        var files = Directory.GetFiles(PublishOutputDir, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var entryName = Path.GetRelativePath(PublishOutputDir, file);

            // Normalize path separators for cross-platform compatibility
            entryName = entryName.Replace("\\", "/");

            Logger.LogInfo("Adding file: {0}", entryName);

            // Add file with maximum compression
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.SmallestSize);
        }

        Logger.LogSuccess("ZIP archive created with {0} files", files.Length);
    }

    private async Task CreateTarGzArchiveAsync(string outputPath)
    {
        Logger.LogInfo("Creating TAR.GZ archive...");

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        // Create temporary tar file
        var tempTarPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tar");

        try
        {
            Logger.LogInfo("Creating TAR archive...");
            await TarFile.CreateFromDirectoryAsync(PublishOutputDir, tempTarPath, includeBaseDirectory: false);

            Logger.LogInfo("Compressing with GZIP...");
            await using var tarStream = File.OpenRead(tempTarPath);
            await using var outputStream = File.Create(outputPath);
            await using var gzipStream = new GZipStream(outputStream, CompressionLevel.SmallestSize);
            await tarStream.CopyToAsync(gzipStream);

            var originalSize = new FileInfo(tempTarPath).Length;
            var compressedSize = new FileInfo(outputPath).Length;
            var ratio = (1 - ((double)compressedSize / originalSize)) * 100;

            Logger.LogSuccess("TAR.GZ archive created. Compression ratio: {0:F2}%", ratio);
        }
        finally
        {
            // Clean up temporary tar file
            if (File.Exists(tempTarPath))
            {
                File.Delete(tempTarPath);
            }
        }
    }
}