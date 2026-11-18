using System.Security.Cryptography;
using System.Text;

namespace Toolbox.Security;

public sealed class ClipboardSecretTracker
{
    private byte[]? _salt;
    private byte[]? _hash;
    private uint _sequenceNumber;

    public bool IsTracking => _hash is not null;

    public void Track(string secret, uint sequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(secret);
        _salt = RandomNumberGenerator.GetBytes(16);
        _hash = Compute(_salt, secret);
        _sequenceNumber = sequenceNumber;
    }

    public bool IsUnchangedSince(uint currentSequenceNumber) =>
        IsTracking && currentSequenceNumber == _sequenceNumber;

    public bool Matches(string? clipboardText)
    {
        if (_salt is null || _hash is null || clipboardText is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_hash, Compute(_salt, clipboardText));
    }

    public void Reset()
    {
        _salt = null;
        _hash = null;
        _sequenceNumber = 0;
    }

    private static byte[] Compute(byte[] salt, string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        try
        {
            return HMACSHA256.HashData(salt, data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
