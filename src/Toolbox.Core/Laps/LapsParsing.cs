using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Toolbox.Ldap;

namespace Toolbox.Laps;

public static class LapsAttributes
{
    public const string WindowsEncryptedPassword = "msLAPS-EncryptedPassword";
    public const string WindowsPassword = "msLAPS-Password";
    public const string WindowsExpiration = "msLAPS-PasswordExpirationTime";
    public const string LegacyPassword = "ms-Mcs-AdmPwd";
    public const string LegacyExpiration = "ms-Mcs-AdmPwdExpirationTime";

    public static readonly string[] All =
    {
        WindowsEncryptedPassword, WindowsPassword, WindowsExpiration, LegacyPassword, LegacyExpiration
    };
}

public sealed record WindowsLapsPayload(string? AccountName, string Password, DateTimeOffset? UpdatedUtc);

public static class LapsParsing
{
    // 16-byte header (timestamp, blob length, flags) followed by a DPAPI-NG blob
    public const int EncryptedHeaderLength = 16;

    public static ReadOnlyMemory<byte> ExtractEncryptedBlob(byte[] attributeValue)
    {
        ArgumentNullException.ThrowIfNull(attributeValue);
        if (attributeValue.Length <= EncryptedHeaderLength)
        {
            throw new FormatException("The encrypted LAPS attribute is too short.");
        }

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(attributeValue.AsSpan(8, 4));
        var available = attributeValue.Length - EncryptedHeaderLength;
        if (declared == 0 || declared > available)
        {
            throw new FormatException("The encrypted LAPS attribute has an invalid length header.");
        }

        return attributeValue.AsMemory(EncryptedHeaderLength, (int)declared);
    }

    public static WindowsLapsPayload ParsePayload(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var trimmed = json.TrimEnd('\0', ' ', '\r', '\n', '\t');

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("p", out var passwordElement) ||
                passwordElement.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("The Windows LAPS payload has no password field.");
            }

            var password = passwordElement.GetString();
            if (string.IsNullOrEmpty(password))
            {
                throw new FormatException("The Windows LAPS payload has an empty password.");
            }

            string? account = null;
            if (root.TryGetProperty("n", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                account = nameElement.GetString();
            }

            DateTimeOffset? updated = null;
            if (root.TryGetProperty("t", out var timeElement) && timeElement.ValueKind == JsonValueKind.String &&
                long.TryParse(timeElement.GetString(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var fileTime))
            {
                updated = WindowsVersionFormatter.FromFileTime(fileTime);
            }

            return new WindowsLapsPayload(account, password, updated);
        }
        catch (JsonException)
        {
            throw new FormatException("The Windows LAPS payload is not valid JSON.");
        }
    }

    public static DateTimeOffset? ParseExpiration(object? attributeValue) => attributeValue switch
    {
        long fileTime => WindowsVersionFormatter.FromFileTime(fileTime),
        string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) =>
            WindowsVersionFormatter.FromFileTime(parsed),
        _ => null
    };
}
