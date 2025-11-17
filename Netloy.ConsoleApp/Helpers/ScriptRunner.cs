using System.Diagnostics;
using System.Runtime.InteropServices;
using Netloy.ConsoleApp.Extensions;
using Netloy.ConsoleApp.NetloyLogger;

namespace Netloy.ConsoleApp.Helpers;

public static class ScriptRunner
{
    public static int RunScript(string scriptPath, string arguments = "")
    {
        ProcessStartInfo processInfo;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var command = string.IsNullOrEmpty(arguments)
                ? $"\"{scriptPath}\""
                : $"\"{scriptPath}\" {arguments}";

            processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var command = string.IsNullOrEmpty(arguments)
                ? scriptPath
                : $"{scriptPath} {arguments}";

            processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }
        else
        {
            throw new PlatformNotSupportedException("Operating system not supported");
        }

        using var process = Process.Start(processInfo);
        if (process == null)
            return -1;

        // Get output
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Logger.LogInfo("Output: \n{0}", forceLog: true, output);
        if (!error.IsStringNullOrEmpty())
        {
            Logger.LogError("Error: {0}", forceLog: true, error);
            return process.ExitCode;
        }

        Logger.LogInfo("Exit Code: {0}", process.ExitCode);
        return process.ExitCode;
    }
}