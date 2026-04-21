using System.DirectoryServices.Protocols;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider.Ldap;
using PassReset.Tests.Fakes;
using Xunit;

namespace PassReset.Tests.Services;

public class LdapPasswordChangeProviderTests
{
    private static (LdapPasswordChangeProvider sut, FakeLdapSession fake) Build(
        PasswordChangeOptions? opts = null)
    {
        opts ??= new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname", "userprincipalname", "mail" },
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var fake = new FakeLdapSession();
        var sut = new LdapPasswordChangeProvider(
            Options.Create(opts),
            NullLogger<LdapPasswordChangeProvider>.Instance,
            () => fake);
        return (sut, fake);
    }

    private static SearchResponse MakeResponse(params SearchResultEntry[] entries)
    {
        // SearchResponse has no parameterless ctor on .NET 10; use the internal
        // (string dn, DirectoryControl[] controls, ResultCode result, string message, Uri[] referral) overload.
        var response = (SearchResponse)Activator.CreateInstance(
            typeof(SearchResponse),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object?[] { string.Empty, Array.Empty<DirectoryControl>(), ResultCode.Success, string.Empty, Array.Empty<Uri>() },
            null)!;
        var entriesProp = typeof(SearchResponse).GetProperty("Entries")!;
        var collection = (SearchResultEntryCollection)entriesProp.GetValue(response)!;
        var addMethod = typeof(SearchResultEntryCollection).GetMethod(
            "Add", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(SearchResultEntry) }, null)!;
        foreach (var e in entries) addMethod.Invoke(collection, new object?[] { e });
        return response;
    }

    private static SearchResultEntry MakeEntry(string dn, params (string Name, string Value)[] attrs)
    {
        // SearchResultEntry has a public (string dn) ctor on .NET 10.
        var entry = (SearchResultEntry)Activator.CreateInstance(
            typeof(SearchResultEntry),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object[] { dn },
            null)!;
        return entry;
    }

    private static ModifyResponse MakeModifyResponse(
        ResultCode resultCode = ResultCode.Success, string errorMessage = "")
    {
        // ModifyResponse has no public parameterless ctor. Use the internal
        // (string dn, DirectoryControl[] controls, ResultCode result, string message, Uri[] referral) overload,
        // same pattern as MakeResponse for SearchResponse.
        var response = (ModifyResponse)Activator.CreateInstance(
            typeof(ModifyResponse),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object?[] { string.Empty, Array.Empty<DirectoryControl>(), resultCode, errorMessage, Array.Empty<Uri>() },
            null)!;
        return response;
    }

    [Fact]
    public async Task FindUserDn_SamAccountNameHits_ReturnsDn()
    {
        var (sut, fake) = Build();
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "alice" })!;
        var dn = await task;

        Assert.Equal("CN=Alice,OU=Users,DC=corp,DC=example,DC=com", dn);
        Assert.Equal(1, fake.SearchCallCount);
    }

    [Fact]
    public async Task FindUserDn_FallsThroughToUpn_WhenSamEmpty()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=alice)", MakeResponse());
        fake.OnSearch(
            "(userPrincipalName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "alice" })!;
        var dn = await task;

        Assert.Equal("CN=Alice,OU=Users,DC=corp,DC=example,DC=com", dn);
        Assert.Equal(2, fake.SearchCallCount);
    }

    [Fact]
    public async Task FindUserDn_AllAttributesMiss_ReturnsNull()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=ghost)",     MakeResponse());
        fake.OnSearch("(userPrincipalName=ghost)",  MakeResponse());
        fake.OnSearch("(mail=ghost)",               MakeResponse());

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "ghost" })!;
        var dn = await task;

        Assert.Null(dn);
        Assert.Equal(3, fake.SearchCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_HappyPath_ReturnsNull()
    {
        var (sut, fake) = Build();
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        // ModifyResponse has no public parameterless ctor; use MakeModifyResponse helper.
        fake.OnModify(
            "CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            MakeModifyResponse(ResultCode.Success));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.Null(result);
        Assert.Equal(1, fake.BindCallCount);
        Assert.Equal(1, fake.ModifyCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_UserNotFound_ReturnsUserNotFound()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=ghost)",     MakeResponse());
        fake.OnSearch("(userPrincipalName=ghost)",  MakeResponse());
        fake.OnSearch("(mail=ghost)",               MakeResponse());

        var result = await sut.PerformPasswordChangeAsync("ghost", "any", "new");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.UserNotFound, result!.ErrorCode);
        Assert.Equal(0, fake.ModifyCallCount);
    }
}
