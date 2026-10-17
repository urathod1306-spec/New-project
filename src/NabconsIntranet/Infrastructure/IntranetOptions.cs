namespace NabconsIntranet.Infrastructure;

/// <summary>Settings read from the "Intranet" section of appsettings.json.</summary>
public sealed class IntranetOptions
{
    public string OrganisationName { get; set; } = "NABARD Consultancy Services";
    public string ShortName { get; set; } = "NABCONS";
    public string SiteTitle { get; set; } = "NABCONS Intranet";
    public string Tagline { get; set; } = "";
    public string FooterText { get; set; } = "";

    /// <summary>"Local" (intranet user accounts) or "Windows" (Windows Authentication / Active Directory).</summary>
    public string AuthenticationMode { get; set; } = "Local";
    public string InitialAdminPassword { get; set; } = "Admin@12345";
    /// <summary>Windows mode: DOMAIN\user names that always receive the Administrator role.</summary>
    public List<string> WindowsAdministrators { get; set; } = new();
    /// <summary>Windows mode: AD group name (DOMAIN\Group) to intranet role.</summary>
    public Dictionary<string, string> WindowsGroupRoles { get; set; } = new();
    public bool WindowsAutoProvisionStaff { get; set; } = true;

    public bool SeedDemoData { get; set; } = true;
    public bool ShowDemoAccountsOnLogin { get; set; } = true;
    /// <summary>Allow staff to register themselves (local accounts only); an administrator approves each request.</summary>
    public bool SelfRegistrationEnabled { get; set; } = true;
    public bool SelfRegistrationAvailable => SelfRegistrationEnabled && !IsWindowsAuth;

    /// <summary>A vertical change made on My Profile waits for an administrator (vertical controls access to Business Leads).</summary>
    public bool VerticalChangeRequiresApproval { get; set; } = true;
    /// <summary>Business Development vertical: its Master Lists ID (Administration → Master lists), or 0 to match by name.</summary>
    public long BusinessDevelopmentVerticalId { get; set; } = 0;
    public string BusinessDevelopmentVerticalName { get; set; } = "BDU";
    /// <summary>If set (e.g. "nabcons.in"), official e-mail addresses entered on My Profile must use this domain.</summary>
    public string OfficialEmailDomain { get; set; } = "";
    public int MaxPhotoMB { get; set; } = 2;
    public int MaxLeadFileMB { get; set; } = 10;
    public int MaxLeadTotalMB { get; set; } = 25;

    public string DataFolder { get; set; } = "data";
    public string DocumentsFolder { get; set; } = "documents";
    public string BackupFolder { get; set; } = "backups";
    public string LogFolder { get; set; } = "logs";
    public string SecondaryBackupFolder { get; set; } = "";

    public string DailyBackupTime { get; set; } = "19:30";
    public int BackupRetentionDays { get; set; } = 30;

    public int MaxUploadMB { get; set; } = 20;
    public List<string> AllowedUploadExtensions { get; set; } = new();

    public int DisplayUtcOffsetMinutes { get; set; } = 330;
    public int SessionTimeoutMinutes { get; set; } = 30;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    public bool IsWindowsAuth => string.Equals(AuthenticationMode, "Windows", StringComparison.OrdinalIgnoreCase);

    public string ContentRoot { get; private set; } = "";
    public string DataPath { get; private set; } = "";
    public string DatabasePath => Path.Combine(DataPath, "intranet.db");
    public string DocumentsPath { get; private set; } = "";
    public string BackupPath { get; private set; } = "";
    public string LogPath { get; private set; } = "";
    /// <summary>data\uploads — staff photos (inside the data folder, outside the web root; included in backups).</summary>
    public string UploadsPath => Path.Combine(DataPath, "uploads");
    public string PhotosPath => Path.Combine(UploadsPath, "photos");
    public long MaxUploadBytes => MaxUploadMB * 1024L * 1024L;

    public void Resolve(string contentRoot)
    {
        ContentRoot = contentRoot;
        DataPath = Full(DataFolder);
        DocumentsPath = Full(DocumentsFolder);
        BackupPath = Full(BackupFolder);
        LogPath = Full(LogFolder);
        if (AllowedUploadExtensions.Count == 0)
            AllowedUploadExtensions = new() { ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".png", ".jpg", ".jpeg" };
        AllowedUploadExtensions = AllowedUploadExtensions.Select(e => e.Trim().ToLowerInvariant()).Distinct().ToList();
        foreach (var dir in new[] { DataPath, DocumentsPath, BackupPath, LogPath, PhotosPath })
            Directory.CreateDirectory(dir);
    }

    private string Full(string folder) =>
        Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(ContentRoot, folder));
}
