using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Admin;

public class RegistrationsModel(RegistrationRepository registrations) : AppPage
{
    public string Status { get; private set; } = RegistrationStatus.Pending;
    public List<RegistrationRequest> Items { get; private set; } = new();

    public void OnGet(string? status)
    {
        Status = status is RegistrationStatus.Approved or RegistrationStatus.Rejected or "All" ? status : RegistrationStatus.Pending;
        Items = registrations.List(Status == "All" ? null : Status);
    }
}
