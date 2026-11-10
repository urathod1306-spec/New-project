using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Inward;

/// <summary>
/// The inward form (DD-08 §6.3): the registry inwards for its offices; any user may self-inward (setting). Warnings
/// (possible duplicates) are shown with "Confirm and save"; a duplicate invoice needs "This is not a duplicate" and a reason.
/// </summary>
public class NewModel(InwardRepository repo, InwardWorkflow workflow, InwardOptions o, MasterRepository masters, Fmt fmt) : InwardPage
{
    [BindProperty] public InwardInput Input { get; set; } = new();
    public InwardFormModel Form { get; private set; } = new();
    public List<string> Warnings { get; private set; } = new();
    public bool Blocked { get; private set; }
    public string? Error { get; private set; }
    public bool SelfInward { get; private set; }

    private IActionResult? Prepare()
    {
        var mine = repo.Offices().Where(x => Access.IsOperatorOf(User, x.Id)).ToList();
        SelfInward = mine.Count == 0;
        if (SelfInward && !o.SelfInwardEnabled) return NotFound();
        var offices = mine;
        if (SelfInward) offices = repo.OwnOffice(User) is { } own ? new() { own } : repo.Offices();
        if (offices.Count == 0) { Error = "No office is set up yet. Ask the Administrator to add offices on Master lists."; }
        Form = new InwardFormModel
        {
            Input = Input, SelfInward = SelfInward, Offices = offices, Users = repo.ActiveUsers(), Categories = masters.Names(MasterTypes.InwardCategory),
            ProjectCodes = repo.ProjectCodes(true), MaxBackdateDays = o.MaxBackdateDays, DefaultTerms = o.InvoiceTermsDays, Blocked = Blocked,
            ReplyDaysHint = $"{o.ReplyDueDaysNormal} days (Normal), {o.ReplyDueDaysUrgent} (Urgent), {o.ReplyDueDaysImmediate} (Immediate)",
        };
        return null;
    }

    public IActionResult OnGet(string? type)
    {
        Input = new InwardInput
        {
            ItemType = type != null && InwardType.Labels.ContainsKey(type) ? type : InwardType.Letter, Mode = "Post",
            ReceivedOn = fmt.Today.ToString("yyyy-MM-dd"), Priority = "Normal", ActionRequired = InwardAction.Reply,
        };
        return Prepare() ?? Page();
    }

    public IActionResult OnPost(bool confirmed)
    {
        Input.CopyToIds ??= new();
        try
        {
            var id = workflow.Inward(Input, User, confirmed);
            var it = repo.Item(id, User)!;
            Flash($"Inwarded as {it.Number}." + (it.ClosureType == Closure.Reminder ? " It is recorded as a reminder of the original item." : ""));
            return Redirect($"/Inward/Item/{id}");
        }
        catch (InwardConfirmException ex) { Warnings = ex.Warnings; Blocked = ex.Blocked; }
        catch (WorkflowException ex) { Error = ex.Message; }
        return Prepare() ?? Page();
    }
}
