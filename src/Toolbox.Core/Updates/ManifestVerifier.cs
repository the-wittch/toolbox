using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolbox.Updates;

public sealed class ManifestSignature
{
    public const string RsaSha256 = "RSA-PKCS1-SHA256";

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("certificate")]
    public string Certificate { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}

public sealed record SignatureCheck(bool IsTrusted, bool IsSigned, string? Signer, string? Error)
{
    public static SignatureCheck Fail(string error) => new(false, false, null, error);
}

public static class ManifestVerifier
{
    public static SignatureCheck Verify(
        byte[] manifestBytes,
        byte[]? signatureFileBytes,
        IReadOnlyCollection<string> trustedThumbprints,
        bool requireSignature)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        var pins = trustedThumbprints
            .Select(Thumbprints.Normalize)
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (requireSignature && pins.Count == 0)
        {
            return SignatureCheck.Fail("Signed updates are required but no trusted signer thumbprint is configured.");
        }

        // without a pinned signer only the file hashes are checked
        if (pins.Count == 0)
        {
            return new SignatureCheck(true, false, null, null);
        }

        if (signatureFileBytes is null || signatureFileBytes.Length == 0)
        {
            return SignatureCheck.Fail($"The release is not signed ({ReleaseManifest.SignatureFileName} is missing).");
        }

        ManifestSignature? signature;
        try
        {
            signature = JsonSerializer.Deserialize<ManifestSignature>(signatureFileBytes);
        }
        catch (JsonException)
        {
            return SignatureCheck.Fail("The release signature file is not valid JSON.");
        }

        if (signature is null || !string.Equals(signature.Algorithm, ManifestSignature.RsaSha256, StringComparison.Ordinal))
        {
            return SignatureCheck.Fail("The release signature uses an unsupported algorithm.");
        }

        byte[] certificateBytes;
        byte[] signatureBytes;
        try
        {
            certificateBytes = Convert.FromBase64String(signature.Certificate);
            signatureBytes = Convert.FromBase64String(signature.Signature);
        }
        catch (FormatException)
        {
            return SignatureCheck.Fail("The release signature file is malformed.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
        }
        catch (CryptographicException)
        {
            return SignatureCheck.Fail("The signing certificate in the release signature cannot be read.");
        }

        using (certificate)
        {
            var sha1 = Thumbprints.Normalize(certificate.GetCertHashString(HashAlgorithmName.SHA1));
            var sha256 = Thumbprints.Normalize(certificate.GetCertHashString(HashAlgorithmName.SHA256));
            if (!pins.Contains(sha1) && !pins.Contains(sha256))
            {
                return SignatureCheck.Fail($"The release was signed by an untrusted certificate ({certificate.Subject}, {sha1}).");
            }

            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null)
            {
                return SignatureCheck.Fail("The signing certificate does not contain an RSA key.");
            }

            var valid = rsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return valid
                ? new SignatureCheck(true, true, certificate.Subject, null)
                : SignatureCheck.Fail("The release signature does not match the manifest. The release may have been modified.");
        }
    }
}

public static class Thumbprints
{
    public static string Normalize(string? value) =>
        new string((value ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var allowedNoise = value.All(c => Uri.IsHexDigit(c) || c is ' ' or ':' or '\u200E');
        var length = Normalize(value).Length;
        return allowedNoise && length is 40 or 64;
    }
}
