using System.Diagnostics;

namespace PassReset.Web.Services;

internal sealed class DefaultProcessRunner : IProcessRunner
{
    public ProcessRunResult Run(string fileName, IReadOnlyList<string> args, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        if (!proc.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new ProcessRunResult(-1, "", "Process timed out.");
        }

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        return new ProcessRunResult(proc.ExitCode, stdout, stderr);
    }
}
