using System.Runtime.InteropServices;
using Toolbox.Diagnostics;
using Toolbox.IO;

namespace Toolbox.Tests;

public class ErrorTranslatorTests
{
    [Fact]
    public void Server_not_operational_is_actionable() =>
        Assert.Contains("domain controller", ErrorTranslator.Describe(new COMException("x", unchecked((int)0x8007203A))), StringComparison.Ordinal);

    [Fact]
    public void Unknown_com_error_shows_code_not_message()
    {
        var text = ErrorTranslator.Describe(new COMException("internal detail", unchecked((int)0x80041234)));
        Assert.Contains("0x80041234", text, StringComparison.Ordinal);
        Assert.DoesNotContain("internal detail", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unwraps_aggregate() =>
        Assert.Contains("timed out", ErrorTranslator.Describe(new AggregateException(new TimeoutException())), StringComparison.Ordinal);

    [Fact]
    public void Access_denied() =>
        Assert.Contains("Access denied", ErrorTranslator.Describe(new UnauthorizedAccessException("C:\\secret")), StringComparison.Ordinal);
}

[Collection("AppLog")]
public sealed class AppLogTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Writes_single_line_entries_with_exception_details()
    {
        AppLog.Initialize(_temp.Path);
        AppLog.Info("line one\r\nline two");
        AppLog.Error("failed", new InvalidOperationException("boom"));
        var text = File.ReadAllText(AppLog.CurrentFilePath!);
        Assert.Contains("[INF]", text, StringComparison.Ordinal);
        Assert.Contains("line one  line two", text, StringComparison.Ordinal);
        Assert.Contains("boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Prunes_old_files()
    {
        var old = _temp.Write("toolbox-20000101.log", "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        AppLog.Initialize(_temp.Path, retentionDays: 14);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Stops_at_size_limit()
    {
        AppLog.Initialize(_temp.Path, maxBytesPerFile: 200);
        for (var i = 0; i < 50; i++)
        {
            AppLog.Info("0123456789012345678901234567890123456789");
        }

        Assert.True(new FileInfo(AppLog.CurrentFilePath!).Length < 600);
    }
}

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Replaces_existing_file_and_leaves_no_temp_files()
    {
        var path = _temp.Write("a.json", "old");
        AtomicFile.WriteAllText(path, "new");
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_temp.Path));
    }
}
