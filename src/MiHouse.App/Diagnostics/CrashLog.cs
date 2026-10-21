using System.Globalization;
using System.Reflection;
using System.Text;

namespace MiHouse.App.Diagnostics;

/// <summary>
/// Writes bounded crash reports under the directory supplied by <see cref="Initialize"/> (the
/// package state directory's logs folder). Logging must never throw.
/// </summary>
/// <remarks>
/// Content policy: only whitelisted fields are persisted. Exception messages are never recorded,
/// inner exceptions contribute their type names only, and the stack is reconstructed from
/// reflection frames; raw <see cref="Exception.StackTrace"/> text is never persisted.
/// </remarks>
internal static class CrashLog
{
    private const int _keepFiles = 20;
    private const string _messageNotRecorded = "<not recorded>";

    private static string? _directory;

    /// <summary>
    /// Sets the directory crash reports are written to. Until this is called <see cref="Write"/>
    /// and <see cref="Prune"/> do nothing.
    /// </summary>
    /// <param name="directory">Directory that holds the crash reports.</param>
    public static void Initialize(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>
    /// Writes one crash report and prunes old reports. A failure while logging is ignored.
    /// </summary>
    public static void Write(string source, bool handled, Exception? exception)
    {
        var directory = _directory;
        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);

            var content = new StringBuilder()
                .AppendLine($"Timestamp: {DateTimeOffset.Now:O}")
                .AppendLine($"AppVersion: {GetAppVersion()}")
                .AppendLine($"Source: {NormalizeSource(source)}")
                .AppendLine($"Handled: {handled}")
                .AppendLine($"ExceptionType: {GetExceptionType(exception)}")
                .AppendLine($"InnerExceptionTypes: {GetInnerExceptionTypes(exception)}")
                .AppendLine($"message: {_messageNotRecorded}")
                .AppendLine("StackTrace:")
                .AppendLine(FormatStackTrace(exception))
                .ToString();

            string path;
            using (var stream = CreateLogFile(directory, out path))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
            }

            Prune(path);
        }
        catch (Exception)
        {
            // Crash reporting must never mask the original failure.
        }
    }

    /// <summary>
    /// Deletes all but the newest <see cref="_keepFiles"/> reports. Called at startup and after
    /// every write. <paramref name="newestFile"/> is never deleted.
    /// </summary>
    public static void Prune(string? newestFile = null)
    {
        var directory = _directory;
        if (directory is null)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var protectedPath = newestFile is null ? null : Path.GetFullPath(newestFile);
            var files = Directory
                .EnumerateFiles(directory, "crash-*.log")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToList();

            var kept = 0;
            foreach (var file in files)
            {
                var isProtected = protectedPath is not null
                    && string.Equals(file.FullName, protectedPath, StringComparison.OrdinalIgnoreCase);

                if (kept < _keepFiles || isProtected)
                {
                    kept++;
                    continue;
                }

                try
                {
                    file.Delete();
                }
                catch (Exception)
                {
                    // Keep pruning the remaining files.
                }
            }
        }
        catch (Exception)
        {
            // Pruning is best effort.
        }
    }

    private static FileStream CreateLogFile(string directory, out string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        for (var attempt = 0; ; attempt++)
        {
            var name = attempt == 0
                ? $"crash-{stamp}.log"
                : $"crash-{stamp}-{attempt + 1}.log";
            path = Path.Combine(directory, name);

            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Name collision with a report from the same second; try the next suffix.
            }
        }
    }

    private static string GetAppVersion()
    {
        var assembly = typeof(CrashLog).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static string NormalizeSource(string source)
    {
        if (source.StartsWith("Application", StringComparison.Ordinal))
        {
            return "Application";
        }

        if (source.StartsWith("AppDomain", StringComparison.Ordinal))
        {
            return "AppDomain";
        }

        if (source.StartsWith("TaskScheduler", StringComparison.Ordinal))
        {
            return "TaskScheduler";
        }

        return "Unknown";
    }

    private static string GetExceptionType(Exception? exception)
        => exception?.GetType().FullName ?? "(none)";

    private static string GetInnerExceptionTypes(Exception? exception)
    {
        var types = new List<string>();

        for (var inner = exception?.InnerException; inner is not null; inner = inner.InnerException)
        {
            types.Add(inner.GetType().FullName ?? inner.GetType().Name);
        }

        return types.Count == 0 ? "(none)" : string.Join(", ", types);
    }

    private static string FormatStackTrace(Exception? exception)
    {
        if (exception is null)
        {
            return "(none)";
        }

        System.Diagnostics.StackTrace stackTrace;
        try
        {
            stackTrace = new System.Diagnostics.StackTrace(exception, fNeedFileInfo: true);
        }
        catch (Exception)
        {
            return "(none)";
        }

        var frames = stackTrace.GetFrames();
        if (frames is null || frames.Length == 0)
        {
            return "(none)";
        }

        var lines = new List<string>(frames.Length);
        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method?.DeclaringType is not { } declaringType)
            {
                continue;
            }

            lines.Add($"   at {declaringType.FullName}.{method.Name} (line {frame.GetFileLineNumber()})");
        }

        return lines.Count == 0 ? "(none)" : string.Join(Environment.NewLine, lines);
    }
}
