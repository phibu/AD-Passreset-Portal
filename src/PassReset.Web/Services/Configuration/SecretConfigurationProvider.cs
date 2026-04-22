using Microsoft.Extensions.Configuration;

namespace PassReset.Web.Services.Configuration;

/// <summary>
/// <see cref="ConfigurationProvider"/> that decrypts <see cref="SecretBundle"/> fields
/// and maps them to canonical configuration keys for password change, SMTP, and reCAPTCHA settings.
/// </summary>
internal sealed class SecretConfigurationProvider : ConfigurationProvider
{
    private readonly Func<ISecretStore> _secretStoreFactory;

    public SecretConfigurationProvider(Func<ISecretStore> secretStoreFactory)
    {
        _secretStoreFactory = secretStoreFactory;
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var store = _secretStoreFactory();
            var bundle = store.Load();

            if (bundle is not null)
            {
                if (bundle.LdapPassword is not null)
                    data["PasswordChangeOptions:LdapPassword"] = bundle.LdapPassword;

                if (bundle.ServiceAccountPassword is not null)
                    data["PasswordChangeOptions:ServiceAccountPassword"] = bundle.ServiceAccountPassword;

                if (bundle.SmtpPassword is not null)
                    data["SmtpSettings:Password"] = bundle.SmtpPassword;

                if (bundle.RecaptchaPrivateKey is not null)
                    data["ClientSettings:Recaptcha:PrivateKey"] = bundle.RecaptchaPrivateKey;
            }
        }
        catch
        {
            // If secrets cannot be loaded, proceed with empty data.
            // Configuration sources later in the pipeline can provide values.
        }

        Data = data;
    }
}
