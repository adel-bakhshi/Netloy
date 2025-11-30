using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Netloy.ConsoleApp.Helpers;

public static class ScriptRunner
{
    public static async Task<int> RunScriptAsync(string scriptPath)
    {
        if (!File.Exists(scriptPath))
        {
            Logger.LogError("Script file not found: {0}", forceLog: true, scriptPath);
            return -1;
        }

        ProcessStartInfo processInfo;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Use cmd.exe to execute .bat or .cmd files
            processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory()
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Make script executable first
            await MakeScriptExecutableAsync(scriptPath);

            // Unix-like: Use bash to execute shell scripts
            processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory()
            };
        }
        else
        {
            throw new PlatformNotSupportedException($"Operating system not supported: {RuntimeInformation.OSDescription}");
        }

        Logger.LogInfo("Executing script: {0}", scriptPath);

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Logger.LogError("Failed to start script process", forceLog: true);
            return -1;
        }

        // Read output and error streams
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        // Log output if available
        if (!output.IsStringNullOrEmpty())
            Logger.LogInfo("Script output:\n{0}", forceLog: true, output.Trim());

        // Log errors if available
        if (!error.IsStringNullOrEmpty())
        {
            if (process.ExitCode != 0)
            {
                Logger.LogError("Script error:\n{0}", forceLog: true, error.Trim());
            }
            else
            {
                // Sometimes warnings come through stderr
                Logger.LogWarning("Script warnings:\n{0}", forceLog: true, error.Trim());
            }
        }

        Logger.LogInfo("Script exit code: {0}", process.ExitCode);
        return process.ExitCode;
    }

    private static async Task MakeScriptExecutableAsync(string scriptPath)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                Logger.LogWarning("Failed to start chmod process for script: {0}", scriptPath);
                return;
            }

            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to make script executable: {error}");
            }

            Logger.LogDebug("Script made executable: {0}", scriptPath);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not make script executable: {0}. Error: {1}", scriptPath, ex.Message);
            // Don't throw - script might already be executable or chmod might not be available
        }
    }
}