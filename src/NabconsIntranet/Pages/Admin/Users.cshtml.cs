using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Admin;

public class UsersModel(UserRepository users) : AppPage
{
    public List<AppUser> Items { get; private set; } = new();
    public void OnGet() => Items = users.All();
}
