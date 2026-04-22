using PassReset.Common;
using PassReset.Web.Services.Configuration;

namespace PassReset.Tests.Windows.Configuration;

public sealed class AppSettingsEditorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public AppSettingsEditorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "passreset-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "appsettings.Production.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private AppSettingsEditor MakeSut() => new(_path);

    private const string SeedJson = """
        {
          "Logging": { "LogLevel": { "Default": "Information" } },
          "AllowedHosts": "*",
          "PasswordChangeOptions": {
            "UseAutomaticContext": true,
            "ProviderMode": "Auto",
            "LdapHostnames": ["dc1.corp.local", "dc2.corp.local"],
            "LdapPort": 636,
            "LdapUseSsl": true,
            "BaseDn": "DC=corp,DC=local",
            "ServiceAccountDn": "",
            "DefaultDomain": "CORP",
            "AllowedAdGroups": ["CN=Users,DC=corp,DC=local"],
            "RestrictedAdGroups": [],
            "LocalPolicy": {
              "BannedWordsPath": null,
              "LocalPwnedPasswordsPath": null,
              "MinBannedTermLength": 4
            }
          },
          "SmtpSettings": {
            "Host": "smtp.corp.local",
            "Port": 25,
            "Username": "",
            "FromAddress": "noreply@corp.local",
            "UseStartTls": true
          },
          "ClientSettings": {
            "Recaptcha": { "Enabled": false, "SiteKey": "" }
          },
          "SiemSettings": {
            "Syslog": { "Enabled": false, "Host": "", "Port": 514, "Protocol": "Udp" }
          },
          "SiteLocalKey": "should-survive-roundtrip"
        }
        """;

    private void SeedFile(string json = SeedJson) => File.WriteAllText(_path, json);

    [Fact]
    public void Load_ReadsManagedSections()
    {
        SeedFile();
        var snap = MakeSut().Load();

        Assert.True(snap.PasswordChange.UseAutomaticContext);
        Assert.Equal(ProviderMode.Auto, snap.PasswordChange.ProviderMode);
        Assert.Equal(new[] { "dc1.corp.local", "dc2.corp.local" }, snap.PasswordChange.LdapHostnames);
        Assert.Equal(636, snap.PasswordChange.LdapPort);
        Assert.Equal("CORP", snap.PasswordChange.DefaultDomain);
        Assert.Equal(new[] { "CN=Users,DC=corp,DC=local" }, snap.Groups.AllowedAdGroups);
        Assert.Equal("smtp.corp.local", snap.Smtp.Host);
        Assert.Equal(4, snap.LocalPolicy.MinBannedTermLength);
    }

    [Fact]
    public void Save_PreservesUnmanagedKeys()
    {
        SeedFile();
        var sut = MakeSut();
        var snap = sut.Load();

        sut.Save(snap with { Smtp = snap.Smtp with { Host = "smtp2.corp.local" } });

        var contents = File.ReadAllText(_path);
        Assert.Contains("\"SiteLocalKey\": \"should-survive-roundtrip\"", contents);
        Assert.Contains("\"AllowedHosts\": \"*\"", contents);
        Assert.Contains("\"smtp2.corp.local\"", contents);
    }

    [Fact]
    public void Save_PreservesTopLevelKeyOrder()
    {
        SeedFile();
        var sut = MakeSut();
        var snap = sut.Load();

        sut.Save(snap);

        var reread = File.ReadAllText(_path);
        // Expect keys to appear in their original order: Logging, AllowedHosts,
        // PasswordChangeOptions, SmtpSettings, ClientSettings, SiemSettings, SiteLocalKey.
        var loggingIdx = reread.IndexOf("\"Logging\"", StringComparison.Ordinal);
        var siteLocalIdx = reread.IndexOf("\"SiteLocalKey\"", StringComparison.Ordinal);
        var pwchangeIdx = reread.IndexOf("\"PasswordChangeOptions\"", StringComparison.Ordinal);
        Assert.True(loggingIdx >= 0);
        Assert.True(pwchangeIdx > loggingIdx);
        Assert.True(siteLocalIdx > pwchangeIdx);
    }

    [Fact]
    public void Save_WritesAtomically_LeavesNoTmpFile()
    {
        SeedFile();
        var sut = MakeSut();
        sut.Save(sut.Load());
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Save_MutatingOnlyOwnedKey_DoesNotTouchOtherFields()
    {
        SeedFile();
        var sut = MakeSut();
        var snap = sut.Load();

        sut.Save(snap with { Smtp = snap.Smtp with { Host = "changed.corp.local" } });

        var reloaded = sut.Load();
        Assert.Equal("changed.corp.local", reloaded.Smtp.Host);
        Assert.True(reloaded.PasswordChange.UseAutomaticContext);
        Assert.Equal("CORP", reloaded.PasswordChange.DefaultDomain);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        Assert.False(File.Exists(_path));
        var snap = MakeSut().Load();

        Assert.True(snap.PasswordChange.UseAutomaticContext);
        Assert.Empty(snap.PasswordChange.LdapHostnames);
        Assert.Equal(4, snap.LocalPolicy.MinBannedTermLength);
    }
}
