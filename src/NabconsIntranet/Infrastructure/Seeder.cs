using System.Security.Claims;
using NabconsIntranet.Data;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// First-run setup. Always ensures an administrator exists and master lists are populated.
/// When SeedDemoData is true (default for the pilot) it also loads clearly-marked SYNTHETIC
/// staff, circulars and requests so the system can be demonstrated immediately.
/// </summary>
public sealed class Seeder(Db db, IntranetOptions o, UserRepository users, MasterRepository masters, StaffRepository staff,
    CircularRepository circulars, NoticeRepository notices, RequestWorkflow workflow, FileStore files, AuditService audit, Fmt fmt,
    ILogger<Seeder> log)
{
    public const string DemoPassword = "Nabcons@2026";

    public static readonly (string user, string name, string roles, string vertical, string note)[] DemoUsers =
    {
        ("admin", "System Administrator", "Staff,Administrator", "IT & Digital", "Administrator - users, masters, backups"),
        ("publisher", "Priya Kulkarni", "Staff,Publisher", "HR & Administration", "Publisher - circulars and notices"),
        ("approver1", "Rajesh Iyer", "Staff,Approver", "HR & Administration", "Approver - Reviewing Officer"),
        ("approver2", "Anita Deshmukh", "Staff,Approver", "Finance & Accounts", "Approver - Sanctioning Authority"),
        ("staff", "Vikram Singh", "Staff", "Rural Infrastructure", "Staff - browse and submit requests"),
        ("auditor", "Meera Nair", "Staff,Auditor", "Finance & Accounts", "Auditor - read-only audit history"),
        ("bdu", "Rohan Kapoor", "Staff", "BDU", "BDU vertical - business lead pipeline"),
        ("coo", "Kavita Rao", "Staff,LeadViewer", "HR & Administration", "LeadViewer (COO) - sees all business leads"),
    };

    /// <summary>Capitals of all 28 states and 8 union territories (Chandigarh serves Punjab, Haryana and the UT; J&amp;K has Srinagar and Jammu).</summary>
    public static readonly string[] StateAndUtCapitals =
    {
        "Agartala", "Aizawl", "Amaravati", "Bengaluru", "Bhopal", "Bhubaneswar", "Chandigarh", "Chennai", "Daman", "Dehradun", "Dispur", "Gandhinagar", "Gangtok", "Hyderabad", "Imphal", "Itanagar", "Jaipur", "Jammu", "Kavaratti", "Kohima", "Kolkata", "Leh", "Lucknow", "Mumbai", "New Delhi", "Panaji", "Patna", "Puducherry", "Raipur", "Ranchi", "Shillong", "Shimla", "Srinagar", "Sri Vijaya Puram (Port Blair)", "Thiruvananthapuram"
    };

    public void Run()
    {
        AuditService.AmbientActor.Value = "system (first-run setup)";
        try
        {
            SeedMasters();
            EnsureAdmin();
            if (o.SeedDemoData && staff.Count() == 0 && circulars.CountPublished() == 0)
            {
                log.LogInformation("Loading synthetic demo data");
                SeedDemo();
            }
        }
        finally
        {
            AuditService.AmbientActor.Value = null;
        }
    }

    private void SeedMasters()
    {
        void Ensure(string type, params string[] names)
        {
            if (masters.List(type, activeOnly: false).Count > 0) return;
            for (int i = 0; i < names.Length; i++) masters.Add(type, names[i], i);
            audit.Log("Master.Seeded", "MasterItems", type, string.Join("; ", names));
        }
        Ensure(MasterTypes.Vertical, "Agri & Allied", "Climate Change & Sustainability", "Rural Infrastructure", "Financial Inclusion",
            "Microfinance & Livelihoods", "Watershed & Natural Resources", "IT & Digital", "Finance & Accounts", "HR & Administration", "BDU");
        var offices = new[] { "Mumbai (Head Office)", "New Delhi", "Lucknow", "Bhopal", "Pune", "Hyderabad", "Bengaluru",
            "Kolkata", "Guwahati", "Chennai", "Jaipur" };
        Ensure(MasterTypes.Location, offices.Concat(StateAndUtCapitals.Where(c =>
            !offices.Any(o => o.Equals(c, StringComparison.OrdinalIgnoreCase) || o.StartsWith(c + " (", StringComparison.OrdinalIgnoreCase)))).ToArray());
        Ensure(MasterTypes.CircularCategory, "HR Circulars", "Administrative Orders", "IT & Security Advisories");
        Ensure(MasterTypes.RequestCategory, "Office Administration", "Procurement (below delegated limit)", "Travel / Tour Approval",
            "Training / Workshop Nomination", "IT Equipment / Access", "Other");
    }

    private void EnsureAdmin()
    {
        if (o.IsWindowsAuth) return; // Windows mode: administrators come from WindowsAdministrators in appsettings.json
        if (users.CountActiveAdmins() > 0) return;
        var existing = users.GetByUsername("admin");
        if (existing == null)
        {
            var id = users.Create(new AppUser
            {
                Username = "admin", DisplayName = "System Administrator", Roles = "Staff,Administrator",
                Vertical = "IT & Digital", PasswordHash = PasswordHasher.Hash(o.InitialAdminPassword), MustChangePassword = true,
            }, "system");
            audit.Log("User.Created", "User", id, "Initial administrator account 'admin' (must change password at first sign-in)");
        }
    }

    private ClaimsPrincipal Principal(string username)
    {
        var u = users.GetByUsername(username)!;
        return new ClaimsPrincipal(IdentityBuilder.BuildIdentity(u, "seed"));
    }

    private void SeedDemo()
    {
        // --- demo users (local mode only) ---
        if (!o.IsWindowsAuth)
        {
            foreach (var d in DemoUsers.Where(d => d.user != "admin"))
            {
                if (users.GetByUsername(d.user) != null) continue;
                var id = users.Create(new AppUser
                {
                    Username = d.user, DisplayName = d.name, Roles = d.roles, Vertical = d.vertical,
                    Email = d.user + "@nabcons.local", PasswordHash = PasswordHasher.Hash(DemoPassword),
                }, "system");
                audit.Log("User.Created", "User", id, $"Demo account {d.user} ({d.roles})");
            }
            // demo admin keeps a known password so the demo can be run end-to-end
            var admin = users.GetByUsername("admin");
            if (admin != null && o.ShowDemoAccountsOnLogin)
                users.SetPassword(admin.Id, DemoPassword, mustChange: false, "system");
        }

        // --- 25 synthetic staff ---
        var people = new (string name, string desig, string vertical, string loc)[]
        {
            ("Aarav Sharma", "Managing Director", "HR & Administration", "Mumbai (Head Office)"),
            ("Kavita Rao", "Chief Operating Officer", "HR & Administration", "Mumbai (Head Office)"),
            ("Rajesh Iyer", "Deputy General Manager", "HR & Administration", "Mumbai (Head Office)"),
            ("Anita Deshmukh", "Chief Financial Officer", "Finance & Accounts", "Mumbai (Head Office)"),
            ("Priya Kulkarni", "Assistant General Manager", "HR & Administration", "Mumbai (Head Office)"),
            ("Meera Nair", "Manager - Internal Audit", "Finance & Accounts", "Mumbai (Head Office)"),
            ("Vikram Singh", "Senior Consultant", "Rural Infrastructure", "New Delhi"),
            ("Sunita Verma", "Senior Consultant", "Agri & Allied", "Lucknow"),
            ("Arjun Mehta", "Consultant", "Climate Change & Sustainability", "Pune"),
            ("Deepa Pillai", "Consultant", "Financial Inclusion", "Chennai"),
            ("Rohit Gupta", "Project Manager", "Rural Infrastructure", "Bhopal"),
            ("Neha Joshi", "Consultant", "Watershed & Natural Resources", "Jaipur"),
            ("Sanjay Patil", "Senior Consultant", "Microfinance & Livelihoods", "Pune"),
            ("Farah Khan", "Manager - IT", "IT & Digital", "Mumbai (Head Office)"),
            ("Manoj Das", "Regional Coordinator", "Agri & Allied", "Kolkata"),
            ("Lakshmi Reddy", "Consultant", "Climate Change & Sustainability", "Hyderabad"),
            ("Harish Bora", "Regional Coordinator", "Watershed & Natural Resources", "Guwahati"),
            ("Pooja Agarwal", "Accounts Officer", "Finance & Accounts", "Mumbai (Head Office)"),
            ("Kiran Shetty", "Consultant", "Financial Inclusion", "Bengaluru"),
            ("Amit Chauhan", "Project Associate", "Rural Infrastructure", "New Delhi"),
            ("Swati Mishra", "Project Associate", "Agri & Allied", "Lucknow"),
            ("Naveen Kumar", "Systems Officer", "IT & Digital", "Hyderabad"),
            ("Rekha Menon", "HR Officer", "HR & Administration", "Mumbai (Head Office)"),
            ("Gaurav Tiwari", "Consultant", "Microfinance & Livelihoods", "Bhopal"),
            ("Ishita Banerjee", "Project Associate", "Climate Change & Sustainability", "Kolkata"),
            ("Rohan Kapoor", "Manager - Business Development", "BDU", "Mumbai (Head Office)"),
        };
        for (int i = 0; i < people.Length; i++)
        {
            var p = people[i];
            var parts = p.name.ToLowerInvariant().Split(' ');
            staff.Upsert(new StaffMember
            {
                EmployeeCode = $"NC{1001 + i}",
                FullName = p.name,
                Designation = p.desig,
                Vertical = p.vertical,
                Location = p.loc,
                OfficialEmail = $"{parts[0]}.{parts[^1]}@nabcons.local",
                Extension = (2100 + i * 3).ToString(),
                Mobile = i % 4 == 0 ? $"90000 0{1000 + i}" : null,
                ShowMobile = i % 4 == 0,
                ActiveFrom = "2024-04-01",
            }, "system (demo seed)");
        }
        audit.Log("Staff.Imported", "Staff", null, $"{people.Length} synthetic staff rows loaded (demo seed)");
        // link demo accounts to their (synthetic) directory entries so My Profile works out of the box
        foreach (var d in DemoUsers)
        {
            var u = users.GetByUsername(d.user);
            var s = staff.All().FirstOrDefault(x => x.FullName == d.name);
            if (u != null && s != null && u.StaffId == null) db.Exec("UPDATE Users SET StaffId = @sid WHERE Id = @uid", new { sid = s.StaffId, uid = u.Id });
        }

        // --- 10 synthetic circulars across 3 categories ---
        var y = fmt.Today.Year;
        var list = new (string no, string title, string cat, string vert, string date, string kw, string synopsis, string vis)[]
        {
            ($"NABCONS/HR/{y}/01", "Revised leave application procedure", "HR Circulars", "HR & Administration", $"{y}-01-15",
                "leave, attendance, HR", "Leave applications are to be routed through the reporting officer at least three working days in advance. Supersedes earlier procedure.", Visibility.AllStaff),
            ($"NABCONS/HR/{y}/02", "Office timings during summer months", "HR Circulars", "HR & Administration", $"{y}-03-20",
                "office timings, attendance", "Office hours from April to June are 09:00 to 17:30 at all locations.", Visibility.AllStaff),
            ($"NABCONS/HR/{y}/03", "Nomination for in-house training programmes", "HR Circulars", "HR & Administration", $"{y}-05-02",
                "training, capacity building, nomination", "Verticals may nominate up to two officers per quarter for in-house training.", Visibility.AllStaff),
            ($"NABCONS/ADM/{y}/01", "Procedure for booking conference rooms", "Administrative Orders", "HR & Administration", $"{y}-02-10",
                "conference room, booking, administration", "Conference rooms at Head Office are booked through the Administration desk (Ext. 2105).", Visibility.AllStaff),
            ($"NABCONS/ADM/{y}/02", "Tour and travel claim submission timelines", "Administrative Orders", "Finance & Accounts", $"{y}-04-08",
                "travel, tour, claims, reimbursement", "Travel claims are to be submitted within 15 days of completion of tour with supporting documents.", Visibility.AllStaff),
            ($"NABCONS/ADM/{y}/03", "Stationery requisition process", "Administrative Orders", "HR & Administration", $"{y}-06-12",
                "stationery, requisition, procurement", "Monthly stationery requisitions are consolidated by each vertical and sent by the 5th of every month.", Visibility.AllStaff),
            ($"NABCONS/ADM/{y}/04", "Field project expense approval matrix", "Administrative Orders", "Rural Infrastructure", $"{y}-07-01",
                "delegation, approval matrix, field expenses", "Approval levels for field project expenses within the Rural Infrastructure vertical.", Visibility.Vertical),
            ($"NABCONS/IT/{y}/01", "Password and screen-lock policy", "IT & Security Advisories", "IT & Digital", $"{y}-01-28",
                "password, screen lock, cyber security", "Passwords must be at least 10 characters and screens must lock after 5 minutes of inactivity.", Visibility.AllStaff),
            ($"NABCONS/IT/{y}/02", "Use of removable media on office PCs", "IT & Security Advisories", "IT & Digital", $"{y}-04-22",
                "USB, removable media, data protection", "Removable media may be used only after scanning and only with IT approval.", Visibility.AllStaff),
            ($"NABCONS/IT/{y}/03", "Phishing awareness advisory", "IT & Security Advisories", "IT & Digital", $"{y}-08-05",
                "phishing, email, cyber security, awareness", "Do not open unexpected attachments. Report suspicious messages to the IT desk.", Visibility.AllStaff),
        };
        var ids = new List<long>();
        foreach (var c in list)
        {
            var id = circulars.Create(new Circular
            {
                CircularNumber = c.no, Title = c.title, Category = c.cat, IssuingVertical = c.vert, IssueDate = c.date,
                EffectiveDate = c.date, Keywords = c.kw, Synopsis = c.synopsis, Visibility = c.vis, CreatedBy = "publisher",
            });
            var pdf = SimplePdf.Create("NABCONS - " + c.title, new[]
            {
                "SAMPLE / SYNTHETIC DEMONSTRATION DOCUMENT - NOT AN OFFICIAL CIRCULAR",
                "",
                "Circular No.: " + c.no,
                "Date: " + c.date,
                "Category: " + c.cat,
                "Issued by: " + c.vert,
                "",
                c.synopsis,
                "",
                "This document was generated automatically to demonstrate the NABCONS Intranet circular repository.",
                "Replace it with genuine circulars after the pilot is approved.",
            });
            var stored = files.SaveBytes(pdf, c.no.Replace('/', '-') + ".pdf", "circulars");
            circulars.AddFile(id, stored, "publisher");
            ids.Add(id);
        }
        // supersession demo: HR/03 (new) supersedes nothing; create HR/04 superseding HR/01
        var supersedingId = circulars.Create(new Circular
        {
            CircularNumber = $"NABCONS/HR/{y}/04", Title = "Leave application procedure (revised)", Category = "HR Circulars",
            IssuingVertical = "HR & Administration", IssueDate = $"{y}-09-01", EffectiveDate = $"{y}-09-15",
            Keywords = "leave, attendance, HR, revised", Synopsis = "Leave applications are now routed through the intranet request form. This circular supersedes the January procedure.",
            Visibility = Visibility.AllStaff, SupersedesId = ids[0], CreatedBy = "publisher",
        });
        circulars.AddFile(supersedingId, files.SaveBytes(SimplePdf.Create("NABCONS - Leave application procedure (revised)", new[]
        {
            "SAMPLE / SYNTHETIC DEMONSTRATION DOCUMENT - NOT AN OFFICIAL CIRCULAR", "", $"Circular No.: NABCONS/HR/{y}/04",
            $"Supersedes: NABCONS/HR/{y}/01", "", "Leave applications are now routed through the intranet request form.",
        }), $"NABCONS-HR-{y}-04.pdf", "circulars"), "publisher");

        foreach (var id in ids) circulars.Publish(id, "publisher");
        circulars.Publish(supersedingId, "publisher");
        circulars.Withdraw(ids[5], "Replaced by the consolidated procurement process (demo of a withdrawn circular).", "publisher");
        audit.Log("Circular.Seeded", "Circulars", null, $"{ids.Count + 1} synthetic circulars published; 1 superseded, 1 withdrawn (demo seed)");

        notices.Add(new Notice
        {
            Title = "Welcome to the NABCONS Intranet pilot", IsImportant = true,
            Body = "This is a pilot running entirely inside the NABARD LAN. All staff, circulars and requests shown are synthetic sample data.",
            StartsOn = fmt.Today.AddDays(-1).ToString("yyyy-MM-dd"), PostedBy = "Priya Kulkarni", PostedAt = Db.Now(),
        });
        notices.Add(new Notice
        {
            Title = "Quarterly backup restore test", Body = "The administrator will test a full restore from backup on the last working day of the quarter.",
            StartsOn = fmt.Today.AddDays(-1).ToString("yyyy-MM-dd"), PostedBy = "Priya Kulkarni", PostedAt = Db.Now(),
        });

        // --- sample requests with genuine workflow history (local mode only: needs demo users) ---
        if (o.IsWindowsAuth) return;
        var staffP = Principal("staff");
        var r1 = Principal("approver1");
        var r2 = Principal("approver2");
        long reviewer = r1.UserId(), approver = r2.UserId();

        long NewReq(string subject, string cat, double? amount, string details)
        {
            AuditService.AmbientActor.Value = "staff";
            return workflow.CreateDraft(new ApprovalRequest
            {
                Subject = subject, RequestCategory = cat, Amount = amount, Details = details,
                RequiredBy = fmt.Today.AddDays(14).ToString("yyyy-MM-dd"), ReviewerUserId = reviewer, ApproverUserId = approver,
            }, staffP);
        }
        void Act(long id, string action, ClaimsPrincipal who, string? remarks = null)
        {
            AuditService.AmbientActor.Value = who.UserName();
            var r = workflow.Get(id)!;
            workflow.Execute(id, action, remarks, r.RowVersion, who);
        }

        var a = NewReq("Purchase of two document scanners for Delhi office", "Procurement (below delegated limit)", 38500,
            "Two flatbed scanners are needed to digitise project files at the New Delhi office. Three quotations obtained; lowest is attached in the physical file.");
        Act(a, "Submit", staffP);
        Act(a, "Recommend", r1, "Requirement verified. Recommended.");
        Act(a, "Approve", r2, "Sanctioned within delegated powers.");

        var b = NewReq("Tour to Lucknow for project review meeting", "Travel / Tour Approval", 12000,
            "Two-day tour to Lucknow to attend the quarterly project review with the state implementing agency.");
        Act(b, "Submit", staffP);
        Act(b, "Recommend", r1, "Recommended.");
        Act(b, "Reject", r2, "Meeting is being held by video conference; physical travel not required.");

        var c2 = NewReq("Nomination for GIS mapping workshop", "Training / Workshop Nomination", null,
            "Requesting nomination to the three-day GIS mapping workshop for watershed projects.");
        Act(c2, "Submit", staffP);

        NewReq("Additional monitor for data analysis work", "IT Equipment / Access", 9500,
            "A second monitor would help with spreadsheet-heavy monitoring and evaluation work.");
        AuditService.AmbientActor.Value = "system (first-run setup)";
    }
}
