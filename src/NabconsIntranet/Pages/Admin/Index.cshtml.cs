using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class IndexModel(Db db, IntranetOptions o, BackupService backups, RegistrationRepository registrations) : AppPage
{
    public long Users, Staff, Circulars, Requests, AuditEntries;
    public long DbBytes, DocBytes;
    public BackupInfo? LastBackup;
    public long PendingRegistrations;
    public IntranetOptions Options => o;

    public void OnGet()
    {
        Users = db.Scalar<long>("SELECT COUNT(*) FROM Users WHERE IsActive = 1");
        Staff = db.Scalar<long>("SELECT COUNT(*) FROM Staff");
        Circulars = db.Scalar<long>("SELECT COUNT(*) FROM Circulars");
        Requests = db.Scalar<long>("SELECT COUNT(*) FROM Requests");
        AuditEntries = db.Scalar<long>("SELECT COUNT(*) FROM AuditLog");
        DbBytes = System.IO.File.Exists(o.DatabasePath) ? new FileInfo(o.DatabasePath).Length : 0;
        DocBytes = System.IO.Directory.Exists(o.DocumentsPath) ? System.IO.Directory.EnumerateFiles(o.DocumentsPath, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
        LastBackup = backups.List().FirstOrDefault();
        PendingRegistrations = registrations.PendingCount();
    }
}
