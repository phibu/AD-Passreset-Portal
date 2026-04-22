using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PassReset.Web.Services.Configuration;

namespace PassReset.Tests.Windows.Configuration;

public sealed class SecretConfigurationProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _secretsPath;
    private readonly ConfigProtector _protector;

    public SecretConfigurationProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "passreset-scp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _secretsPath = Path.Combine(_tempDir, "secrets.dat");
        _protector = new ConfigProtector(new EphemeralDataProtectionProvider());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private IConfigurationRoot BuildConfig(bool includeEnvVars = false, IReadOnlyDictionary<string, string?>? envVarOverrides = null)
    {
        var cb = new ConfigurationBuilder();
        cb.Add(new SecretConfigurationSource(
            () => new SecretStore(_protector, _secretsPath, NullLogger<SecretStore>.Instance)));
        if (includeEnvVars && envVarOverrides is not null)
            cb.AddInMemoryCollection(envVarOverrides);
        return cb.Build();
    }

    private void Seed(SecretBundle bundle) =>
        new SecretStore(_protector, _secretsPath, NullLogger<SecretStore>.Instance).Save(bundle);

    [Fact]
    public void MissingFile_ContributesNoKeys()
    {
        var cfg = BuildConfig();
        Assert.Null(cfg["PasswordChangeOptions:LdapPassword"]);
        Assert.Null(cfg["SmtpSettings:Password"]);
    }

    [Fact]
    public void SeededBundle_SurfacesAtCanonicalKeys()
    {
        Seed(new SecretBundle(
            LdapPassword: "ldap-secret",
            ServiceAccountPassword: "svc-secret",
            SmtpPassword: "smtp-secret",
            RecaptchaPrivateKey: "recaptcha-secret"));

        var cfg = BuildConfig();

        Assert.Equal("ldap-secret",      cfg["PasswordChangeOptions:LdapPassword"]);
        Assert.Equal("svc-secret",       cfg["PasswordChangeOptions:ServiceAccountPassword"]);
        Assert.Equal("smtp-secret",      cfg["SmtpSettings:Password"]);
        Assert.Equal("recaptcha-secret", cfg["ClientSettings:Recaptcha:PrivateKey"]);
    }

    [Fact]
    public void NullFieldsInBundle_AreNotAddedToConfiguration()
    {
        Seed(new SecretBundle("only-ldap", null, null, null));
        var cfg = BuildConfig();
        Assert.Equal("only-ldap", cfg["PasswordChangeOptions:LdapPassword"]);
        Assert.Null(cfg["SmtpSettings:Password"]);
        Assert.Null(cfg["PasswordChangeOptions:ServiceAccountPassword"]);
    }

    [Fact]
    public void EnvVarSource_AddedAfterSecretSource_WinsOverDecryptedValue()
    {
        // Emulates the STAB-017 env var precedence guarantee: env vars are added
        // to the ConfigurationBuilder AFTER the secret source and must override.
        Seed(new SecretBundle("from-secrets-file", null, null, null));
        var cfg = BuildConfig(includeEnvVars: true,
            envVarOverrides: new Dictionary<string, string?> {
                ["PasswordChangeOptions:LdapPassword"] = "from-env-var"
            });
        Assert.Equal("from-env-var", cfg["PasswordChangeOptions:LdapPassword"]);
    }
}
