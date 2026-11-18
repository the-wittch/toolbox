namespace Toolbox.Launching;

public sealed record ExecutableResolution(string? Path, string? Error, bool IsNetworkPath)
{
    public bool Succeeded => Path is not null;

    public static ExecutableResolution Fail(string error) => new(null, error, false);
}

public sealed class ExecutableResolver
{
    private readonly Func<string, bool> _fileExists;
    private readonly IReadOnlyList<string> _systemDirectories;
    private readonly Func<string, string> _expandEnvironment;

    public ExecutableResolver(
        Func<string, bool> fileExists,
        IReadOnlyList<string> systemDirectories,
        Func<string, string> expandEnvironment)
    {
        _fileExists = fileExists;
        _systemDirectories = systemDirectories;
        _expandEnvironment = expandEnvironment;
    }

    public static string? ValidateConfiguredPath(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return "Executable path is empty.";
        }

        var value = configured.Trim();
        if (value.Contains(CommandTemplate.ComputerNameToken, StringComparison.OrdinalIgnoreCase))
        {
            return "Executable paths may not contain {computerName}; never run a program chosen by the target computer.";
        }

        if (value.Contains('"', StringComparison.Ordinal))
        {
            return "Executable paths must not contain quotes.";
        }

        if (WindowsPath.IsDevicePath(value))
        {
            return "Device paths (\\\\?\\ or \\\\.\\) are not supported.";
        }

        var shapeOk = value.StartsWith('%') || WindowsPath.IsFullyQualified(value) || WindowsPath.IsBareFileName(value);
        if (!shapeOk)
        {
            return "Use a full path (C:\\... or \\\\server\\share\\...) or a bare program name from System32.";
        }

        var extension = WindowsPath.GetExtension(value);
        if (extension.Length > 0 && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return $"Only .exe programs can be launched (got '{extension}').";
        }

        return null;
    }

    public ExecutableResolution Resolve(string? configured, string userName)
    {
        var staticError = ValidateConfiguredPath(configured);
        if (staticError is not null)
        {
            return ExecutableResolution.Fail(staticError);
        }

        var expanded = _expandEnvironment(
            configured!.Trim().Replace(CommandTemplate.UserNameToken, userName, StringComparison.OrdinalIgnoreCase));

        if (WindowsPath.IsBareFileName(expanded))
        {
            var fileName = WindowsPath.GetExtension(expanded).Length == 0 ? expanded + ".exe" : expanded;
            foreach (var directory in _systemDirectories)
            {
                var candidate = WindowsPath.Combine(directory, fileName);
                if (_fileExists(candidate))
                {
                    return new ExecutableResolution(candidate, null, false);
                }
            }

            return ExecutableResolution.Fail($"'{fileName}' was not found in the Windows system folders. Use a full path.");
        }

        if (!WindowsPath.IsFullyQualified(expanded) || WindowsPath.IsDevicePath(expanded))
        {
            return ExecutableResolution.Fail($"'{expanded}' is not a full path after expanding environment variables.");
        }

        if (!WindowsPath.GetExtension(expanded).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutableResolution.Fail($"'{expanded}' is not an .exe file.");
        }

        if (!_fileExists(expanded))
        {
            return ExecutableResolution.Fail($"'{expanded}' was not found.");
        }

        return new ExecutableResolution(expanded, null, WindowsPath.IsUnc(expanded));
    }
}
