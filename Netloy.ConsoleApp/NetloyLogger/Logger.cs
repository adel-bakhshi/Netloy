using Netloy.ConsoleApp.Extensions;

namespace Netloy.ConsoleApp.NetloyLogger;

public static class Logger
{
    private static readonly Lock Lock = new();

    public static LogLevel CurrentLevel { get; set; } = LogLevel.Debug;
    public static bool ShowTimestamp { get; set; } = true;
    public static bool UseColors { get; set; } = true;
    public static bool IsVerbose { get; set; }

    public static void LogDebug(string? message, params object?[]? args)
    {
        Log(message, LogLevel.Debug, null, args);
    }

    public static void LogDebug(string? message, bool? forceLog = null, params object?[]? args)
    {
        Log(message, LogLevel.Debug, forceLog, args);
    }

    public static void LogInfo(string? message, params object?[]? args)
    {
        Log(message, LogLevel.Info, null, args);
    }

    public static void LogInfo(string? message, bool? forceLog = null, params object?[]? args)
    {
        Log(message, LogLevel.Info, forceLog, args);
    }

    public static void LogSuccess(string? message, params object?[]? args)
    {
        Log(message, LogLevel.Success, null, args);
    }

    public static void LogSuccess(string? message, bool? forceLog = null, params object?[]? args)
    {
        Log(message, LogLevel.Success, forceLog, args);
    }

    public static void LogWarning(string? message, params object?[]? args)
    {
        Log(message, LogLevel.Warning, null, args);
    }

    public static void LogWarning(string? message, bool? forceLog = null, params object?[]? args)
    {
        Log(message, LogLevel.Warning, forceLog, args);
    }

    public static void LogError(string? message, params object?[]? args)
    {
        Log(message, LogLevel.Error, null, args);
    }

    public static void LogError(string? message, bool? forceLog = null, params object?[]? args)
    {
        Log(message, LogLevel.Error, forceLog, args);
    }

    public static void LogException(Exception ex)
    {
        LogError($"{ex.GetType().Name}: {ex.Message}", forceLog: true);
        LogDebug($"Stack Trace:\n{ex.StackTrace}", forceLog: true);
    }

    private static void Log(string? message, LogLevel mode, bool? forceLog = null, params object?[]? args)
    {
        if (message.IsStringNullOrEmpty())
            return;

        if (!IsVerbose && mode < CurrentLevel && forceLog != true)
            return;

        lock (Lock)
        {
            var formattedMessage = args?.Length > 0 ? string.Format(message!, args) : message;
            var timestamp = ShowTimestamp ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " : string.Empty;
            var prefix = GetLevelPrefix(mode);

            if (UseColors && !Console.IsOutputRedirected)
            {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = GetLevelColor(mode);
                Console.Write($"{timestamp}{prefix}");
                Console.ForegroundColor = originalColor;
                Console.WriteLine($" {formattedMessage}");
            }
            else
            {
                Console.WriteLine($"{timestamp}{prefix} {formattedMessage}");
            }
        }
    }

    private static string GetLevelPrefix(LogLevel level) => level switch
    {
        LogLevel.Debug => "[DEBUG]",
        LogLevel.Info => "[INFO]",
        LogLevel.Success => "[SUCCESS]",
        LogLevel.Warning => "[WARN]",
        LogLevel.Error => "[ERROR]",
        _ => "[LOG]"
    };

    private static ConsoleColor GetLevelColor(LogLevel level) => level switch
    {
        LogLevel.Debug => ConsoleColor.Gray,
        LogLevel.Info => ConsoleColor.Cyan,
        LogLevel.Success => ConsoleColor.Green,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        _ => ConsoleColor.White
    };
}