using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PassReset.Web.Services.Configuration;

namespace PassReset.Web.Areas.Admin.Pages;

public sealed class SiemModel : PageModel
{
    private readonly IAppSettingsEditor _editor;

    public SiemModel(IAppSettingsEditor editor)
    {
        _editor = editor;
    }

    [BindProperty] public SiemInput Input { get; set; } = new();

    public void OnGet()
    {
        var snap = _editor.Load();
        Input = new SiemInput
        {
            Enabled = snap.Siem.Enabled,
            Host = snap.Siem.Host,
            Port = snap.Siem.Port,
            Protocol = snap.Siem.Protocol,
        };
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        var snap = _editor.Load();
        _editor.Save(snap with
        {
            Siem = new SiemSyslogSection(Input.Enabled, Input.Host ?? "", Input.Port, Input.Protocol ?? "Udp")
        });
        TempData["Success"] = "SIEM settings saved. Recycle the app pool to apply.";
        return RedirectToPage();
    }

    public sealed class SiemInput
    {
        public bool Enabled { get; set; }
        [StringLength(255)] public string? Host { get; set; }
        [Range(1, 65535)] public int Port { get; set; } = 514;
        public string? Protocol { get; set; } = "Udp";
    }
}
