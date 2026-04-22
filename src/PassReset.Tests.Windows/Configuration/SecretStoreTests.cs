using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using PassReset.Web.Services.Configuration;

namespace PassReset.Tests.Windows.Configuration;

public sealed class SecretStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _secretsPath;
    private readonly IConfigProtector _protector;

    public SecretStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "passreset-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _secretsPath = Path.Combine(_tempDir, "secrets.dat");
        _protector = new ConfigProtector(new EphemeralDataProtectionProvider());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private SecretStore MakeSut() =>
        new(_protector, _secretsPath, NullLogger<SecretStore>.Instance);

    [Fact]
    public void Load_MissingFile_ReturnsEmptyBundle()
    {
        Assert.False(File.Exists(_secretsPath));
        var bundle = MakeSut().Load();
        Assert.Equal(SecretBundle.Empty, bundle);
    }

    [Fact]
    public void SaveLoad_FullBundle_RoundTrips()
    {
        var sut = MakeSut();
        var original = new SecretBundle("ldap-p@ss", "svc-p@ss", "smtp-p@ss", "recaptcha-secret");
        sut.Save(original);
        var loaded = sut.Load();
        Assert.Equal(original, loaded);
    }

    [Fact]
    public void SaveLoad_PartialBundle_PreservesNulls()
    {
        var sut = MakeSut();
        var original = new SecretBundle("ldap", null, "smtp", null);
        sut.Save(original);
        var loaded = sut.Load();
        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Save_WritesAreOpaque_NotPlaintext()
    {
        var sut = MakeSut();
        sut.Save(new SecretBundle("my-plaintext-password", null, null, null));
        var onDisk = File.ReadAllText(_secretsPath);
        Assert.DoesNotContain("my-plaintext-password", onDisk);
    }

    [Fact]
    public void Save_WritesAtomically_LeavesNoTmpFile()
    {
        var sut = MakeSut();
        sut.Save(new SecretBundle("a", "b", "c", "d"));
        Assert.True(File.Exists(_secretsPath));
        Assert.False(File.Exists(_secretsPath + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesExisting_PreservesAtomicity()
    {
        var sut = MakeSut();
        sut.Save(new SecretBundle("first", null, null, null));
        sut.Save(new SecretBundle("second", null, null, null));
        Assert.Equal("second", sut.Load().LdapPassword);
    }

    [Fact]
    public void Load_CorruptedFile_Throws()
    {
        File.WriteAllText(_secretsPath, "not-a-valid-ciphertext");
        var sut = MakeSut();
        Assert.ThrowsAny<Exception>(() => sut.Load());
    }
}
