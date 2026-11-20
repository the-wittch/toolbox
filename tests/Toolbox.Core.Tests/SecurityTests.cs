using System.Buffers.Binary;
using System.Text;
using Toolbox.Laps;
using Toolbox.Security;

namespace Toolbox.Tests;

public class AccessPolicyTests
{
    [Fact]
    public void Disabled_allows() => Assert.True(AccessPolicy.Evaluate(false, null, _ => false).Allowed);

    [Fact]
    public void Enabled_without_groups_denies() =>
        Assert.False(AccessPolicy.Evaluate(true, new[] { " ", "" }, _ => true).Allowed);

    [Fact]
    public void Member_of_any_group_is_allowed() =>
        Assert.True(AccessPolicy.Evaluate(true, new[] { "A", "B" }, g => g == "B").Allowed);

    [Fact]
    public void Lookup_failure_denies_and_reports()
    {
        var decision = AccessPolicy.Evaluate(true, new[] { "Missing" }, _ => throw new InvalidOperationException());
        Assert.False(decision.Allowed);
        Assert.Contains("Missing", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_failure_on_one_group_still_checks_the_rest() =>
        Assert.True(AccessPolicy.Evaluate(true, new[] { "Bad", "Good" },
            g => g == "Bad" ? throw new InvalidOperationException() : true).Allowed);
}

public class ClipboardSecretTrackerTests
{
    [Fact]
    public void Matches_only_the_tracked_secret()
    {
        var tracker = new ClipboardSecretTracker();
        tracker.Track("s3cr3t!", 41);
        Assert.True(tracker.Matches("s3cr3t!"));
        Assert.False(tracker.Matches("something the user copied"));
        Assert.True(tracker.IsUnchangedSince(41));
        Assert.False(tracker.IsUnchangedSince(42));
    }

    [Fact]
    public void Reset_stops_tracking()
    {
        var tracker = new ClipboardSecretTracker();
        tracker.Track("x", 1);
        tracker.Reset();
        Assert.False(tracker.IsTracking);
        Assert.False(tracker.Matches("x"));
        Assert.False(tracker.IsUnchangedSince(1));
    }
}

public class LapsParsingTests
{
    [Fact]
    public void Parses_windows_laps_payload()
    {
        var payload = LapsParsing.ParsePayload("{\"n\":\"Administrator\",\"t\":\"1d9a1b2c3d4e5f6\",\"p\":\"Pa55-w0rd\"}\0");
        Assert.Equal("Administrator", payload.AccountName);
        Assert.Equal("Pa55-w0rd", payload.Password);
        Assert.NotNull(payload.UpdatedUtc);
    }

    [Fact]
    public void Invalid_payload_message_never_contains_the_secret()
    {
        var ex = Assert.Throws<FormatException>(() => LapsParsing.ParsePayload("{\"p\":\"TopSecret\""));
        Assert.DoesNotContain("TopSecret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_without_password_is_rejected() =>
        Assert.Throws<FormatException>(() => LapsParsing.ParsePayload("{\"n\":\"a\"}"));

    [Fact]
    public void Extracts_encrypted_blob_using_header_length()
    {
        var value = new byte[16 + 5 + 3];
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(8), 5);
        for (var i = 0; i < 5; i++)
        {
            value[16 + i] = (byte)(i + 1);
        }

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, LapsParsing.ExtractEncryptedBlob(value).ToArray());
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(20, 0)]
    [InlineData(20, 50)]
    public void Rejects_bad_encrypted_headers(int length, uint declared)
    {
        var value = new byte[length];
        if (length >= 12)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(8), declared);
        }

        Assert.Throws<FormatException>(() => LapsParsing.ExtractEncryptedBlob(value));
    }

    [Fact]
    public void Parses_expiration_values()
    {
        var expected = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.Equal(expected, LapsParsing.ParseExpiration(expected.ToFileTime())!.Value.ToUniversalTime());
        Assert.Null(LapsParsing.ParseExpiration(null));
        Assert.Null(LapsParsing.ParseExpiration(0L));
    }

    [Fact]
    public void Result_ToString_redacts_password()
    {
        var result = LapsResult.Retrieved(LapsSource.LegacyLaps, "Hunter2!", "admin", null);
        Assert.DoesNotContain("Hunter2!", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Hunter2!", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(result.ToString())), StringComparison.Ordinal);
    }
}
