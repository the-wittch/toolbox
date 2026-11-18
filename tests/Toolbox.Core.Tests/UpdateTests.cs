using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Toolbox.Updates;

namespace Toolbox.Tests;

internal static class Releases
{
    public static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static byte[] Publish(TempDirectory temp, string dir, string version, IDictionary<string, string> files)
    {
        var entries = new List<object>();
        foreach (var (path, content) in files)
        {
            temp.Write(dir + "/" + path, content);
            entries.Add(new { path, size = Encoding.UTF8.GetByteCount(content), sha256 = Sha(content) });
        }

        var manifest = new { formatVersion = 1, product = "Toolbox", version, files = entries };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        File.WriteAllBytes(temp.Combine(dir, ReleaseManifest.FileName), bytes);
        return bytes;
    }

    public static byte[] ManifestJson(params (string Path, long Size, string Sha)[] files) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            product = "Toolbox",
            version = "3.0.0",
            files = files.Select(f => new { path = f.Path, size = f.Size, sha256 = f.Sha })
        });
}

public class ReleaseManifestTests
{
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void Parses_valid_manifest()
    {
        var manifest = ReleaseManifest.Parse(Releases.ManifestJson(("Toolbox.exe", 3, Hash), (@"Assets\AppIcon.ico", 1, Hash)));
        Assert.Equal(new Version(3, 0, 0, 0), manifest.ParsedVersion);
        Assert.Equal("Assets/AppIcon.ico", manifest.Files[1].Path);
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData(@"..\evil.exe")]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\Windows\evil.exe")]
    [InlineData("a/../../evil.exe")]
    [InlineData("Toolbox.exe:stream")]
    [InlineData("CON.txt")]
    [InlineData("trailing. ")]
    [InlineData("toolbox-install.json")]
    [InlineData("toolbox-manifest.json")]
    [InlineData(".toolbox-update/staged.json")]
    [InlineData("x.dll.toolbox-old")]
    [InlineData("TOOLBO~1.JSO")]
    public void Rejects_unsafe_paths(string path) =>
        Assert.Throws<ManifestException>(() => ReleaseManifest.Parse(Releases.ManifestJson(("Toolbox.exe", 1, Hash), (path, 1, Hash))));

    [Fact]
    public void Rejects_duplicate_paths_case_insensitively() =>
        Assert.Throws<ManifestException>(() => ReleaseManifest.Parse(Releases.ManifestJson(("Toolbox.exe", 1, Hash), ("toolbox.EXE", 1, Hash))));

    [Fact]
    public void Requires_entry_executable() =>
        Assert.Throws<ManifestException>(() => ReleaseManifest.Parse(Releases.ManifestJson(("Other.exe", 1, Hash))));

    [Fact]
    public void Rejects_bad_hash() =>
        Assert.Throws<ManifestException>(() => ReleaseManifest.Parse(Releases.ManifestJson(("Toolbox.exe", 1, "abc"))));

    [Fact]
    public void Rejects_wrong_product()
    {
        var json = Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"product\":\"Other\",\"version\":\"1.0\",\"files\":[]}");
        Assert.Throws<ManifestException>(() => ReleaseManifest.Parse(json));
    }

    [Fact]
    public void Resolve_refuses_to_escape_root()
    {
        using var temp = new TempDirectory();
        Assert.Throws<ManifestException>(() => ManifestPaths.Resolve(temp.Path, "../x"));
    }
}

public sealed class ManifestVerifierTests : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly X509Certificate2 _cert;
    private readonly X509Certificate2 _otherCert;
    private readonly byte[] _manifest = Encoding.UTF8.GetBytes("{\"formatVersion\":1}");

    public ManifestVerifierTests()
    {
        _cert = CreateCert(_key, "CN=Toolbox Release Signing");
        using var otherKey = RSA.Create(2048);
        _otherCert = CreateCert(otherKey, "CN=Attacker");
    }

    public void Dispose()
    {
        _key.Dispose();
        _cert.Dispose();
        _otherCert.Dispose();
    }

    private static X509Certificate2 CreateCert(RSA key, string subject)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static byte[] Signature(X509Certificate2 cert, RSA key, byte[] data) =>
        JsonSerializer.SerializeToUtf8Bytes(new ManifestSignature
        {
            Algorithm = ManifestSignature.RsaSha256,
            Certificate = Convert.ToBase64String(cert.RawData),
            Signature = Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        });

    [Fact]
    public void Trusts_pinned_sha1_thumbprint()
    {
        var check = ManifestVerifier.Verify(_manifest, Signature(_cert, _key, _manifest), new[] { _cert.Thumbprint }, true);
        Assert.True(check.IsTrusted, check.Error);
        Assert.True(check.IsSigned);
    }

    [Fact]
    public void Trusts_pinned_sha256_thumbprint_with_formatting_noise()
    {
        var pin = "\u200E" + string.Join(':', Convert.ToHexString(SHA256.HashData(_cert.RawData)).Chunk(2).Select(c => new string(c))).ToLowerInvariant();
        var check = ManifestVerifier.Verify(_manifest, Signature(_cert, _key, _manifest), new[] { pin }, true);
        Assert.True(check.IsTrusted, check.Error);
    }

    [Fact]
    public void Rejects_tampered_manifest()
    {
        var sig = Signature(_cert, _key, _manifest);
        var tampered = Encoding.UTF8.GetBytes("{\"formatVersion\":2}");
        Assert.False(ManifestVerifier.Verify(tampered, sig, new[] { _cert.Thumbprint }, true).IsTrusted);
    }

    [Fact]
    public void Rejects_valid_signature_from_unpinned_certificate()
    {
        using var otherKey = RSA.Create(2048);
        using var attacker = CreateCert(otherKey, "CN=Toolbox Release Signing");
        var check = ManifestVerifier.Verify(_manifest, Signature(attacker, otherKey, _manifest), new[] { _cert.Thumbprint }, true);
        Assert.False(check.IsTrusted);
        Assert.Contains("untrusted", check.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_pinned_certificate_with_foreign_signature()
    {
        using var otherKey = RSA.Create(2048);
        var forged = Signature(_cert, otherKey, _manifest);
        Assert.False(ManifestVerifier.Verify(_manifest, forged, new[] { _cert.Thumbprint }, true).IsTrusted);
    }

    [Fact]
    public void Missing_signature_fails_when_pinned() =>
        Assert.False(ManifestVerifier.Verify(_manifest, null, new[] { _cert.Thumbprint }, false).IsTrusted);

    [Fact]
    public void Garbage_signature_fails() =>
        Assert.False(ManifestVerifier.Verify(_manifest, Encoding.UTF8.GetBytes("nope"), new[] { _cert.Thumbprint }, true).IsTrusted);

    [Fact]
    public void Requiring_signature_without_pins_fails_closed() =>
        Assert.False(ManifestVerifier.Verify(_manifest, Signature(_cert, _key, _manifest), Array.Empty<string>(), true).IsTrusted);

    [Fact]
    public void No_pins_means_integrity_only()
    {
        var check = ManifestVerifier.Verify(_manifest, null, Array.Empty<string>(), false);
        Assert.True(check.IsTrusted);
        Assert.False(check.IsSigned);
    }
}

public sealed class UpdateStagingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _source;
    private readonly string _install;

    public UpdateStagingTests()
    {
        _source = _temp.Combine("share");
        _install = _temp.Combine("install");
        Directory.CreateDirectory(_install);
    }

    public void Dispose() => _temp.Dispose();

    private ReleaseManifest PublishV2() => ReleaseManifest.Parse(Releases.Publish(_temp, "share", "2.0.0", new Dictionary<string, string>
    {
        ["Toolbox.exe"] = "exe v2",
        ["Toolbox.dll"] = "dll v2",
        ["Assets/AppIcon.ico"] = "icon",
        ["appsettings.json"] = "{}"
    }));

    private void InstallV1()
    {
        _temp.Write("install/Toolbox.exe", "exe v1");
        _temp.Write("install/Toolbox.dll", "dll v1");
        _temp.Write("install/Assets/AppIcon.ico", "icon");
        _temp.Write("install/appsettings.json", "{}");
        new InstallInfo { UpdateSourcePath = _source, InstalledVersion = "1.0.0" }.Save(_install);
    }

    [Fact]
    public async Task Stages_only_changed_files_then_applies_them()
    {
        InstallV1();
        var staged = await UpdateStager.StageAsync(_source, _install, PublishV2(), null, CancellationToken.None);
        Assert.Equal(2, staged.ChangedFiles);
        Assert.Equal(2, staged.UnchangedFiles);
        Assert.Equal("exe v1", File.ReadAllText(Path.Combine(_install, "Toolbox.exe")));

        var result = UpdateApplier.Apply(_install);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("exe v2", File.ReadAllText(Path.Combine(_install, "Toolbox.exe")));
        Assert.Equal("dll v2", File.ReadAllText(Path.Combine(_install, "Toolbox.dll")));
        Assert.False(Directory.Exists(UpdateLayout.WorkDirectory(_install)));
        Assert.Empty(Directory.GetFiles(_install, "*.toolbox-old*", SearchOption.AllDirectories));
        Assert.Equal("2.0.0", InstallInfo.TryLoad(_install)!.InstalledVersion);
    }

    [Fact]
    public async Task Tampered_release_file_is_refused_and_nothing_is_staged()
    {
        InstallV1();
        var manifest = PublishV2();
        File.WriteAllText(Path.Combine(_source, "Toolbox.dll"), "dll evil");
        await Assert.ThrowsAsync<UpdateIntegrityException>(() =>
            UpdateStager.StageAsync(_source, _install, manifest, null, CancellationToken.None));
        Assert.False(Directory.Exists(UpdateLayout.WorkDirectory(_install)));
        Assert.Equal("dll v1", File.ReadAllText(Path.Combine(_install, "Toolbox.dll")));
    }

    [Fact]
    public async Task Oversized_release_file_is_refused()
    {
        InstallV1();
        var manifest = PublishV2();
        File.AppendAllText(Path.Combine(_source, "Toolbox.exe"), new string('x', 1000));
        await Assert.ThrowsAsync<UpdateIntegrityException>(() =>
            UpdateStager.StageAsync(_source, _install, manifest, null, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_release_file_is_refused()
    {
        InstallV1();
        var manifest = PublishV2();
        File.Delete(Path.Combine(_source, "Toolbox.dll"));
        await Assert.ThrowsAsync<UpdateIntegrityException>(() =>
            UpdateStager.StageAsync(_source, _install, manifest, null, CancellationToken.None));
    }

    [Fact]
    public async Task Extra_files_on_the_share_are_ignored()
    {
        InstallV1();
        var manifest = PublishV2();
        File.WriteAllText(Path.Combine(_source, "planted.dll"), "evil");
        await UpdateStager.StageAsync(_source, _install, manifest, null, CancellationToken.None);
        Assert.True(UpdateApplier.Apply(_install).Succeeded);
        Assert.False(File.Exists(Path.Combine(_install, "planted.dll")));
    }

    [Fact]
    public async Task Failed_apply_rolls_back()
    {
        InstallV1();
        await UpdateStager.StageAsync(_source, _install, PublishV2(), null, CancellationToken.None);

        var marker = UpdateApplier.ReadMarker(_install)!;
        var blocked = Path.Combine(_install, marker.Files[1].Replace('/', Path.DirectorySeparatorChar));
        File.Delete(blocked);
        Directory.CreateDirectory(blocked);

        var result = UpdateApplier.Apply(_install);
        Assert.False(result.Succeeded);
        Assert.Contains("rolled back", result.Message, StringComparison.Ordinal);
        var first = Path.Combine(_install, marker.Files[0].Replace('/', Path.DirectorySeparatorChar));
        Assert.EndsWith("v1", File.ReadAllText(first), StringComparison.Ordinal);
        Assert.False(UpdateApplier.IsInterrupted(_install));
        Assert.True(File.Exists(UpdateLayout.MarkerPath(_install)));
    }

    [Fact]
    public async Task Interrupted_apply_is_completed_on_retry()
    {
        InstallV1();
        await UpdateStager.StageAsync(_source, _install, PublishV2(), null, CancellationToken.None);
        var marker = UpdateApplier.ReadMarker(_install)!;

        var rel = marker.Files[0].Replace('/', Path.DirectorySeparatorChar);
        var target = Path.Combine(_install, rel);
        File.Move(target, target + UpdateLayout.OldFileMarker);
        File.Move(Path.Combine(UpdateLayout.StagingDirectory(_install), rel), target);
        File.WriteAllText(UpdateLayout.JournalPath(_install), marker.Version);

        Assert.True(UpdateApplier.IsInterrupted(_install));
        var result = UpdateApplier.Apply(_install);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("exe v2", File.ReadAllText(Path.Combine(_install, "Toolbox.exe")));
        Assert.Equal("dll v2", File.ReadAllText(Path.Combine(_install, "Toolbox.dll")));
        Assert.False(UpdateApplier.IsInterrupted(_install));
    }

    [Fact]
    public async Task Fresh_install_into_empty_folder()
    {
        var target = _temp.Combine("fresh");
        await UpdateStager.StageAsync(_source, target, PublishV2(), null, CancellationToken.None);
        Assert.True(UpdateApplier.Apply(target).Succeeded);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(target, "Assets", "AppIcon.ico")));
    }

    [Fact]
    public async Task Cancellation_cleans_up()
    {
        InstallV1();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateStager.StageAsync(_source, _install, PublishV2(), null, cts.Token));
        Assert.False(Directory.Exists(UpdateLayout.WorkDirectory(_install)));
    }

    [Fact]
    public void Cleanup_removes_old_files_but_not_work_folder()
    {
        _temp.Write("install/a.dll.toolbox-old", "x");
        _temp.Write("install/sub/b.dll.toolbox-old-1234", "x");
        Assert.Equal(2, UpdateApplier.CleanupOldFiles(_install));
    }
}

public class VersionParsingTests
{
    [Theory]
    [InlineData("3", "3.0.0.0")]
    [InlineData("3.1", "3.1.0.0")]
    [InlineData("v3.1.2", "3.1.2.0")]
    [InlineData("3.1.2+abcdef", "3.1.2.0")]
    [InlineData("3.1.2-beta", "3.1.2.0")]
    [InlineData("3.1.2.4", "3.1.2.4")]
    public void Parses(string text, string expected)
    {
        Assert.True(VersionParsing.TryParse(text, out var v));
        Assert.Equal(Version.Parse(expected), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Rejects(string? text) => Assert.False(VersionParsing.TryParse(text, out _));

    [Fact]
    public void Three_and_four_part_versions_compare_equal()
    {
        VersionParsing.TryParse("3.0.0", out var a);
        VersionParsing.TryParse("3.0.0.0", out var b);
        Assert.Equal(a, b);
    }
}

public sealed class PowerShellReleaseCompatibilityTests : IDisposable
{
    // produced by scripts/ToolboxRelease.psm1 with a throwaway self-signed certificate
    private const string FixtureSigner = "4222FBEFD47B0ACDF7782DBBC65820A2958160B4";

    private readonly TempDirectory _temp = new();
    private readonly string _fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "signed-release");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Release_signed_by_the_publish_script_verifies_and_installs()
    {
        var manifestBytes = File.ReadAllBytes(Path.Combine(_fixture, ReleaseManifest.FileName));
        var signature = File.ReadAllBytes(Path.Combine(_fixture, ReleaseManifest.SignatureFileName));

        var check = ManifestVerifier.Verify(manifestBytes, signature, new[] { FixtureSigner }, requireSignature: true);
        Assert.True(check.IsTrusted, check.Error);
        Assert.Equal("CN=Toolbox Test Signing", check.Signer);

        var manifest = ReleaseManifest.Parse(manifestBytes);
        await UpdateStager.StageAsync(_fixture, _temp.Path, manifest, null, CancellationToken.None);
        Assert.True(UpdateApplier.Apply(_temp.Path).Succeeded);
        Assert.Equal("exe", File.ReadAllText(Path.Combine(_temp.Path, "Toolbox.exe")));
    }

    [Fact]
    public void Release_is_rejected_with_a_different_pin()
    {
        var manifestBytes = File.ReadAllBytes(Path.Combine(_fixture, ReleaseManifest.FileName));
        var signature = File.ReadAllBytes(Path.Combine(_fixture, ReleaseManifest.SignatureFileName));
        Assert.False(ManifestVerifier.Verify(manifestBytes, signature, new[] { new string('B', 40) }, true).IsTrusted);
    }
}
