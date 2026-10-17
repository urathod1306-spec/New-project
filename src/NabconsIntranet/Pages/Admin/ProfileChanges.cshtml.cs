using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

/// <summary>Vertical changes requested on My Profile (the vertical controls Business Leads access).</summary>
public class ProfileChangesModel(ProfileRepository profiles) : AppPage
{
    public string Status { get; private set; } = "Pending";
    public List<ProfileChangeRequest> Items { get; private set; } = new();

    public void OnGet(string? status)
    {
        Status = status is "Approved" or "Rejected" or "All" ? status : "Pending";
        Items = profiles.Requests(Status == "All" ? null : Status);
    }

    public IActionResult OnPostDecide(long id, bool approve, string? remarks)
    {
        try
        {
            profiles.Decide(id, approve, remarks, User.UserName());
            Flash(approve ? "Vertical change approved. It applies from the person's next page view." : "Vertical change rejected.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }
}
