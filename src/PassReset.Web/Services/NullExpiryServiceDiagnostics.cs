namespace PassReset.Web.Services;

/// <summary>
/// Null-object implementation of <see cref="IExpiryServiceDiagnostics"/>. Wired into DI
/// when the expiry notification service is disabled or when running under the debug
/// provider. Reports <c>IsEnabled=false</c> so the health controller's expiryService
/// check returns "not-enabled" (treated as neutral by the aggregate rollup).
/// </summary>
internal sealed class NullExpiryServiceDiagnostics : IExpiryServiceDiagnostics
{
    public bool IsEnabled => false;
    public DateTimeOffset? LastTickUtc => null;
}
