using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PassReset.Web.Services.Configuration;

namespace PassReset.Web.Areas.Admin.Pages;

public sealed class GroupsModel : PageModel
{
    private readonly IAppSettingsEditor _editor;

    public GroupsModel(IAppSettingsEditor editor)
    {
        _editor = editor;
    }

    [BindProperty] public GroupsInput Input { get; set; } = new();

    public void OnGet()
    {
        var snap = _editor.Load();
        Input = new GroupsInput
        {
            AllowedAdGroupsMultiline = string.Join(Environment.NewLine, snap.Groups.AllowedAdGroups),
            RestrictedAdGroupsMultiline = string.Join(Environment.NewLine, snap.Groups.RestrictedAdGroups),
        };
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();

        var snap = _editor.Load();
        var allowed = Split(Input.AllowedAdGroupsMultiline);
        var restricted = Split(Input.RestrictedAdGroupsMultiline);
        _editor.Save(snap with { Groups = new GroupsSection(allowed, restricted) });

        TempData["Success"] = "Group filters saved. Recycle the app pool to apply.";
        return RedirectToPage();
    }

    private static string[] Split(string? multiline) =>
        (multiline ?? "")
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public sealed class GroupsInput
    {
        public string? AllowedAdGroupsMultiline { get; set; }
        public string? RestrictedAdGroupsMultiline { get; set; }
    }
}
