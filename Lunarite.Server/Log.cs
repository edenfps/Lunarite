using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Lunarite;

public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? _file;

    // Opt-in durable info/warning/error trace for unattended client tests. Debug packet traces
    // remain debugger-only so a normal log does not persist raw authentication payloads.
    public static string EnableFile(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("A server log directory is required.", nameof(directory));
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        string name = $"server-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss.fffffffZ}-{Environment.ProcessId}.log";
        string path = Path.Combine(root, name);
        var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough), new UTF8Encoding(false)) { AutoFlush = true };
        lock (Gate)
        {
            _file?.Dispose();
            _file = writer;
        }
        return path;
    }

    public static void DisableFile()
    {
        lock (Gate)
        {
            _file?.Dispose();
            _file = null;
        }
    }

    public static void WriteSuccess(string format, params object?[] args)
    {
        WriteTime();

        Console.ForegroundColor = ConsoleColor.Green;

        WriteLine(format, args);
    }

    public static void WriteWarning(string format, params object?[] args)
    {
        WriteTime();

        Console.ForegroundColor = ConsoleColor.Yellow;

        WriteLine(format, args);
    }

    public static void WriteError(string format, params object?[] args)
    {
        WriteTime();

        Console.ForegroundColor = ConsoleColor.Red;

        WriteLine(format, args);
    }

    public static void WriteInfo(string format, params object?[] args)
    {
        WriteTime();

        WriteLine(format, args);
    }

    public static void WriteDebug(string format, params object?[] args)
    {
        Debug.WriteLine("[{0}] {1}", DateTime.Now.ToLongTimeString(), string.Format(format.Replace("\n", "\n\t\t"), args));
    }

    // Already formatted by Microsoft.Extensions.Logging; see LogProvider.
    public static void Write(LogLevel level, string message)
    {
        if (level <= LogLevel.Debug)
        {
            Debug.WriteLine("[{0}] {1}", DateTime.Now.ToLongTimeString(), message.Replace("\n", "\n\t\t"));
            return;
        }

        WriteTime();

        if (level == LogLevel.Warning)
            Console.ForegroundColor = ConsoleColor.Yellow;
        else if (level >= LogLevel.Error)
            Console.ForegroundColor = ConsoleColor.Red;

        WriteLine("{0}", message.Replace("\n", "\n\t"));
    }

    // Date and time: the live log runs for days and outlives restarts in logs/.
    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static void WriteTime()
    {
        Console.Write("[{0}] ", Now());
    }

    private static void WriteLine(string format, params object?[] args)
    {
        string message = string.Format(format.Replace("\n", "\n\t"), args);
        string time = Now();
        lock (Gate)
        {
            Console.WriteLine(message);
            Console.ResetColor();
            _file?.WriteLine($"[{time}] {message}");
            Debug.WriteLine("[{0}] {1}", time, string.Format(format.Replace("\n", "\n\t\t"), args));
        }
    }
}
