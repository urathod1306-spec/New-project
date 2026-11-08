using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Data;

/// <summary>What the user chose on the ATR page (stored with a snapshot as OptionsJson).</summary>
public sealed class AtrOptions
{
    public string Template { get; set; } = AtrTemplate.BoardAtr;
    public List<long> EventIds { get; set; } = new();
    public string AsOn { get; set; } = "";
    public string? CompletedSince { get; set; }
    public string OfficerCodeFormat { get; set; } = "Code";
}

/// <summary>One printed table row of a non-summary section, as stored in ComplianceSnapshotItems.</summary>
public sealed class AtrItem
{
    public int Seq { get; set; }
    public int Section { get; set; }
    public long? PointId { get; set; }
    public List<string> Cells { get; set; } = new();
    public bool Bold { get; set; }
    public string? ReportedText { get; set; }
    public string? Status { get; set; }
}

/// <summary>Everything printed that is not an item row (stored with a snapshot as HeaderJson).</summary>
public sealed class AtrHeader
{
    public string Org { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Subtitles { get; set; } = new();
    public string? Signature { get; set; }
    public List<AtrSectionDef> Sections { get; set; } = new();
}

public sealed class AtrSectionDef
{
    public string? Heading { get; set; }
    public string? SheetName { get; set; }
    public bool IsSummary { get; set; }
    public List<AtrColumn> Columns { get; set; } = new();
    /// <summary>Rows of a summary section (item sections keep their rows in <see cref="AtrItem"/>).</summary>
    public List<List<string>>? SummaryRows { get; set; }
}

public sealed class AtrColumn
{
    public string H { get; set; } = "";
    public float W { get; set; }
}

/// <summary>A built ATR: the content (header + items), its hash, the forum it belongs to and the earliest as-on date allowed.</summary>
public sealed class AtrBuild
{
    public AtrOptions Options { get; init; } = new();
    public AtrHeader Header { get; init; } = new();
    public List<AtrItem> Items { get; init; } = new();
    public string HeaderJson { get; init; } = "";
    public string OptionsJson { get; init; } = "";
    public string Hash { get; init; } = "";
    public long? ForumId { get; init; }
    public string Forum { get; init; } = "";
    public string MinAsOn { get; init; } = "";
}

/// <summary>
/// Builds Action Taken Reports (DD-07 §7) from live data or from a snapshot, computes their content hash, and saves,
/// verifies, regenerates and voids snapshots. Rendering is done by the format writers from the same model, so a
/// snapshot regenerates byte-identical output.
/// </summary>
public sealed class AtrBuilder(Db db, ComplianceAccess access, ComplianceRepository repo, ComplianceOptions o, IntranetOptions io, AuditService audit, Fmt fmt)
{
    public const int MaxEvents = 20;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private string Dot(string? iso) => fmt.DateDot(iso);

    // ================================================================= live build

    private sealed class PointRow
    {
        public long Id { get; set; }
        public long EventId { get; set; }
        public long Seq { get; set; }
        public string? RefNo { get; set; }
        public string? RefSubject { get; set; }
        public string PointText { get; set; } = "";
        public string Status { get; set; } = "";
        public string? AtrText { get; set; }
        public string? AtrAsOn { get; set; }
        public string? ClosedAt { get; set; }
        public long? CarriedFromId { get; set; }
        public string? OfficerCode { get; set; }
        public string OfficerName { get; set; } = "";
    }

    private static bool IsDone(string s) => s is CStatus.Completed or CStatus.ProposedClosure;

    private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X", _ => n.ToString(CultureInfo.InvariantCulture) };

    private string EventDate(ComplianceEvent e) =>
        e.HeldOn != null ? Dot(e.HeldOn) : e.PeriodFrom != null || e.PeriodTo != null ? $"{Dot(e.PeriodFrom)}–{Dot(e.PeriodTo)}" : Dot(e.ReportReceivedOn);

    private string Officer(PointRow p, string format) => format switch
    {
        "Name" => p.OfficerName,
        "NameAndCode" => string.IsNullOrEmpty(p.OfficerCode) ? p.OfficerName : $"{p.OfficerName} ({p.OfficerCode})",
        _ => string.IsNullOrEmpty(p.OfficerCode) ? p.OfficerName : p.OfficerCode!,
    };

    /// <summary>The newest non-voided snapshot of the same template and forum with an as-on date on or before this one (DD-07 §7.2).</summary>
    public ComplianceSnapshot? PreviousSnapshot(string template, long? forumId, string asOn) => db.Single<ComplianceSnapshot>("""
        SELECT s.* FROM ComplianceSnapshots s
        WHERE s.Template = @template AND s.ForumId IS @forumId AND s.AsOn <= @asOn
          AND NOT EXISTS (SELECT 1 FROM ComplianceSnapshotVoids v WHERE v.SnapshotId = s.Id)
        ORDER BY s.AsOn DESC, s.Id DESC LIMIT 1
        """, new { template, forumId, asOn });

    /// <summary>Default for "include completed since": the as-on date of the previous snapshot of this template and forum.</summary>
    public string? DefaultCompletedSince(string template, long? forumId, string asOn) => PreviousSnapshot(template, forumId, asOn)?.AsOn;

    /// <summary>Checks the options and builds the report from live data. Throws WorkflowException with a reason the user can act on.</summary>
    public AtrBuild Build(AtrOptions opt, ClaimsPrincipal u)
    {
        if (!AtrTemplate.Labels.ContainsKey(opt.Template)) throw new WorkflowException("Choose a template.");
        var ids = opt.EventIds.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) throw new WorkflowException("Choose at least one event.");
        if (ids.Count > MaxEvents) throw new WorkflowException($"Choose at most {MaxEvents} events.");
        var events = ids.Select(id => repo.Event(id)).ToList();
        if (events.Any(e => e == null || !access.CanSeeEventManaged(e, u))) throw new WorkflowException("An event was not found.");
        var evs = events.Select(e => e!).OrderByDescending(e => e.DateKey ?? "").ThenByDescending(e => e.Id).ToList();
        if (opt.Template != AtrTemplate.ObservationReply && evs.Select(e => e.ForumId).Distinct().Count() > 1)
            throw new WorkflowException("All events in this template must belong to the same forum.");
        var asOn = ComplianceWorkflow.ParseDate("Status as on", opt.AsOn, true)!;
        var today = fmt.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.CompareOrdinal(asOn, today) > 0) throw new WorkflowException("The 'status as on' date cannot be in the future.");
        var since = ComplianceWorkflow.ParseDate("Include completed since", opt.CompletedSince);
        var format = opt.OfficerCodeFormat is "Name" or "NameAndCode" ? opt.OfficerCodeFormat : "Code";
        var options = new AtrOptions { Template = opt.Template, EventIds = evs.Select(e => e.Id).ToList(), AsOn = asOn, CompletedSince = since, OfficerCodeFormat = format };

        var points = evs.ToDictionary(e => e.Id, e => db.Query<PointRow>("""
            SELECT p.Id, p.EventId, p.Seq, p.RefNo, p.RefSubject, p.PointText, p.Status, p.AtrText, p.AtrAsOn, p.ClosedAt, p.CarriedFromId, p.OfficerCode,
                   u.DisplayName AS OfficerName
            FROM CompliancePoints p JOIN Users u ON u.Id = p.PrimaryOfficerUserId
            WHERE p.EventId = @Id AND p.Status NOT IN ('Dropped','Merged') ORDER BY p.Seq, p.Id
            """, new { e.Id }));
        bool CompletedInWindow(PointRow p) => p.Status == CStatus.Completed && since != null && p.ClosedAt != null && string.CompareOrdinal(fmt.LocalDate(p.ClosedAt), since) >= 0;
        bool Listed(PointRow p) => p.Status != CStatus.Completed || CompletedInWindow(p);

        var newest = evs[0];
        var header = new AtrHeader { Org = io.OrganisationName, Signature = string.IsNullOrWhiteSpace(o.SignatureBlock) ? null : o.SignatureBlock, Subtitles = { $"Status as on {Dot(asOn)}" } };
        var items = new List<AtrItem>();
        var printed = new List<PointRow>();
        void Item(int section, PointRow? p, IEnumerable<string> cells, string? reported, bool bold = false)
        {
            items.Add(new AtrItem { Seq = items.Count + 1, Section = section, PointId = p?.Id, Cells = cells.Select(c => c ?? "").ToList(), Bold = bold, ReportedText = reported, Status = p?.Status });
            if (p != null) printed.Add(p);
        }
        void Summary(List<PointRow> rows)
        {
            var done = rows.Count(r => IsDone(r.Status));
            header.Sections.Add(new AtrSectionDef
            {
                IsSummary = true,
                Columns = { new() { H = "Completed", W = 1 }, new() { H = "Ongoing", W = 1 }, new() { H = "Total", W = 1 } },
                SummaryRows = new() { new() { done.ToString(CultureInfo.InvariantCulture), (rows.Count - done).ToString(CultureInfo.InvariantCulture), rows.Count.ToString(CultureInfo.InvariantCulture) } },
            });
        }

        switch (options.Template)
        {
            case AtrTemplate.BoardAtr:
            {
                header.Title = $"Follow-up Actions on the Minutes of the {(string.IsNullOrEmpty(newest.EventNo) ? "" : newest.EventNo + " ")}Meeting of the {newest.Forum} held on {EventDate(newest)}";
                var previous = PreviousSnapshot(options.Template, newest.ForumId, asOn);
                var prevText = previous == null ? new Dictionary<long, string?>()
                    : db.Query<ComplianceSnapshotItem>("SELECT PointId, ReportedText FROM ComplianceSnapshotItems WHERE SnapshotId = @Id AND PointId IS NOT NULL", new { previous.Id })
                        .GroupBy(x => x.PointId!.Value).ToDictionary(g => g.Key, g => g.Last().ReportedText);
                for (var k = 0; k < evs.Count; k++)
                {
                    var e = evs[k];
                    var section = header.Sections.Count;
                    if (k == 0)
                    {
                        header.Sections.Add(new AtrSectionDef
                        {
                            Heading = $"Annexure I — {e.Heading(fmt)} held on {EventDate(e)}", SheetName = "Annexure I",
                            Columns = { new() { H = "Memorandum / Agenda No. (with subject)", W = 2 }, new() { H = "Action Point", W = 4 }, new() { H = "Action Taken", W = 4 }, new() { H = "Status", W = 1.2f } },
                        });
                        var rows = points[e.Id];
                        foreach (var p in rows)
                            Item(section, p, new[]
                            {
                                string.Join("\n", new[] { p.RefNo, p.RefSubject }.Where(x => !string.IsNullOrWhiteSpace(x))),
                                p.PointText, p.AtrText ?? "—",
                                IsDone(p.Status) ? "Completed" : p.Status == CStatus.PendingExternal ? "Ongoing (guidance awaited)" : "Ongoing",
                            }, p.AtrText);
                        Summary(rows);
                    }
                    else
                    {
                        var rows = points[e.Id].Where(Listed).ToList();
                        header.Sections.Add(new AtrSectionDef
                        {
                            Heading = $"Annexure {Roman(k + 1)} — {e.Heading(fmt)} held on {EventDate(e)}", SheetName = $"Annexure {Roman(k + 1)}",
                            Columns = { new() { H = "Agenda No.", W = 1.5f }, new() { H = "Action Point", W = 3.5f }, new() { H = $"Action presented in previous {e.Forum} meeting", W = 3.5f }, new() { H = "Further action taken / to be taken", W = 3.5f } },
                        });
                        foreach (var p in rows)
                            Item(section, p, new[]
                            {
                                string.Join("\n", new[] { p.RefNo, p.RefSubject }.Where(x => !string.IsNullOrWhiteSpace(x))),
                                p.PointText, prevText.TryGetValue(p.Id, out var pt) && !string.IsNullOrWhiteSpace(pt) ? pt! : "—",
                                (p.AtrText ?? "—") + (IsDone(p.Status) ? " [Completed]" : ""),
                            }, p.AtrText);
                        Summary(rows);
                    }
                }
                break;
            }
            case AtrTemplate.ObservationReply:
            {
                header.Title = evs.Count == 1 ? $"Statement of Compliance — {newest.Forum}: {newest.Title}" : "Statement of Compliance on Audit and Inspection Observations";
                foreach (var e in evs)
                {
                    var section = header.Sections.Count;
                    header.Sections.Add(new AtrSectionDef
                    {
                        Heading = $"{e.Forum} — {e.Title} ({EventDate(e)})", SheetName = e.Forum,
                        Columns = { new() { H = "S.No.", W = 0.8f }, new() { H = "Observation", W = 5 }, new() { H = "Proposed Action / Reply", W = 5 } },
                    });
                    var n = 0;
                    foreach (var p in points[e.Id].Where(Listed))
                        Item(section, p, new[]
                        {
                            string.IsNullOrWhiteSpace(p.RefNo) ? (++n).ToString(CultureInfo.InvariantCulture) : p.RefNo!,
                            p.PointText, (p.Status == CStatus.PendingExternal ? "Guidance awaited — " : "") + (p.AtrText ?? "—"),
                        }, p.AtrText);
                }
                break;
            }
            default:
            {
                header.Title = "Internal Audit — Compliance Status";
                header.Sections.Add(new AtrSectionDef
                {
                    Heading = $"Internal Audit — compliance status as on {Dot(asOn)}", SheetName = "Compliance " + Dot(asOn),
                    Columns =
                    {
                        new() { H = "S.No.", W = 0.7f }, new() { H = "Audit Observation", W = 4.5f }, new() { H = "Dealing Person", W = 1.3f },
                        new() { H = "Compliance", W = 4.5f }, new() { H = $"Status as on {Dot(asOn)}", W = 1.3f }, new() { H = "New/Old", W = 0.8f },
                    },
                });
                var n = 0;
                foreach (var e in Enumerable.Reverse(evs))       // oldest observations first
                    foreach (var p in points[e.Id].Where(Listed))
                        Item(0, p, new[]
                        {
                            string.IsNullOrWhiteSpace(p.RefNo) ? (++n).ToString(CultureInfo.InvariantCulture) : p.RefNo!,
                            p.PointText, Officer(p, format), p.AtrText ?? "—",
                            IsDone(p.Status) ? "Complied" : p.Status == CStatus.PendingExternal ? "Pending" : "Ongoing",
                            e.Id == newest.Id && p.CarriedFromId == null ? "New" : "Old",
                        }, p.AtrText);
                break;
            }
        }

        // as-on rule (DD-07 §7.6): never earlier than the latest change the report contains
        var min = "";
        foreach (var p in printed)
        {
            foreach (var d in new[] { p.AtrAsOn, p.ClosedAt != null ? fmt.LocalDate(p.ClosedAt) : null })
                if (d != null && string.CompareOrdinal(d, min) > 0) min = d;
        }
        foreach (var chunk in printed.Select(p => p.Id).Distinct().Chunk(500))
        {
            var last = db.Scalar<string>($"SELECT MAX(At) FROM ComplianceHistory WHERE PointId IN ({string.Join(",", chunk)})");
            var d = last != null ? fmt.LocalDate(last) : "";
            if (string.CompareOrdinal(d, min) > 0) min = d;
        }
        if (min.Length > 0 && string.CompareOrdinal(asOn, min) < 0)
            throw new WorkflowException($"The 'status as on' date cannot be earlier than {Dot(min)}, the date of the latest change in this report.");

        var headerJson = JsonSerializer.Serialize(header, Json);
        return new AtrBuild
        {
            Options = options, Header = header, Items = items, HeaderJson = headerJson, OptionsJson = JsonSerializer.Serialize(options, Json),
            Hash = ContentHash(headerJson, items), ForumId = newest.ForumId, Forum = newest.Forum, MinAsOn = min,
        };
    }

    // ================================================================= hash and document

    /// <summary>SHA-256 of the printed content (DD-07 §7.5 step 2); identical for a live build and the snapshot made from it.</summary>
    public static string ContentHash(string headerJson, IEnumerable<AtrItem> items)
    {
        var sb = new StringBuilder("NABCONS ATR v1\n").Append(headerJson).Append('\n');
        foreach (var i in items)
            sb.Append(i.Seq).Append('|').Append(i.Section).Append('|').Append(i.PointId?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|')
              .Append(i.Bold ? "1" : "0").Append('|').Append(JsonSerializer.Serialize(i.Cells, Json)).Append('|')
              .Append(i.ReportedText ?? "").Append('|').Append(i.Status ?? "").Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Turns header + items into the printable model. The footer is added here, outside the hashed content.</summary>
    public static ReportDocument ToDocument(AtrHeader h, IReadOnlyList<AtrItem> items, string footer, DateTime generatedAt, string fileBase) => new()
    {
        OrganisationLine = h.Org, Title = h.Title, Subtitles = h.Subtitles.ToList(), Signature = h.Signature, Footer = footer,
        GeneratedAt = DateTime.SpecifyKind(generatedAt, DateTimeKind.Utc), FileBase = fileBase,
        Sections = h.Sections.Select((s, i) => new ReportSection
        {
            Heading = s.Heading, SheetName = s.SheetName, IsSummary = s.IsSummary,
            Columns = s.Columns.Select(c => new ReportColumn(c.H, c.W)).ToList(),
            Rows = s.IsSummary
                ? (s.SummaryRows ?? new()).Select(r => new ReportRow(r, true)).ToList()
                : items.Where(x => x.Section == i).OrderBy(x => x.Seq).Select(x => new ReportRow(x.Cells, x.Bold)).ToList(),
        }).ToList(),
    };

    public static string DraftFooter(string hash) => $"Draft — content hash {hash[..16]} · not for submission";

    public string FinalFooter(ComplianceSnapshot s) => $"Final — submitted to {s.SubmittedTo} on {Dot(s.SubmittedOn)} · Snapshot #{s.Id} · SHA-256 {s.Sha256[..16]}";

    public ReportDocument DraftDocument(AtrBuild b) =>
        ToDocument(b.Header, b.Items, DraftFooter(b.Hash), DateTime.UtcNow, $"ATR-draft-{b.Options.AsOn}");

    // ================================================================= snapshots

    /// <summary>
    /// Marks an ATR final (DD-07 §3.4, §7.5): inside one BEGIN IMMEDIATE transaction the report is rebuilt, its content
    /// hash compared with the one the user previewed, and the snapshot rows inserted. The hash goes into the audit chain.
    /// </summary>
    public long Finalise(AtrOptions opt, string expectedHash, string? submittedTo, string? submittedOn, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can mark an ATR as final.");
        var to = ComplianceText.Required("Submitted to", submittedTo, 150);
        var on = ComplianceWorkflow.ParseDate("Submitted on", submittedOn, true)!;
        long id;
        AtrBuild b;
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            b = Build(opt, u);                          // no one can write while this transaction holds the lock
            if (!string.Equals(b.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new WorkflowException("The data has changed since your preview. Preview the report again, check it, and then mark it final.");
            // a double click must not create two identical final ATRs
            var same = Db.Scalar<long>(c, tx, """
                SELECT COALESCE((SELECT s.Id FROM ComplianceSnapshots s WHERE s.Sha256 = @h AND s.Template = @t AND s.SubmittedOn = @on
                  AND NOT EXISTS (SELECT 1 FROM ComplianceSnapshotVoids v WHERE v.SnapshotId = s.Id) ORDER BY s.Id DESC LIMIT 1), 0)
                """, new { h = b.Hash, t = b.Options.Template, on });
            if (same > 0) throw new WorkflowException($"This ATR is already marked final as snapshot #{same}.");
            var now = Db.Now();
            id = Db.Insert(c, tx, """
                INSERT INTO ComplianceSnapshots (Template, ForumId, Forum, Title, SubmittedTo, SubmittedOn, AsOn, OptionsJson, HeaderJson, ItemCount, Sha256, CreatedByUserId, CreatedByName, CreatedAt)
                VALUES (@Template, @ForumId, @Forum, @Title, @to, @on, @AsOn, @OptionsJson, @HeaderJson, @n, @Hash, @uid, @name, @now)
                """, new
            {
                b.Options.Template, b.ForumId, b.Forum, b.Header.Title, to, on, b.Options.AsOn, b.OptionsJson, b.HeaderJson, n = b.Items.Count, b.Hash,
                uid = u.UserId(), name = u.DisplayName(), now,
            });
            foreach (var e in b.Options.EventIds) Db.Exec(c, tx, "INSERT INTO ComplianceSnapshotEvents (SnapshotId, EventId) VALUES (@id, @e)", new { id, e });
            foreach (var i in b.Items)
                Db.Exec(c, tx, """
                    INSERT INTO ComplianceSnapshotItems (SnapshotId, Seq, Section, PointId, Cells, Bold, ReportedText, Status)
                    VALUES (@id, @Seq, @Section, @PointId, @cells, @Bold, @ReportedText, @Status)
                    """, new { id, i.Seq, i.Section, i.PointId, cells = JsonSerializer.Serialize(i.Cells, Json), i.Bold, i.ReportedText, i.Status });
            foreach (var e in b.Options.EventIds)
                Db.Exec(c, tx, "INSERT INTO ComplianceHistory (EventId, At, ActorUserId, ActorName, Action, Detail) VALUES (@e, @now, @uid, @name, 'AtrFinalised', @d)",
                    new { e, now, uid = u.UserId(), name = u.DisplayName(), d = $"Snapshot #{id}: {AtrTemplate.Labels[b.Options.Template]}, as on {b.Options.AsOn}, submitted to {to} on {on}" });
            tx.Commit();
        }
        audit.Log("Compliance.AtrFinalised", "ComplianceSnapshot", id, $"snapshot #{id}; {b.Options.Template}; events {string.Join(",", b.Options.EventIds)}; {b.Items.Count} rows; SHA-256 {b.Hash}");
        return id;
    }

    public ComplianceSnapshot? Snapshot(long id) => db.Single<ComplianceSnapshot>("""
        SELECT s.*, v.Reason AS VoidReason, v.VoidedByName, v.VoidedAt FROM ComplianceSnapshots s LEFT JOIN ComplianceSnapshotVoids v ON v.SnapshotId = s.Id WHERE s.Id = @id
        """, new { id });

    public List<AtrItem> SnapshotItems(long id) => db.Query<ComplianceSnapshotItem>("SELECT * FROM ComplianceSnapshotItems WHERE SnapshotId = @id ORDER BY Seq", new { id })
        .Select(i => new AtrItem
        {
            Seq = (int)i.Seq, Section = (int)i.Section, PointId = i.PointId, Cells = JsonSerializer.Deserialize<List<string>>(i.Cells, Json) ?? new(),
            Bold = i.Bold, ReportedText = i.ReportedText, Status = i.Status,
        }).ToList();

    public List<long> SnapshotEvents(long id) => db.Query<IdOnly>("SELECT EventId AS Id FROM ComplianceSnapshotEvents WHERE SnapshotId = @id", new { id }).Select(x => x.Id).ToList();

    private sealed class IdOnly { public long Id { get; set; } }

    /// <summary>A snapshot is visible when every one of its events is visible to the user in the management views.</summary>
    public bool CanView(ComplianceSnapshot s, ClaimsPrincipal u) =>
        access.SeesAll(u) && SnapshotEvents(s.Id).All(e => repo.Event(e) is { } ev && access.CanSeeEventManaged(ev, u));

    public List<ComplianceSnapshot> Snapshots(ClaimsPrincipal u, int limit = 300) => db.Query<ComplianceSnapshot>($"""
        SELECT s.*, v.Reason AS VoidReason, v.VoidedByName, v.VoidedAt FROM ComplianceSnapshots s LEFT JOIN ComplianceSnapshotVoids v ON v.SnapshotId = s.Id
        WHERE NOT EXISTS (SELECT 1 FROM ComplianceSnapshotEvents se JOIN ComplianceEvents e ON e.Id = se.EventId
                          WHERE se.SnapshotId = s.Id AND NOT ({access.ManagedVisibleSql(u)}))
        ORDER BY s.Id DESC LIMIT @limit
        """, new { uid = u.UserId(), limit });

    /// <summary>Recomputes the content hash from the stored rows (true = unchanged since it was marked final).</summary>
    public bool Verify(ComplianceSnapshot s) => string.Equals(ContentHash(s.HeaderJson, SnapshotItems(s.Id)), s.Sha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>The printable model rebuilt only from the snapshot rows, stamped with the snapshot's creation time.</summary>
    public ReportDocument Regenerate(ComplianceSnapshot s)
    {
        var header = JsonSerializer.Deserialize<AtrHeader>(s.HeaderJson, Json) ?? new AtrHeader();
        return ToDocument(header, SnapshotItems(s.Id), FinalFooter(s), CreatedAt(s), $"ATR-snapshot-{s.Id}");
    }

    public static DateTime CreatedAt(ComplianceSnapshot s) =>
        DateTime.Parse(s.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public void Void(long id, string? reason, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can void a snapshot.");
        var why = ComplianceText.Required("Reason", reason, ComplianceText.Remarks);
        var s = Snapshot(id);
        if (s == null || !CanView(s, u)) throw new WorkflowException("Snapshot not found.");
        // INSERT OR IGNORE: if two people void it at the same moment, the second one is simply told it is already void
        var n = db.Exec("INSERT OR IGNORE INTO ComplianceSnapshotVoids (SnapshotId, Reason, VoidedByName, VoidedAt) VALUES (@id, @why, @name, @now)", new { id, why, name = u.DisplayName(), now = Db.Now() });
        if (s.IsVoided || n == 0) throw new WorkflowException("This snapshot is already void.");
        audit.Log("Compliance.SnapshotVoided", "ComplianceSnapshot", id, $"snapshot #{id} voided");
    }
}
