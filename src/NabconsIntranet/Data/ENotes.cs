using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>Settings from the "ENote" section of appsettings.json (safe defaults when absent).</summary>
public sealed class ENoteOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>"AllStaff" (approved, non-confidential notes visible to every signed-in user) or "Participants".</summary>
    public string ApprovedVisibility { get; set; } = "AllStaff";
    public int MaxBodyChars { get; set; } = 50_000;
    public int MaxFileMB { get; set; } = 10;
    public int MaxNoteTotalMB { get; set; } = 100;
    public int MaxFilesPerNote { get; set; } = 50;
    public int AgeingAlertDays { get; set; } = 7;
    public string NumberPrefix { get; set; } = "NCS/EN";
    public List<string> AllowedExtensions { get; set; } = new();

    public bool AllStaffSeeApproved => !string.Equals(ApprovedVisibility, "Participants", StringComparison.OrdinalIgnoreCase);
    public long MaxFileBytes => Math.Max(1, MaxFileMB) * 1024L * 1024L;
    public long MaxNoteBytes => Math.Max(1, MaxNoteTotalMB) * 1024L * 1024L;

    public void Resolve()
    {
        if (AllowedExtensions.Count == 0)
            AllowedExtensions = new() { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".jpg", ".jpeg", ".png", ".txt", ".csv" };
        AllowedExtensions = AllowedExtensions.Select(e => e.Trim().ToLowerInvariant()).Select(e => e.StartsWith('.') ? e : "." + e).Distinct().ToList();
        if (AllowedExtensions.Contains(".jpg") && !AllowedExtensions.Contains(".jpeg")) AllowedExtensions.Add(".jpeg");
        if (string.IsNullOrWhiteSpace(NumberPrefix)) NumberPrefix = "NCS/EN";
    }
}

public static class ENoteStates
{
    public const string Draft = "Draft";
    public const string InMovement = "InMovement";
    public const string Returned = "ReturnedToInitiator";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";
    public const string Closed = "Closed";

    public static readonly string[] All = { Draft, InMovement, Returned, Approved, Rejected, Withdrawn, Closed };
    /// <summary>States in which somebody holds the note and is expected to act.</summary>
    public static readonly string[] Pending = { InMovement, Returned };

    public static string Label(string s) => s switch
    {
        InMovement => "In movement",
        Returned => "Returned to initiator",
        _ => s,
    };

    public static string Css(string s) => s switch
    {
        Draft or Closed => "neutral",
        InMovement => "info",
        Returned => "warning",
        Approved => "success",
        Rejected or Withdrawn => "danger",
        _ => "neutral",
    };

    public static bool IsDecided(string s) => s is Approved or Rejected or Withdrawn or Closed;
    public static bool CanCloseFrom(string s) => s is Approved or Rejected or Withdrawn;
}

public sealed class ENote
{
    public long Id { get; set; }
    public string? NoteNumber { get; set; }
    public string? NumberCode { get; set; }
    public string? FinancialYear { get; set; }
    public long? Serial { get; set; }
    public string Subject { get; set; } = "";
    public string? Category { get; set; }
    public long InitiatorUserId { get; set; }
    public string InitiatorName { get; set; } = "";
    public string? InitiatorDesignation { get; set; }
    public string? InitiatorVertical { get; set; }
    public long? CurrentHolderUserId { get; set; }
    public string? CurrentHolderName { get; set; }
    public long? PreviousHolderUserId { get; set; }
    public string? HeldSince { get; set; }
    public string State { get; set; } = ENoteStates.Draft;
    public string? Outcome { get; set; }
    public string? DraftBody { get; set; }
    public long CurrentBodyVersion { get; set; }
    public long LastSeq { get; set; }
    public bool IsConfidential { get; set; }
    public bool DeclarationAccepted { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? SubmittedAt { get; set; }
    public string? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? DecidedByName { get; set; }
    public string? DecidedByDesignation { get; set; }
    public string? DecisionRemarks { get; set; }
    public string? ClosedAt { get; set; }
    public string UpdatedAt { get; set; } = "";
    public long RowVersion { get; set; }
    public string? PdfPath { get; set; }
    public string? PdfSha256 { get; set; }
    public string? PdfState { get; set; }
    public string? RecordSha256 { get; set; }
    public string? LastAgeingAlertOn { get; set; }

    public bool WasSubmitted => SubmittedAt != null;
    public string DisplayNumber => NoteNumber ?? $"Draft #{Id}";
    /// <summary>The state the archival PDF reflects (a Closed note keeps its outcome).</summary>
    public string FinalLabel => State == ENoteStates.Closed && Outcome != null ? $"Closed ({Outcome})" : ENoteStates.Label(State);
    public bool IsApprovedRecord => Outcome == ENoteStates.Approved && State is ENoteStates.Approved or ENoteStates.Closed;
}

public sealed class ENoteBodyVersion
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public long Version { get; set; }
    public string BodyText { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public string CreatedBy { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class ENoteMovement
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public long Seq { get; set; }
    public string Action { get; set; } = "";
    public long? FromUserId { get; set; }
    public string? FromName { get; set; }
    public string? FromDesignation { get; set; }
    public long? ToUserId { get; set; }
    public string? ToName { get; set; }
    public string? ToDesignation { get; set; }
    public string? FromState { get; set; }
    public string ToState { get; set; } = "";
    public string? Remarks { get; set; }
    public string Timestamp { get; set; } = "";
}

public sealed class ENoteComment
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public long Seq { get; set; }
    public long AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string? AuthorDesignation { get; set; }
    public string Text { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public sealed class ENoteAttachment
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public string StoredPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public long UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = "";
    public string UploadedAt { get; set; } = "";
    public long StageSeq { get; set; }
    public string? Stage { get; set; }
    public long? SupersededById { get; set; }
}

/// <summary>One line of the note-sheet: a comment or a movement, in order.</summary>
public sealed record ENoteSheetEntry(long Seq, string Kind, string Name, string? Designation, string Timestamp, string? Action,
    string? ToName, string? ToDesignation, string? Text, string? ToState);

public sealed record ENotePerson(long Id, string Name, string? Designation, string? Vertical, bool IsActive);

public sealed class ENoteRow
{
    public long Id { get; set; }
    public string? NoteNumber { get; set; }
    public string Subject { get; set; } = "";
    public string? Category { get; set; }
    public string State { get; set; } = "";
    public string? Outcome { get; set; }
    public string InitiatorName { get; set; } = "";
    public string? InitiatorVertical { get; set; }
    public long? CurrentHolderUserId { get; set; }
    public string? CurrentHolderName { get; set; }
    public string? SenderName { get; set; }
    public string? HeldSince { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? SubmittedAt { get; set; }
    public string? DecidedAt { get; set; }
    public string? ClosedAt { get; set; }
    public string UpdatedAt { get; set; } = "";
    public bool IsConfidential { get; set; }
    public string DisplayNumber => NoteNumber ?? $"Draft #{Id}";
}

public sealed class ENoteFilter
{
    public string Tab { get; set; } = "pending";
    public string? Q { get; set; }
    public string? State { get; set; }
    public string? Category { get; set; }
    public string? Vertical { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Initiator { get; set; }
    public string? Holder { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class ENoteLookupRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Designation { get; set; }
    public string? Vertical { get; set; }
    public string Username { get; set; } = "";
}

/// <summary>Read side of e-Note: queries, lists and the visibility rules (who may see what).</summary>
public sealed class ENoteRepository(Db db, ENoteOptions o, Fmt fmt)
{
    public ENoteOptions Options => o;

    public ENote? Get(long id) => db.Single<ENote>("SELECT * FROM ENotes WHERE Id = @id", new { id });

    public bool IsParticipant(long noteId, long userId) =>
        db.Scalar<long>("SELECT COUNT(*) FROM ENoteParticipants WHERE NoteId = @noteId AND UserId = @userId", new { noteId, userId }) > 0;

    /// <summary>
    /// Full view of a note: the initiator and everyone it has been marked to; and, once approved, every signed-in user
    /// unless the note is confidential or the setting is "Participants". Administrators and Auditors get no extra access
    /// here — they see metadata only, through the register.
    /// </summary>
    public bool CanView(ENote n, ClaimsPrincipal u)
    {
        var uid = u.UserId();
        if (uid <= 0) return false;
        if (n.InitiatorUserId == uid) return true;
        if (n.State == ENoteStates.Draft) return false;
        if (n.IsApprovedRecord && !n.IsConfidential && o.AllStaffSeeApproved) return true;
        return IsParticipant(n.Id, uid);
    }

    public static bool CanSeeRegister(ClaimsPrincipal u) => u.IsAdmin() || u.IsAuditor();

    public ENotePerson? Person(long userId) => Person(db.Open(), null, userId, dispose: true);

    public static ENotePerson? Person(SqliteConnection c, SqliteTransaction? tx, long userId, bool dispose = false)
    {
        try
        {
            return Db.Query<PersonRow>(c, tx, """
                SELECT u.Id, u.DisplayName AS Name, s.Designation, u.Vertical, u.IsActive
                FROM Users u LEFT JOIN Staff s ON s.StaffId = u.StaffId WHERE u.Id = @userId
                """, new { userId }).Select(p => new ENotePerson(p.Id, p.Name, p.Designation, p.Vertical, p.IsActive)).FirstOrDefault();
        }
        finally { if (dispose) c.Dispose(); }
    }

    private sealed class PersonRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? Designation { get; set; }
        public string? Vertical { get; set; }
        public bool IsActive { get; set; }
    }

    public List<ENoteBodyVersion> Versions(long noteId) =>
        db.Query<ENoteBodyVersion>("SELECT * FROM ENoteBodyVersions WHERE NoteId = @noteId ORDER BY Version", new { noteId });

    public List<ENoteMovement> Movements(long noteId) =>
        db.Query<ENoteMovement>("SELECT * FROM ENoteMovements WHERE NoteId = @noteId ORDER BY Seq", new { noteId });

    public List<ENoteComment> Comments(long noteId) =>
        db.Query<ENoteComment>("SELECT * FROM ENoteComments WHERE NoteId = @noteId ORDER BY Seq", new { noteId });

    public List<ENoteAttachment> Attachments(long noteId) =>
        db.Query<ENoteAttachment>("SELECT * FROM ENoteAttachments WHERE NoteId = @noteId ORDER BY Id", new { noteId });

    public ENoteAttachment? Attachment(long id) => db.Single<ENoteAttachment>("SELECT * FROM ENoteAttachments WHERE Id = @id", new { id });

    public (long count, long bytes) Usage(long noteId)
    {
        var r = db.Query<UsageRow>("SELECT COUNT(*) AS N, COALESCE(SUM(SizeBytes), 0) AS B FROM ENoteAttachments WHERE NoteId = @noteId", new { noteId }).First();
        return (r.N, r.B);
    }

    private sealed class UsageRow { public long N { get; set; } public long B { get; set; } }

    /// <summary>Comments and movements merged in order, as on a paper note-sheet.</summary>
    public List<ENoteSheetEntry> Sheet(long noteId)
    {
        var list = Movements(noteId).Select(m => new ENoteSheetEntry(m.Seq, "Movement", m.FromName ?? "", m.FromDesignation, m.Timestamp, m.Action,
            m.ToName, m.ToDesignation, m.Remarks, m.ToState)).ToList();
        list.AddRange(Comments(noteId).Select(cm => new ENoteSheetEntry(cm.Seq, "Comment", cm.AuthorName, cm.AuthorDesignation, cm.Timestamp, null, null, null, cm.Text, null)));
        return list.OrderBy(e => e.Seq).ToList();
    }

    private const string RowSelect = """
        SELECT n.Id, n.NoteNumber, n.Subject, n.Category, n.State, n.Outcome, n.InitiatorName, n.InitiatorVertical, n.CurrentHolderUserId,
               n.CurrentHolderName, p.DisplayName AS SenderName, n.HeldSince, n.CreatedAt, n.SubmittedAt, n.DecidedAt, n.ClosedAt, n.UpdatedAt, n.IsConfidential
        FROM ENotes n LEFT JOIN Users p ON p.Id = n.PreviousHolderUserId
        """;

    public static readonly Dictionary<string, string> Tabs = new()
    {
        ["pending"] = "Pending with me",
        ["drafts"] = "Drafts",
        ["initiated"] = "Initiated by me",
        ["seen"] = "Seen by me",
        ["approved"] = "Approved notes (all staff)",
    };

    /// <summary>One tab of "My e-Notes": search, filters and paging. Every tab is limited to notes the user may see.</summary>
    public Paged<ENoteRow> List(ClaimsPrincipal u, ENoteFilter f, int pageSize = 25)
    {
        var where = new List<string>();
        switch (f.Tab)
        {
            case "drafts": where.Add($"n.InitiatorUserId = @uid AND n.State = '{ENoteStates.Draft}'"); break;
            case "initiated": where.Add($"n.InitiatorUserId = @uid AND n.State <> '{ENoteStates.Draft}'"); break;
            case "seen": where.Add("n.Id IN (SELECT NoteId FROM ENoteParticipants WHERE UserId = @uid) AND n.InitiatorUserId <> @uid"); break;
            case "approved":
                if (!o.AllStaffSeeApproved) where.Add("0 = 1");
                else where.Add($"n.Outcome = '{ENoteStates.Approved}' AND n.State IN ('{ENoteStates.Approved}', '{ENoteStates.Closed}') AND n.IsConfidential = 0");
                break;
            default: f.Tab = "pending"; where.Add($"n.CurrentHolderUserId = @uid AND n.State IN ('{ENoteStates.InMovement}', '{ENoteStates.Returned}')"); break;
        }
        return Page(where, f, f.Tab == "pending" ? "n.HeldSince, n.Id" : f.Tab == "approved" ? "n.DecidedAt DESC, n.Id DESC" : "n.UpdatedAt DESC, n.Id DESC",
            u.UserId(), pageSize);
    }

    /// <summary>The register (Administrators and Auditors): every submitted note, metadata only.</summary>
    public Paged<ENoteRow> Register(ENoteFilter f, int pageSize = 25) =>
        Page(new List<string> { $"n.State <> '{ENoteStates.Draft}'" }, f, "n.SubmittedAt DESC, n.Id DESC", 0, pageSize);

    public List<ENoteRow> RegisterAll(ENoteFilter f) => Page(new List<string> { $"n.State <> '{ENoteStates.Draft}'" }, f, "n.SubmittedAt DESC, n.Id DESC", 0, 50_000).Items;

    private Paged<ENoteRow> Page(List<string> where, ENoteFilter f, string order, long uid, int pageSize)
    {
        var fts = SearchText.ToFtsQuery(f.Q);
        if (fts != null) where.Add("n.Id IN (SELECT rowid FROM ENoteSearch WHERE ENoteSearch MATCH @fts)");
        if (!string.IsNullOrWhiteSpace(f.State) && ENoteStates.All.Contains(f.State)) where.Add("n.State = @state");
        if (!string.IsNullOrWhiteSpace(f.Category)) where.Add("n.Category = @category");
        if (!string.IsNullOrWhiteSpace(f.Vertical)) where.Add("n.InitiatorVertical = @vertical");
        if (f.From != null) where.Add("COALESCE(n.SubmittedAt, n.CreatedAt) >= @from");
        if (f.To != null) where.Add("COALESCE(n.SubmittedAt, n.CreatedAt) < @to");
        if (!string.IsNullOrWhiteSpace(f.Initiator)) where.Add(@"n.InitiatorName LIKE @initiator ESCAPE '\'");
        if (!string.IsNullOrWhiteSpace(f.Holder)) where.Add(@"n.CurrentHolderName LIKE @holder ESCAPE '\'");
        var page = Math.Max(1, f.Page);
        var args = new
        {
            uid,
            fts, state = f.State, category = f.Category, vertical = f.Vertical,
            from = f.From != null ? UtcStart(f.From.Value) : null,
            to = f.To != null ? UtcStart(f.To.Value.AddDays(1)) : null,
            initiator = SearchText.Like(f.Initiator), holder = SearchText.Like(f.Holder),
            take = pageSize, skip = (page - 1) * pageSize,
        };
        var w = string.Join(" AND ", where);
        return new Paged<ENoteRow>
        {
            Items = db.Query<ENoteRow>($"{RowSelect} WHERE {w} ORDER BY {order} LIMIT @take OFFSET @skip", args),
            Total = db.Scalar<long>($"SELECT COUNT(*) FROM ENotes n WHERE {w}", args),
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>Start of a local office day, in UTC (stored timestamps are UTC).</summary>
    private string UtcStart(DateOnly d) => (d.ToDateTime(TimeOnly.MinValue) - (fmt.LocalNow - DateTime.UtcNow)).ToString("o", CultureInfo.InvariantCulture);

    /// <summary>"Mark to" type-ahead: active users by name, user name or employee code (at least 2 characters, at most 10 results).</summary>
    public List<ENoteLookupRow> Lookup(string? q, long excludeUserId)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return new();
        return db.Query<ENoteLookupRow>("""
            SELECT u.Id, u.DisplayName AS Name, s.Designation, u.Vertical, u.Username
            FROM Users u LEFT JOIN Staff s ON s.StaffId = u.StaffId
            WHERE u.IsActive = 1 AND u.Id <> @excludeUserId
              AND (u.DisplayName LIKE @like ESCAPE '\' OR u.Username LIKE @like ESCAPE '\' OR s.EmployeeCode LIKE @like ESCAPE '\')
            ORDER BY CASE WHEN u.DisplayName LIKE @prefix ESCAPE '\' THEN 0 ELSE 1 END, u.DisplayName
            LIMIT 10
            """, new { like = SearchText.Like(q), prefix = SearchText.Like(q)[1..], excludeUserId });
    }

    /// <summary>Text of a version (or the latest submitted version).</summary>
    public ENoteBodyVersion? Version(long noteId, long? version) => version is > 0
        ? db.Single<ENoteBodyVersion>("SELECT * FROM ENoteBodyVersions WHERE NoteId = @noteId AND Version = @version", new { noteId, version })
        : db.Single<ENoteBodyVersion>("SELECT * FROM ENoteBodyVersions WHERE NoteId = @noteId ORDER BY Version DESC LIMIT 1", new { noteId });

    // ---- "ENote.Viewed" is sampled to once per user per note per day, to keep the audit log lean ----
    private readonly ConcurrentDictionary<(long note, long user), string> _viewed = new();

    public bool FirstViewToday(long noteId, long userId)
    {
        var today = fmt.Today.ToString("yyyy-MM-dd");
        var key = (noteId, userId);
        if (_viewed.TryGetValue(key, out var d) && d == today) return false;
        _viewed[key] = today;
        if (_viewed.Count > 100_000) foreach (var k in _viewed.Where(kv => kv.Value != today).Select(kv => kv.Key).ToList()) _viewed.TryRemove(k, out _);
        return true;
    }

    /// <summary>Paragraphs of a note body: separated by blank lines; single line breaks are kept inside a paragraph.</summary>
    public static List<string> Paragraphs(string? body) =>
        System.Text.RegularExpressions.Regex.Split((body ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim(), @"\n[ \t]*\n+")
            .Select(p => p.Trim('\n')).Where(p => p.Trim().Length > 0).ToList();

    public static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
