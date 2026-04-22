namespace PassReset.Web.Services;

/// <summary>
/// Test-seam over <see cref="System.Diagnostics.Process"/> for the admin UI's
/// "Recycle App Pool" action. The real implementation invokes <c>appcmd.exe</c>.
/// </summary>
public interface IProcessRunner
{
    ProcessRunResult Run(string fileName, IReadOnlyList<string> args, TimeSpan? timeout = null);
}

public sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);
