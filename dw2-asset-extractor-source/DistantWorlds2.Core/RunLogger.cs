using System.Text;

namespace DistantWorlds2.Core;

public static class RunLogger
{
    private static readonly object Sync = new();
    private static StreamWriter? writer;

    public static void Initialize(string logPath)
    {
        lock (Sync)
        {
            writer?.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            writer = new StreamWriter(logPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }
    }

    public static void Info(string message) => Write(message, null, false);
    public static void Warn(string message) => Write(message, ConsoleColor.Yellow, false);
    public static void Error(string message) => Write(message, ConsoleColor.Red, true);
    public static void Yellow(string message) => Write(message, ConsoleColor.Yellow, false);

    public static void Dispose()
    {
        lock (Sync)
        {
            writer?.Dispose();
            writer = null;
        }
    }

    private static void Write(string message, ConsoleColor? color, bool stderr)
    {
        lock (Sync)
        {
            writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");

            var previousColor = Console.ForegroundColor;
            try
            {
                if (color.HasValue)
                    Console.ForegroundColor = color.Value;

                if (stderr)
                    Console.Error.WriteLine(message);
                else
                    Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = previousColor;
            }
        }
    }
}
