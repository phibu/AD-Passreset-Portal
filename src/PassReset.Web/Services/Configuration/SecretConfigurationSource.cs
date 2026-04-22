using Microsoft.Extensions.Configuration;

namespace PassReset.Web.Services.Configuration;

/// <summary>
/// <see cref="IConfigurationSource"/> that builds a <see cref="SecretConfigurationProvider"/>
/// to decrypt and surface secrets from a <see cref="SecretStore"/>.
/// </summary>
public sealed class SecretConfigurationSource : IConfigurationSource
{
    private readonly Func<ISecretStore> _secretStoreFactory;

    public SecretConfigurationSource(Func<ISecretStore> secretStoreFactory)
    {
        _secretStoreFactory = secretStoreFactory;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SecretConfigurationProvider(_secretStoreFactory);
    }
}
