namespace Toolbox.Laps;

public enum LapsSource
{
    None,
    WindowsLapsEncrypted,
    WindowsLaps,
    LegacyLaps
}

public enum LapsStatus
{
    Retrieved,
    NotAvailable,
    DecryptionFailed,
    Failed,
    Disabled
}

public sealed class LapsResult
{
    private LapsResult(LapsStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    public LapsStatus Status { get; }

    public string Message { get; }

    public LapsSource Source { get; private init; }

    public string? Password { get; private init; }

    public string? AccountName { get; private init; }

    public DateTimeOffset? Expiration { get; private init; }

    public static LapsResult Retrieved(LapsSource source, string password, string? account, DateTimeOffset? expiration) =>
        new(LapsStatus.Retrieved, "Password retrieved.")
        {
            Source = source,
            Password = password,
            AccountName = account,
            Expiration = expiration
        };

    public static LapsResult NotAvailable() =>
        new(LapsStatus.NotAvailable,
            "No LAPS password is available. Either LAPS is not managing this computer or you are not permitted to read it.");

    public static LapsResult DecryptionFailed(string message) => new(LapsStatus.DecryptionFailed, message)
    {
        Source = LapsSource.WindowsLapsEncrypted
    };

    public static LapsResult Failed(string message) => new(LapsStatus.Failed, message);

    public static LapsResult Disabled() => new(LapsStatus.Disabled, "LAPS retrieval is disabled in settings.");

    public static string Describe(LapsSource source) => source switch
    {
        LapsSource.WindowsLapsEncrypted => "Windows LAPS (encrypted)",
        LapsSource.WindowsLaps => "Windows LAPS",
        LapsSource.LegacyLaps => "Microsoft LAPS (legacy)",
        _ => "LAPS"
    };

    public override string ToString() =>
        $"LapsResult {{ Status = {Status}, Source = {Source}, Password = {(Password is null ? "<none>" : "<redacted>")} }}";
}
