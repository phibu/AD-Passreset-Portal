using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PassReset.Web.Services.Configuration;

namespace PassReset.Web.Areas.Admin.Pages;

public sealed class LocalPolicyModel : PageModel
{
    private readonly IAppSettingsEditor _editor;

    public LocalPolicyModel(IAppSettingsEditor editor)
    {
        _editor = editor;
    }

    [BindProperty] public LocalPolicyInput Input { get; set; } = new();

    public void OnGet()
    {
        var snap = _editor.Load();
        Input = new LocalPolicyInput
        {
            BannedWordsPath = snap.LocalPolicy.BannedWordsPath,
            LocalPwnedPasswordsPath = snap.LocalPolicy.LocalPwnedPasswordsPath,
            MinBannedTermLength = snap.LocalPolicy.MinBannedTermLength,
        };
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();

        var snap = _editor.Load();
        _editor.Save(snap with
        {
            LocalPolicy = new LocalPolicySection(
                BannedWordsPath: string.IsNullOrWhiteSpace(Input.BannedWordsPath) ? null : Input.BannedWordsPath,
                LocalPwnedPasswordsPath: string.IsNullOrWhiteSpace(Input.LocalPwnedPasswordsPath) ? null : Input.LocalPwnedPasswordsPath,
                MinBannedTermLength: Input.MinBannedTermLength)
        });

        TempData["Success"] = "Local policy saved. Recycle the app pool to apply.";
        return RedirectToPage();
    }

    public sealed class LocalPolicyInput
    {
        [StringLength(500)] public string? BannedWordsPath { get; set; }
        [StringLength(500)] public string? LocalPwnedPasswordsPath { get; set; }
        [Range(1, 100)] public int MinBannedTermLength { get; set; } = 4;
    }
}
