using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Tests;

/// <summary>Shared helpers for driving e-Notes directly through the workflow.</summary>
public abstract class ENoteTestBase : IDisposable
{
    protected readonly TestApp App;
    protected ENoteTestBase(params (string, string)[] settings) => App = new TestApp(settings);
    public void Dispose() => App.Dispose();

    protected ENoteWorkflow W => App.Get<ENoteWorkflow>();
    protected ENoteRepository R => App.Get<ENoteRepository>();
    protected long Id(string user) => App.User(user).Id;
    protected long Rv(long id) => R.Get(id)!.RowVersion;
    protected static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    protected long Draft(string user = "staff", bool confidential = false, string subject = "Hiring of a vehicle for field work", string body = "Para one.\n\nPara two.")
        => W.Create(new ENoteWorkflow.DraftInput(subject, "Procurement", body, confidential, true), App.Principal(user));

    protected void Mark(long id, string from, string to, string? remarks = "Please see.") => W.MarkTo(id, Id(to), remarks, Rv(id), App.Principal(from));

    protected static IFormFile File(string name, byte[] bytes)
    {
        var ms = new MemoryStream(bytes);
        return new FormFile(ms, 0, bytes.Length, "uploads", name) { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };
    }

    protected Task Attach(long id, string user, string name = "scope.pdf", byte[]? bytes = null) =>
        W.AttachAsync(id, new[] { File(name, bytes ?? Pdf) }, null, App.Principal(user));
}

public class ENoteWorkflowTests() : ENoteTestBase
{
    [Fact]
    public void Only_the_current_holder_can_act()
    {
        var id = Draft();
        Mark(id, "staff", "approver1", null);                                  // remark optional for the initiator
        Assert.Throws<WorkflowException>(() => W.Comment(id, "Not mine", Rv(id), App.Principal("approver2")));
        Assert.Throws<WorkflowException>(() => W.MarkTo(id, Id("auditor"), "x", Rv(id), App.Principal("approver2")));
        Assert.Throws<WorkflowException>(() => W.Decide(id, true, "ok", Rv(id), App.Principal("approver2")));
        Assert.Throws<WorkflowException>(() => W.ReturnToInitiator(id, "x", Rv(id), App.Principal("approver2")));
        Assert.ThrowsAsync<WorkflowException>(() => Attach(id, "approver2")).Wait();
        W.Comment(id, "Examined.", Rv(id), App.Principal("approver1"));      // the holder can
        Assert.Single(R.Comments(id));
    }

    [Fact]
    public void Initiator_never_decides_and_marking_guards_hold()
    {
        var id = Draft();
        var staff = App.Principal("staff");
        Assert.Throws<WorkflowException>(() => W.MarkTo(id, Id("staff"), null, Rv(id), staff));          // to self
        var inactive = App.Get<UserRepository>().GetByUsername("publisher")!;
        inactive.IsActive = false;
        App.Get<UserRepository>().Update(inactive, "test", false);
        Assert.Throws<WorkflowException>(() => W.MarkTo(id, inactive.Id, null, Rv(id), staff));          // to an inactive user
        Mark(id, "staff", "approver1");
        Assert.Throws<WorkflowException>(() => W.MarkTo(id, Id("staff"), "back", Rv(id), App.Principal("approver1")));  // to the initiator while moving
        Assert.Throws<WorkflowException>(() => W.MarkTo(id, Id("approver1"), "x", Rv(id), App.Principal("approver1"))); // to the holder
        // the initiator cannot approve or reject, even when the note comes back to them
        W.ReturnToInitiator(id, "Please add costs.", Rv(id), App.Principal("approver1"));
        Assert.Throws<WorkflowException>(() => W.Decide(id, true, "self", Rv(id), staff));
        Assert.Throws<WorkflowException>(() => W.Decide(id, false, "self", Rv(id), staff));
    }

    [Fact]
    public void Remarks_are_mandatory_where_the_rules_say_so()
    {
        var id = Draft();
        Mark(id, "staff", "approver1", null);
        var a1 = App.Principal("approver1");
        Assert.Throws<WorkflowException>(() => W.MarkTo(id, Id("approver2"), " ", Rv(id), a1));
        Assert.Throws<WorkflowException>(() => W.SendBack(id, null, Rv(id), a1));
        Assert.Throws<WorkflowException>(() => W.ReturnToInitiator(id, "", Rv(id), a1));
        Assert.Throws<WorkflowException>(() => W.Decide(id, true, null, Rv(id), a1));
        Assert.Throws<WorkflowException>(() => W.Decide(id, false, "  ", Rv(id), a1));
        Assert.Throws<WorkflowException>(() => W.Comment(id, "", Rv(id), a1));
        Assert.Throws<WorkflowException>(() => W.Withdraw(id, null, Rv(id), App.Principal("staff")));
        // declaration required at creation; subject and body too
        Assert.Throws<WorkflowException>(() => W.Create(new ENoteWorkflow.DraftInput("S", "Procurement", "B", false, false), App.Principal("staff")));
        Assert.Throws<WorkflowException>(() => W.Create(new ENoteWorkflow.DraftInput("", "Procurement", "B", false, true), App.Principal("staff")));
        Assert.Throws<WorkflowException>(() => W.Create(new ENoteWorkflow.DraftInput("S", "Procurement", "", false, true), App.Principal("staff")));
    }

    [Fact]
    public void Text_is_locked_after_submission_except_after_return_and_every_version_is_kept()
    {
        var id = Draft(body: "Version one text.");
        var staff = App.Principal("staff");
        W.EditDraft(id, new ENoteWorkflow.DraftInput("Changed subject", "Procurement", "Draft edited freely.", true, true), Rv(id), staff);
        Mark(id, "staff", "approver1");
        Assert.Matches(@"^NCS/EN/RID/\d{4}-\d{2}/\d{4}$", R.Get(id)!.NoteNumber);       // staff is in Rural Infrastructure (code RID)
        Assert.Throws<WorkflowException>(() => W.EditDraft(id, new ENoteWorkflow.DraftInput("x", "Procurement", "Sneaky edit", false, true), Rv(id), staff));
        Assert.Single(R.Versions(id));
        Assert.Equal("Draft edited freely.", R.Versions(id)[0].BodyText);

        W.ReturnToInitiator(id, "Add the cost.", Rv(id), App.Principal("approver1"));
        W.EditDraft(id, new ENoteWorkflow.DraftInput("Changed subject", "Procurement", "Draft edited freely.\n\nCost: ₹ 51,300.", false, false), Rv(id), staff);
        Assert.True(R.Get(id)!.IsConfidential);                                // the confidential flag is locked after the first submission
        Mark(id, "staff", "approver2", "Revised as advised.");
        var v = R.Versions(id);
        Assert.Equal(2, v.Count);
        Assert.Contains("51,300", v[1].BodyText);
        Assert.Equal(ENoteRepository.Sha(v[1].BodyText), v[1].Sha256);
        Assert.Contains(R.Movements(id), m => m.Action == "Resubmitted");

        // returned again and re-submitted without changes: no new version
        W.ReturnToInitiator(id, "Just confirm.", Rv(id), App.Principal("approver2"));
        Mark(id, "staff", "approver2", "Confirmed.");
        Assert.Equal(2, R.Versions(id).Count);
    }

    [Fact]
    public async Task History_tables_are_append_only_by_database_triggers()
    {
        var id = Draft();
        await Attach(id, "staff", "draft.pdf");
        W.RemoveDraftAttachment(id, R.Attachments(id)[0].Id, App.Principal("staff"));     // allowed before the first submission
        await Attach(id, "staff", "kept.pdf");
        Mark(id, "staff", "approver1");
        W.Comment(id, "A comment", Rv(id), App.Principal("approver1"));
        using var c = App.Get<Db>().Open();
        foreach (var sql in new[]
        {
            "UPDATE ENoteMovements SET Remarks = 'x'", "DELETE FROM ENoteMovements",
            "UPDATE ENoteComments SET Text = 'x'", "DELETE FROM ENoteComments",
            "UPDATE ENoteBodyVersions SET BodyText = 'x'", "DELETE FROM ENoteBodyVersions",
            "DELETE FROM ENoteAttachments", "UPDATE ENoteAttachments SET OriginalFileName = 'x'", "UPDATE ENoteAttachments SET Sha256 = 'x'",
        })
            Assert.Throws<SqliteException>(() => Db.Exec(c, null, sql));
        // the one allowed change: "superseded by" a newer attachment, set once
        await Attach(id, "approver1", "newer.pdf");
        var all = R.Attachments(id);
        Assert.Throws<WorkflowException>(() => W.Supersede(id, all[0].Id, all[1].Id, App.Principal("approver1")));  // not my file
        W.Supersede(id, all[0].Id, all[1].Id, App.Principal("staff"));
        Assert.Equal(all[1].Id, R.Attachment(all[0].Id)!.SupersededById);
        Assert.Throws<SqliteException>(() => Db.Exec(c, null, "UPDATE ENoteAttachments SET SupersededById = NULL"));
        Assert.Equal(2, R.Attachments(id).Count);                               // both stay visible
    }

    [Fact]
    public void Two_people_acting_at_once_the_second_sees_this_note_has_changed()
    {
        var id = Draft();
        Mark(id, "staff", "approver1");
        var stale = Rv(id);
        W.Comment(id, "First", stale, App.Principal("approver1"));
        var ex = Assert.Throws<WorkflowException>(() => W.Decide(id, true, "Second, with the old version", stale, App.Principal("approver1")));
        Assert.Equal(ENoteWorkflow.Changed, ex.Message);
        var ex2 = Assert.Throws<WorkflowException>(() => W.Withdraw(id, "late", stale, App.Principal("staff")));
        Assert.Equal(ENoteWorkflow.Changed, ex2.Message);
    }

    [Fact]
    public void Numbers_run_per_vertical_code_and_restart_each_financial_year()
    {
        Assert.Equal("2025-26", LeadRepository.FinancialYear(new DateOnly(2026, 3, 31)));
        Assert.Equal("2026-27", LeadRepository.FinancialYear(new DateOnly(2026, 4, 1)));
        var fy = LeadRepository.FinancialYear(App.Get<Fmt>().Today);
        var masters = App.Get<MasterRepository>();
        var db = App.Get<Db>();
        var verticals = masters.List(MasterTypes.Vertical, false);
        masters.SetCode(verticals.First(v => v.Name == "Rural Infrastructure").Id, "RURAL");
        masters.SetCode(verticals.First(v => v.Name == "HR & Administration").Id, null);    // no code -> GEN
        // a new code's register starts at 0001; last year's serials for the same code do not carry over
        db.Exec("INSERT INTO ENoteSerials (Code, FinancialYear, LastSerial) VALUES ('RURAL', '2000-01', 57)");
        var a = Draft("staff"); Mark(a, "staff", "approver1");                        // staff is in Rural Infrastructure
        var b = Draft("staff"); Mark(b, "staff", "approver1");
        var genBefore = db.Scalar<long>("SELECT COALESCE(MAX(LastSerial), 0) FROM ENoteSerials WHERE Code = 'GEN' AND FinancialYear = @fy", new { fy });
        var c = Draft("approver1"); Mark(c, "approver1", "approver2");             // HR & Administration
        Assert.Equal($"NCS/EN/RURAL/{fy}/0001", R.Get(a)!.NoteNumber);
        Assert.Equal($"NCS/EN/RURAL/{fy}/0002", R.Get(b)!.NoteNumber);
        Assert.Equal($"NCS/EN/GEN/{fy}/{genBefore + 1:D4}", R.Get(c)!.NoteNumber);
        Assert.Equal(57, db.Scalar<long>("SELECT LastSerial FROM ENoteSerials WHERE Code = 'RURAL' AND FinancialYear = '2000-01'"));
        Assert.Null(R.Get(Draft())!.NoteNumber);                                       // drafts have no number
    }

    [Fact]
    public void Send_back_returns_to_the_sender_and_any_holder_can_mark_to_an_earlier_person()
    {
        var id = Draft();
        Mark(id, "staff", "approver1");
        Mark(id, "approver1", "approver2", "For your comments.");
        W.SendBack(id, "Commented; back to you.", Rv(id), App.Principal("approver2"));
        Assert.Equal(Id("approver1"), R.Get(id)!.CurrentHolderUserId);
        Mark(id, "approver1", "coo", "Recommended.");
        Mark(id, "coo", "approver1", "Need level-2 comments first.");                 // marking back down the chain is allowed
        Assert.Equal(Id("approver1"), R.Get(id)!.CurrentHolderUserId);
        // sending back to the initiator = return to the initiator
        var n2 = Draft();
        Mark(n2, "staff", "approver1");
        W.SendBack(n2, "Not clear.", Rv(n2), App.Principal("approver1"));
        Assert.Equal(ENoteStates.Returned, R.Get(n2)!.State);
        Assert.Equal(Id("staff"), R.Get(n2)!.CurrentHolderUserId);
    }

    [Fact]
    public void Every_event_notifies_the_right_people_and_never_the_actor()
    {
        var n = App.Get<INotificationService>();
        long note = 0;
        long Count(string user, string type) => App.Get<Db>().Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE UserId = @u AND Type = @type AND EntityId = @note",
            new { u = Id(user), type, note });
        var id = Draft();
        note = id;
        Mark(id, "staff", "approver1");
        Assert.Equal(1, Count("approver1", "ENote.Received"));
        Assert.Equal(0, Count("staff", "ENote.Received"));
        W.Comment(id, "Looks fine", Rv(id), App.Principal("approver1"));
        Assert.Equal(1, Count("staff", "ENote.Commented"));                          // initiator only
        Assert.Equal(0, Count("approver1", "ENote.Commented"));
        Mark(id, "approver1", "approver2");
        W.SendBack(id, "Back", Rv(id), App.Principal("approver2"));
        Assert.Equal(1, Count("approver1", "ENote.SentBack"));
        Mark(id, "approver1", "coo");
        W.Decide(id, true, "Approved", Rv(id), App.Principal("coo"));
        foreach (var u in new[] { "staff", "approver1", "approver2" }) Assert.Equal(1, Count(u, "ENote.Approved"));
        Assert.Equal(0, Count("coo", "ENote.Approved"));                              // the approver is the actor

        var w = Draft(); note = w; Mark(w, "staff", "approver1");
        W.Withdraw(w, "No longer needed", Rv(w), App.Principal("staff"));
        Assert.Equal(1, Count("approver1", "ENote.Withdrawn"));
        Assert.Equal(0, Count("staff", "ENote.Withdrawn"));
        var r = Draft(); note = r; Mark(r, "staff", "approver1");
        W.ReturnToInitiator(r, "Revise", Rv(r), App.Principal("approver1"));
        Assert.Equal(1, Count("staff", "ENote.Returned"));
        _ = n;
    }

    [Fact]
    public async Task Ageing_alert_reaches_the_holder_once_per_day()
    {
        var id = Draft();
        Mark(id, "staff", "approver1");
        App.Get<Db>().Exec("UPDATE ENotes SET HeldSince = @old WHERE Id = @id", new { old = DateTime.UtcNow.AddDays(-9).ToString("o"), id });
        App.Get<HousekeepingService>().Run();
        App.Get<HousekeepingService>().Run();
        var alerts = App.Get<Db>().Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE Type = 'ENote.Ageing' AND UserId = @u", new { u = Id("approver1") });
        Assert.Equal(1, alerts);
        await Task.CompletedTask;
    }
}

public class ENoteVisibilityTests() : ENoteTestBase
{
    private sealed record Case(string Name, long Id, bool Confidential, string State, bool ApprovedRecord);

    private async Task<List<Case>> MakeAll()
    {
        var list = new List<Case>();
        foreach (var conf in new[] { false, true })
        {
            async Task<long> New(string what)
            {
                var id = Draft(confidential: conf, subject: $"{what} {(conf ? "confidential" : "open")} note");
                await Attach(id, "staff");
                return id;
            }
            var d = await New("Draft"); list.Add(new("Draft", d, conf, ENoteStates.Draft, false));
            var m = await New("Moving"); Mark(m, "staff", "approver1"); list.Add(new("InMovement", m, conf, ENoteStates.InMovement, false));
            var r = await New("Returned"); Mark(r, "staff", "approver1"); W.ReturnToInitiator(r, "x", Rv(r), App.Principal("approver1")); list.Add(new("Returned", r, conf, ENoteStates.Returned, false));
            var a = await New("Approved"); Mark(a, "staff", "approver1"); W.Decide(a, true, "ok", Rv(a), App.Principal("approver1")); list.Add(new("Approved", a, conf, ENoteStates.Approved, true));
            var j = await New("Rejected"); Mark(j, "staff", "approver1"); W.Decide(j, false, "no", Rv(j), App.Principal("approver1")); list.Add(new("Rejected", j, conf, ENoteStates.Rejected, false));
            var w = await New("Withdrawn"); Mark(w, "staff", "approver1"); W.Withdraw(w, "x", Rv(w), App.Principal("staff")); list.Add(new("Withdrawn", w, conf, ENoteStates.Withdrawn, false));
            var ca = await New("ClosedApproved"); Mark(ca, "staff", "approver1"); W.Decide(ca, true, "ok", Rv(ca), App.Principal("approver1")); W.Close(ca, Rv(ca), App.Principal("staff")); list.Add(new("Closed(approved)", ca, conf, ENoteStates.Closed, true));
            var cr = await New("ClosedRejected"); Mark(cr, "staff", "approver1"); W.Decide(cr, false, "no", Rv(cr), App.Principal("approver1")); W.Close(cr, Rv(cr), App.Principal("staff")); list.Add(new("Closed(rejected)", cr, conf, ENoteStates.Closed, false));
        }
        return list;
    }

    [Fact]
    public async Task Visibility_matrix_for_every_state_role_and_confidential_flag_including_url_guessing()
    {
        var cases = await MakeAll();
        // roles: initiator, participant, unrelated staff, auditor, administrator
        var users = new[] { "staff", "approver1", "publisher", "auditor", "admin" };
        var clients = new Dictionary<string, HttpClient>();
        foreach (var u in users) clients[u] = await App.SignIn(u);
        var failures = new List<string>();
        foreach (var k in cases)
        {
            var att = R.Attachments(k.Id).First().Id;
            foreach (var u in users)
            {
                bool view = u switch
                {
                    "staff" => true,
                    "approver1" => k.State != ENoteStates.Draft,
                    _ => k.ApprovedRecord && !k.Confidential,                            // everyone else: approved, non-confidential only
                };
                bool pdf = u == "staff" && ENoteStates.IsDecided(k.State);
                bool print = view && k.State != ENoteStates.Draft;
                async Task Check(string url, bool allowed)
                {
                    var r = await clients[u].GetAsync(url);
                    var ok = r.StatusCode == HttpStatusCode.OK;
                    if (ok != allowed) failures.Add($"{k.Name}{(k.Confidential ? "/conf" : "")} {u} {url}: {(int)r.StatusCode}, expected {(allowed ? "200" : "denied")}");
                }
                await Check($"/ENotes/{k.Id}", view);
                await Check($"/ENotes/Attachment/{att}", view);
                await Check($"/ENotes/{k.Id}/Attachments.zip", view);
                await Check($"/ENotes/{k.Id}/Print", print);
                await Check($"/ENotes/{k.Id}/Pdf", pdf);
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        // administrators and auditors: the register shows metadata only — never the text — and hides confidential subjects
        foreach (var u in new[] { "admin", "auditor" })
        {
            var html = await clients[u].GetStringAsync("/ENotes/Register");
            Assert.Contains("Moving open note", html);
            Assert.DoesNotContain("Moving confidential note", html);
            Assert.DoesNotContain("Para one.", html);
            Assert.DoesNotContain("Draft open note", html);                             // drafts are not in the register
        }
        Assert.Equal(HttpStatusCode.Redirect, (await clients["publisher"].GetAsync("/ENotes/Register")).StatusCode);   // access denied
        var x = await clients["admin"].GetAsync("/ENotes/Register?handler=Export");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", x.Content.Headers.ContentType!.MediaType);

        // "Approved notes (all staff)" lists approved, non-confidential notes for anyone
        var approvedTab = await clients["publisher"].GetStringAsync("/ENotes?tab=approved");
        Assert.Contains("Approved open note", approvedTab);
        Assert.Contains("ClosedApproved open note", approvedTab);
        Assert.DoesNotContain("Approved confidential note", approvedTab);
        Assert.DoesNotContain("Rejected open note", approvedTab);
        // search uses the full-text index but never widens visibility
        var search = await clients["publisher"].GetStringAsync("/ENotes?tab=seen&q=Moving");
        Assert.DoesNotContain("Moving open note", search);
        var mine = await clients["staff"].GetStringAsync("/ENotes?tab=initiated&q=Moving");
        Assert.Contains("Moving open note", mine);
        Assert.DoesNotContain("Approved open note", mine);
    }

    [Fact]
    public async Task Approved_visibility_can_be_limited_to_participants()
    {
        using var app = new TestApp(("ENote__ApprovedVisibility", "Participants"));
        var w = app.Get<ENoteWorkflow>(); var r = app.Get<ENoteRepository>();
        var id = w.Create(new ENoteWorkflow.DraftInput("Participants only", "Procurement", "Body", false, true), app.Principal("staff"));
        w.MarkTo(id, app.User("approver1").Id, null, r.Get(id)!.RowVersion, app.Principal("staff"));
        w.Decide(id, true, "ok", r.Get(id)!.RowVersion, app.Principal("approver1"));
        var pub = await app.SignIn("publisher");
        Assert.Equal(HttpStatusCode.NotFound, (await pub.GetAsync($"/ENotes/{id}")).StatusCode);
        Assert.DoesNotContain("Approved notes (all staff)", await pub.GetStringAsync("/ENotes"));
    }
}

public class ENoteUploadTests() : ENoteTestBase(("ENote__MaxFileMB", "1"), ("ENote__MaxNoteTotalMB", "2"), ("ENote__MaxFilesPerNote", "3"))
{
    private static byte[] PdfOfSize(int bytes)
    {
        var b = new byte[bytes];
        Pdf.CopyTo(b, 0);
        return b;
    }

    [Fact]
    public async Task Size_count_quota_and_file_signature_limits_are_enforced()
    {
        var id = Draft();
        var tooBig = await Assert.ThrowsAsync<WorkflowException>(() => Attach(id, "staff", "big.pdf", PdfOfSize(1024 * 1024 + 10)));
        Assert.Contains("1 MB", tooBig.Message);
        var fake = await Assert.ThrowsAsync<WorkflowException>(() => Attach(id, "staff", "notreal.pdf", "this is just text"u8.ToArray()));
        Assert.Contains("genuine", fake.Message);
        await Assert.ThrowsAsync<WorkflowException>(() => Attach(id, "staff", "tool.exe", new byte[] { 0x4D, 0x5A, 0, 0 }));
        await Attach(id, "staff", "a.pdf", PdfOfSize(900 * 1024));
        await Attach(id, "staff", "b.pdf", PdfOfSize(900 * 1024));
        var quota = await Assert.ThrowsAsync<WorkflowException>(() => Attach(id, "staff", "c.pdf", PdfOfSize(400 * 1024)));   // 2.2 MB > 2 MB
        Assert.Contains("2 MB", quota.Message);
        await Attach(id, "staff", "c.txt", "small"u8.ToArray());
        var count = await Assert.ThrowsAsync<WorkflowException>(() => Attach(id, "staff", "d.txt", "small"u8.ToArray()));   // 4th file
        Assert.Contains("3", count.Message);
        Assert.Equal(3, R.Attachments(id).Count);
        // rejected uploads leave no files behind
        var folder = Path.Combine(App.Root, "documents", "enotes");
        Assert.Equal(3, Directory.GetFiles(folder, "*.dat", SearchOption.AllDirectories).Length);
        Assert.Empty(W.OrphanFiles());
    }
}

public class ENotePdfTests(Xunit.Abstractions.ITestOutputHelper output) : ENoteTestBase
{
    [Fact]
    public async Task Pdf_only_in_final_states_cached_and_footer_hash_matches_the_record()
    {
        var id = Draft(body: "English paragraph.\n\nहिंदी अनुच्छेद — क्षेत्र भ्रमण।");
        await Attach(id, "staff");
        Mark(id, "staff", "approver1");
        var pdf = App.Get<ENotePdfService>();
        await Assert.ThrowsAsync<WorkflowException>(() => pdf.GetOrCreateAsync(id));      // not final yet
        W.Decide(id, true, "Approved.", Rv(id), App.Principal("approver1"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (path1, sha1) = await pdf.GetOrCreateAsync(id);
        var firstMs = sw.ElapsedMilliseconds;
        var bytes = await System.IO.File.ReadAllBytesAsync(path1);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(sha1, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        var n = R.Get(id)!;
        Assert.Equal(pdf.Record(n).Hash(), n.RecordSha256);                                 // the hash in the footer = the record recomputed
        Assert.Contains("enotes/pdf/", n.PdfPath);
        Assert.True(firstMs < 3000, $"PDF took {firstMs} ms");

        long Generated() => App.Get<Db>().Scalar<long>("SELECT COUNT(*) FROM AuditLog WHERE Action = 'ENote.PdfGenerated' AND EntityId = @id", new { id = id.ToString() });
        var (path2, sha2) = await pdf.GetOrCreateAsync(id);                                 // served from the stored copy
        Assert.Equal((path1, sha1), (path2, sha2));
        Assert.Equal(1, Generated());

        W.Close(id, Rv(id), App.Principal("staff"));                                       // state changes again -> regenerated once
        var (path3, sha3) = await pdf.GetOrCreateAsync(id);
        Assert.NotEqual(path1, path3);
        Assert.NotEqual(sha1, sha3);
        Assert.Equal(2, Generated());
        Assert.True(System.IO.File.Exists(path1));                                          // the earlier PDF is kept

        // download over HTTP: the initiator only, as a file
        var c = await App.SignIn("staff");
        var r = await c.GetAsync($"/ENotes/{id}/Pdf");
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(HttpStatusCode.NotFound, (await (await App.SignIn("approver1")).GetAsync($"/ENotes/{id}/Pdf")).StatusCode);
    }

    [Fact]
    public async Task Thirty_page_note_renders_within_budget()
    {
        var para = string.Join(" ", Enumerable.Repeat("The evaluation covers watershed works, livelihoods and institutions in twelve districts; क्षेत्र भ्रमण आवश्यक है।", 6));
        var body = string.Join("\n\n", Enumerable.Range(1, 120).Select(i => $"{i}: {para}"));
        var id = Draft(body: body[..Math.Min(body.Length, 50_000)]);
        Mark(id, "staff", "approver1");
        for (var i = 0; i < 70; i++) W.Comment(id, $"Comment {i}: " + para, Rv(id), App.Principal("approver1"));
        W.Decide(id, true, "Approved.", Rv(id), App.Principal("approver1"));
        var pdf = App.Get<ENotePdfService>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (path, _) = await pdf.GetOrCreateAsync(id);
        var ms = sw.ElapsedMilliseconds;
        var text = System.Text.Encoding.Latin1.GetString(await System.IO.File.ReadAllBytesAsync(path));
        var pages = System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
        output.WriteLine($"{pages}-page PDF generated in {ms} ms");
        Assert.True(pages >= 25, $"expected about 30 pages, got {pages}");
        Assert.True(ms < 3000, $"{pages}-page PDF took {ms} ms (budget 3 s)");
    }
}

public class ENoteMigrationTests() : ENoteTestBase
{
    [Fact]
    public async Task Migration_is_idempotent_and_backups_include_enote_files()
    {
        var db = App.Get<Db>();
        Assert.Empty(db.Initialize());                                                      // second run: nothing to do
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM SchemaInfo WHERE Version = 5"));
        Assert.True(Schema.Version >= 5);
        var id = Draft();
        await Attach(id, "staff");
        Mark(id, "staff", "approver1");
        var backup = App.Get<BackupService>().Create(App.Get<AuditService>(), "test");
        using var zip = ZipFile.OpenRead(backup.FullPath);
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("documents/enotes/", StringComparison.Ordinal) && e.FullName.EndsWith(".dat", StringComparison.Ordinal));
        Assert.True(App.Get<AuditService>().Verify().ok);
    }

    [Fact]
    public void Restarting_never_sets_vertical_codes_on_an_existing_list()
    {
        var masters = App.Get<MasterRepository>();
        foreach (var v in masters.List(MasterTypes.Vertical, false)) masters.SetCode(v.Id, null);
        App.Get<Seeder>().Run();                                                     // runs at every start
        Assert.All(masters.List(MasterTypes.Vertical, false), v => Assert.Null(v.Code));
    }

    [Fact]
    public async Task Attachments_zip_streams_every_file()
    {
        var id = Draft();
        await Attach(id, "staff", "one.pdf");
        await Attach(id, "staff", "two.pdf");
        var c = await App.SignIn("staff");
        var r = await c.GetAsync($"/ENotes/{id}/Attachments.zip");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var zip = new ZipArchive(await r.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        Assert.Equal(new[] { "01 one.pdf", "02 two.pdf" }, zip.Entries.Select(e => e.FullName).ToArray());
        using var s = zip.Entries[0].Open();
        using var ms = new MemoryStream(); s.CopyTo(ms);
        Assert.Equal(Pdf, ms.ToArray());
    }

    [Fact]
    public async Task Pages_work_end_to_end_over_http_including_mark_to_without_javascript()
    {
        var c = await App.SignIn("staff");
        var token = await TestApp.Token(c, "/ENotes/New");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("Office stationery"), "Subject" },
            { new StringContent("Procurement"), "Category" }, { new StringContent("Para 1.\n\nPara 2."), "Body" }, { new StringContent("true"), "Declaration" },
        };
        var f = new ByteArrayContent(Pdf); f.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(f, "uploads", "quote.pdf");
        var r = await c.PostAsync("/ENotes/New", form);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var id = long.Parse(r.Headers.Location!.ToString().Split('/').Last());
        Assert.Single(R.Attachments(id));
        // no JavaScript: the typed name is resolved on the server
        var tok = await TestApp.Token(c, $"/ENotes/{id}");
        r = await c.PostAsync($"/ENotes/{id}?handler=Mark", TestApp.Form(tok, ("toQuery", "Rajesh Iyer"), ("toUserId", ""), ("remarks", ""), ("rowVersion", Rv(id).ToString())));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(Id("approver1"), R.Get(id)!.CurrentHolderUserId);
        // the type-ahead endpoint: at least 2 characters, at most 10 results, active users only
        var a1 = await App.SignIn("approver1");
        Assert.Equal("[]", await a1.GetStringAsync("/api/users/lookup?q=a"));
        Assert.Contains("Anita Deshmukh", await a1.GetStringAsync("/api/users/lookup?q=anit"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.NewClient().GetAsync("/api/users/lookup?q=anit")).StatusCode);
        // a stale form shows "This note has changed"
        var tok2 = await TestApp.Token(a1, $"/ENotes/{id}");
        var stale = Rv(id) - 1;
        r = await a1.PostAsync($"/ENotes/{id}?handler=Comment", TestApp.Form(tok2, ("text", "late"), ("rowVersion", stale.ToString())));
        var page = await a1.GetStringAsync($"/ENotes/{id}");
        Assert.Contains("This note has changed", System.Net.WebUtility.HtmlDecode(page));
    }
}
