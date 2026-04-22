namespace PassReset.Web.Services.Configuration;

/// <summary>
/// Reads and writes the operator-facing <c>appsettings.Production.json</c>, preserving
/// key insertion order and all unmanaged keys. Secrets are NOT handled here —
/// use <see cref="ISecretStore"/> for those.
/// </summary>
public interface IAppSettingsEditor
{
    /// <summary>Reads the current file. Missing file returns defaults.</summary>
    AppSettingsSnapshot Load();

    /// <summary>Writes the snapshot back atomically, preserving unmanaged keys.</summary>
    void Save(AppSettingsSnapshot snapshot);
}
