using System.Globalization;
using System.Text;

namespace Toolbox.Diagnostics;

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static string? _directory;
    private static long _maxBytesPerFile = 10 * 1024 * 1024;
    private static bool _limitNoted;

    public static string? Directory => _directory;

    public static string? CurrentFilePath =>
        _directory is null ? null : Path.Combine(_directory, FileNameFor(DateTime.UtcNow));

    public static void Initialize(string directory, int retentionDays = 14, long maxBytesPerFile = 10 * 1024 * 1024)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                _directory = directory;
                _maxBytesPerFile = maxBytesPerFile;
                _limitNoted = false;
                Prune(directory, retentionDays);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _directory = null;
            }
        }
    }

    public static void Info(string message) => Write("INF", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WRN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    public static string FileNameFor(DateTime utc) =>
        "toolbox-" + utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";

    private static void Write(string level, string message, Exception? exception)
    {
        var directory = _directory;
        if (directory is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(Environment.ProcessId.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(OneLine(message));
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        line.AppendLine();

        lock (Gate)
        {
            try
            {
                var path = Path.Combine(directory, FileNameFor(now));
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= _maxBytesPerFile)
                {
                    if (_limitNoted)
                    {
                        return;
                    }

                    _limitNoted = true;
                    line.Clear().Append(now.ToString("O", CultureInfo.InvariantCulture))
                        .AppendLine(" [WRN] Log size limit reached; further entries today are dropped.");
                }

                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Utf8NoBom.GetBytes(line.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string OneLine(string message) =>
        message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static void Prune(string directory, int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "toolbox-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
