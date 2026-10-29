using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Board;

public class IndexModel(BoardRepository board) : AppPage
{
    public List<BoardPost> Pinned { get; private set; } = new();
    public Paged<BoardPost> Result { get; private set; } = new();
    public BoardOptions Options => board.Options;
    public string? Error { get; private set; }
    public string? Draft { get; private set; }

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (!board.Options.Enabled) context.Result = NotFound();     // MessageBoard:Enabled = false
        base.OnPageHandlerExecuting(context);
    }

    private void Load(int page)
    {
        Pinned = page <= 1 ? board.Pinned() : new();
        Result = board.Page(page);
    }

    public void OnGet(int page = 1)
    {
        Load(page);
        board.MarkSeen(User.UserId());
    }

    public string PageUrl(int page) => page > 1 ? $"/Board?page={page}" : "/Board";

    public IActionResult OnPostCreate(List<IFormFile> photos)
    {
        var body = Request.Form["body"].ToString();
        try
        {
            board.Create(body, photos, User);
            Flash("Your message is on the board.");
            return RedirectToPage();
        }
        catch (WorkflowException ex)
        {
            Error = ex.Message;
            Draft = body;
            Load(1);
            return Page();
        }
    }

    private IActionResult Act(Action a, string done, int page)
    {
        try { a(); Flash(done); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return Redirect(PageUrl(page));
    }

    public IActionResult OnPostDelete(long id, string? reason, int page = 1) => Act(() => board.Delete(id, reason, User), "The post has been removed.", page);
    public IActionResult OnPostPin(long id) => Act(() => board.SetPinned(id, true, User), "Post pinned to the top of the board.", 1);
    public IActionResult OnPostUnpin(long id, int page = 1) => Act(() => board.SetPinned(id, false, User), "Post unpinned.", page);
}
