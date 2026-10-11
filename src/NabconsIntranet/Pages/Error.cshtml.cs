using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NabconsIntranet.Pages;

[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;
    public void OnGet(int? code) => Code = code ?? 500;
    public void OnPost(int? code) => Code = code ?? 500;
}
