using Microsoft.Extensions.Logging.Abstractions;
using NabconsIntranet.Data;

namespace NabconsIntranet.Infrastructure;

/// <summary>Command-line maintenance tools used by the .bat files in the release folder.</summary>
public static class Tools
{
    public static int Run(string[] args, string contentRoot)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        var o = config.GetSection("Intranet").Get<IntranetOptions>() ?? new IntranetOptions();
        o.Resolve(contentRoot);
        var db = new Db(o);
        var accessor = new HttpContextAccessor();
        var audit = new AuditService(db, accessor, NullLogger<AuditService>.Instance);

        switch (args[0].ToLowerInvariant())
        {
            case "--restore":
            {
                if (args.Length < 2) { Console.WriteLine("Usage: NabconsIntranet.exe --restore <path-to-backup.zip>"); return 1; }
                int rc;
                try { rc = BackupService.Restore(o, args[1].Trim('"'), Console.Out); }
                catch (IOException ex)
                {
                    Console.WriteLine("Restore could not move the current data: " + ex.Message);
                    Console.WriteLine("The intranet is probably still running. Close its window (or run Stop-Service.bat) and try again.");
                    return 6;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine("Access denied: " + ex.Message + "\nRun this as a user who can write to the application folder.");
                    return 7;
                }
                if (rc == 0)
                {
                    db.Initialize();
                    audit.Log("Backup.Restored", "Backup", Path.GetFileName(args[1]), "Restored from command line", Environment.UserDomainName + "\\" + Environment.UserName);
                }
                return rc;
            }
            case "--backup":
            {
                db.Initialize();
                var b = new BackupService(o, NullLogger<BackupService>.Instance).Create(audit, Environment.UserDomainName + "\\" + Environment.UserName + " (command line)");
                Console.WriteLine($"Backup created: {b.FullPath}\nSHA-256: {b.Sha256}");
                return 0;
            }
            case "--reset-admin-password":
            {
                db.Initialize();
                var users = new UserRepository(db);
                var admin = users.GetByUsername("admin");
                if (admin == null)
                {
                    var id = users.Create(new AppUser { Username = "admin", DisplayName = "System Administrator", Roles = "Staff,Administrator" }, "command line");
                    admin = users.Get(id)!;
                }
                admin.IsActive = true;
                admin.Roles = Roles.Join(admin.RoleList.Append(Roles.Administrator));
                users.Update(admin, "command line", rotateStamp: true);
                users.SetPassword(admin.Id, o.InitialAdminPassword, mustChange: true, "command line");
                audit.Log("User.PasswordReset", "User", admin.Id, "Administrator password reset from the server console", Environment.UserDomainName + "\\" + Environment.UserName);
                Console.WriteLine($"User 'admin' is active, has the Administrator role and its password is now the InitialAdminPassword from appsettings.json.\nIt must be changed at next sign-in.");
                return 0;
            }
            case "--verify-audit":
            {
                db.Initialize();
                var (ok, count, broken) = audit.Verify();
                Console.WriteLine(ok ? $"Audit log intact: {count} entries verified." : $"AUDIT LOG BROKEN at entry {broken} (after {count} valid entries).");
                return ok ? 0 : 5;
            }
            default:
                Console.WriteLine("""
                    NABCONS Intranet maintenance commands (stop the intranet first):
                      NabconsIntranet.exe                         start the intranet
                      NabconsIntranet.exe --backup                create a backup ZIP now
                      NabconsIntranet.exe --restore <file.zip>    restore a backup ZIP
                      NabconsIntranet.exe --reset-admin-password  re-enable 'admin' with the InitialAdminPassword
                      NabconsIntranet.exe --verify-audit          check the audit log hash chain
                    """);
                return 1;
        }
    }
}
