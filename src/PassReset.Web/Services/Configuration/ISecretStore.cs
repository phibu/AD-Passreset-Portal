namespace PassReset.Web.Services.Configuration;

/// <summary>
/// Reads and writes an encrypted <c>secrets.dat</c> file containing a <see cref="SecretBundle"/>.
/// Implementations must be atomic on save (write-to-tmp + rename) and tolerate a missing file on load.
/// </summary>
public interface ISecretStore
{
    /// <summary>Loads the bundle. Returns <see cref="SecretBundle.Empty"/> if the file does not exist.</summary>
    SecretBundle Load();

    /// <summary>Serializes, encrypts, and writes the bundle atomically.</summary>
    void Save(SecretBundle bundle);
}
