using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PassReset.Web.Services.Configuration;

namespace PassReset.Web.Areas.Admin.Pages;

public sealed class RecaptchaModel : PageModel
{
    private readonly IAppSettingsEditor _editor;
    private readonly ISecretStore _secrets;

    public RecaptchaModel(IAppSettingsEditor editor, ISecretStore secrets)
    {
        _editor = editor;
        _secrets = secrets;
    }

    [BindProperty] public RecaptchaInput Input { get; set; } = new();

    public void OnGet()
    {
        var snap = _editor.Load();
        Input = new RecaptchaInput { Enabled = snap.Recaptcha.Enabled, SiteKey = snap.Recaptcha.SiteKey };
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        var snap = _editor.Load();
        _editor.Save(snap with { Recaptcha = new RecaptchaPublicSection(Input.Enabled, Input.SiteKey ?? "") });

        if (!string.IsNullOrEmpty(Input.NewPrivateKey))
        {
            var bundle = _secrets.Load();
            _secrets.Save(bundle with { RecaptchaPrivateKey = Input.NewPrivateKey });
        }

        TempData["Success"] = "reCAPTCHA settings saved. Recycle the app pool to apply.";
        return RedirectToPage();
    }

    public sealed class RecaptchaInput
    {
        public bool Enabled { get; set; }
        [StringLength(255)] public string? SiteKey { get; set; }
        [StringLength(255)] public string? NewPrivateKey { get; set; }
    }
}
