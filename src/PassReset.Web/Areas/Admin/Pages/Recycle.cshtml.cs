using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PassReset.Web.Services;

namespace PassReset.Web.Areas.Admin.Pages;

public sealed class RecycleModel : PageModel
{
    private const string AppPoolName = "PassResetPool";

    private readonly IProcessRunner _runner;

    public RecycleModel(IProcessRunner runner)
    {
        _runner = runner;
    }

    public string? Output { get; private set; }
    public int? ExitCode { get; private set; }

    public void OnGet() { }

    public IActionResult OnPost()
    {
        var appcmdPath = Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
            "System32", "inetsrv", "appcmd.exe");
        var args = new[] { "recycle", "apppool", $"/apppool.name:{AppPoolName}" };

        var result = _runner.Run(appcmdPath, args, TimeSpan.FromSeconds(30));
        Output = (result.StdOut + result.StdErr).Trim();
        ExitCode = result.ExitCode;
        return Page();
    }
}
