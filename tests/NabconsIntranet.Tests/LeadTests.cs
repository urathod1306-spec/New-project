using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Tests;

public class LeadTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private LeadRepository Leads => _app.Get<LeadRepository>();

    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    private async Task<(long id, string reference)> Submit(HttpClient c, string title = "Evaluation of watershed programme", string client = "State Rural Livelihood Mission",
        string? tender = null, bool confirm = true, params (string name, byte[] bytes)[] files)
    {
        var token = await TestApp.Token(c, "/Leads/New");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(title), "Input.Title" },
            { new StringContent(client), "Input.ClientName" },
            { new StringContent("State Govt"), "Input.ClientType" },
            { new StringContent("Third-party evaluation across 12 districts."), "Input.Description" },
            { new StringContent(confirm ? "true" : "false"), "ConfirmDuplicate" },
        };
        if (tender != null) form.Add(new StringContent(tender), "Input.TenderNumber");
        foreach (var (name, bytes) in files)
        {
            var f = new ByteArrayContent(bytes);
            f.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(f, "Uploads", name);
        }
        var r = await c.PostAsync("/Leads/New", form);
        if (r.StatusCode != HttpStatusCode.Redirect) return (0, await r.Content.ReadAsStringAsync());
        var id = long.Parse(r.Headers.Location!.ToString().Split('/').Last());
        return (id, Leads.Get(id)!.Reference);
    }

    [Fact]
    public async Task Submission_with_attachments_creates_a_lead_with_a_reference()
    {
        var c = await _app.SignIn("staff");
        var (id, reference) = await Submit(c, files: new[] { ("proposal.pdf", Pdf), ("scope.pdf", Pdf) });
        Assert.True(id > 0, reference);
        Assert.Matches(@"^NCS/BD/\d{4}-\d{2}/0001$", reference);
        var lead = Leads.Get(id)!;
        Assert.Equal(LeadStatus.Submitted, lead.Status);
        Assert.Equal(2, Leads.Attachments(id).Count);
        Assert.All(Leads.Attachments(id), a => Assert.Matches(@"^leads/\d{4}/[0-9a-f]{32}\.dat$", a.StoredPath)); // random server-side names
        Assert.Contains(reference, await c.GetStringAsync("/Leads/Mine"));
    }

    [Fact]
    public async Task Disguised_attachment_is_rejected_and_nothing_is_created()
    {
        var c = await _app.SignIn("staff");
        var exe = new byte[] { 0x4D, 0x5A }.Concat(new byte[3000]).ToArray();
        var (id, html) = await Submit(c, files: new[] { ("tender.pdf", exe) });
        Assert.Equal(0, id);
        Assert.Contains("does not match a genuine .pdf", html);
        Assert.Empty(Leads.Mine(_app.User("staff").Id));
    }

    [Fact]
    public void Financial_year_rolls_over_on_1_April_and_the_serial_restarts()
    {
        Assert.Equal("2026-27", LeadRepository.FinancialYear(new DateOnly(2027, 3, 31)));
        Assert.Equal("2027-28", LeadRepository.FinancialYear(new DateOnly(2027, 4, 1)));
        Assert.Equal("2099-00", LeadRepository.FinancialYear(new DateOnly(2099, 12, 1)));
        var who = _app.Principal("staff");
        Lead New() => new() { Title = "T", ClientName = "C", Description = "D" };
        Assert.Equal("NCS/BD/2026-27/0001", Leads.Create(New(), who, new DateOnly(2027, 3, 30)).Reference);
        Assert.Equal("NCS/BD/2026-27/0002", Leads.Create(New(), who, new DateOnly(2027, 3, 31)).Reference);
        Assert.Equal("NCS/BD/2027-28/0001", Leads.Create(New(), who, new DateOnly(2027, 4, 1)).Reference);
        Assert.Equal("NCS/BD/2026-27/0003", Leads.Create(New(), who, new DateOnly(2027, 3, 31)).Reference); // late entry for the old FY
    }

    [Fact]
    public async Task Simultaneous_submissions_never_share_a_reference()
    {
        var who = _app.Principal("staff");
        var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(() =>
            Leads.Create(new Lead { Title = "Concurrent " + i, ClientName = "Client " + i, Description = "D" }, who, new DateOnly(2026, 10, 17)).Reference));
        var refs = await Task.WhenAll(tasks);
        Assert.Equal(40, refs.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 40).Select(n => $"NCS/BD/2026-27/{n:D4}"), refs.OrderBy(r => r));
    }

    [Fact]
    public async Task Duplicate_is_flagged_before_confirming()
    {
        var c = await _app.SignIn("staff");
        var (_, first) = await Submit(c, tender: "SRLM/2026/14");
        var (id, html) = await Submit(c, title: "Something else entirely", client: "Other client", tender: "srlm-2026-14", confirm: false);
        Assert.Equal(0, id);
        Assert.Contains(first, html);
        var json = await c.GetStringAsync("/Leads/New?handler=Check&client=" + Uri.EscapeDataString("State Rural Livelihood Mission") + "&title=" + Uri.EscapeDataString("Watershed programme evaluation"));
        Assert.Contains(first, json);
        var (id2, _) = await Submit(c, title: "Something else entirely", client: "Other client", tender: "srlm-2026-14", confirm: true);
        Assert.True(id2 > 0);
    }

    [Fact]
    public async Task Non_BDU_users_get_403_on_pipeline_other_peoples_leads_and_their_attachments()
    {
        var owner = await _app.SignIn("staff");
        var (id, _) = await Submit(owner, files: new[] { ("p.pdf", Pdf) });
        var attId = Leads.Attachments(id).Single().Id;

        foreach (var user in new[] { "publisher", "auditor", "admin", "approver1" })
        {
            var c = await _app.SignIn(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/Leads")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/Leads/Dashboard")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/Leads?handler=Export")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/Leads/Details/{id}")).StatusCode);       // ID in the URL changed
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/Leads/Attachment/{attId}")).StatusCode);
            // and cannot change its status by posting directly
            var token = await TestApp.Token(c, "/Leads/New");
            var r = await c.PostAsync($"/Leads/Details/{id}?handler=Status", TestApp.Form(token, ("newStatus", LeadStatus.Dropped), ("remarks", "x"), ("lossReason", "Other"), ("lossReasonText", "x"), ("rowVersion", "1")));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        Assert.Equal(LeadStatus.Submitted, Leads.Get(id)!.Status);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/Leads/Details/{id}")).StatusCode);              // the submitter can
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/Leads/Attachment/{attId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/Leads")).StatusCode);                     // but not the pipeline
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NewClient().GetAsync($"/Leads/Details/{id}")).StatusCode); // anonymous -> sign-in
    }

    private async Task<HttpResponseMessage> PostStatus(HttpClient c, long id, params (string, string)[] fields)
    {
        var html = await c.GetStringAsync($"/Leads/Details/{id}");
        var token = await TestApp.Token(c, $"/Leads/Details/{id}", "handler=Status");
        var rv = Regex.Match(html, "name=\"rowVersion\" value=\"(\\d+)\"").Groups[1].Value;
        return await c.PostAsync($"/Leads/Details/{id}?handler=Status", TestApp.Form(token, fields.Append(("rowVersion", rv)).ToArray()));
    }

    [Fact]
    public async Task BDU_and_COO_can_update_status_and_Lost_or_Dropped_need_a_reason()
    {
        var (id, _) = await Submit(await _app.SignIn("staff"));
        var bdu = await _app.SignIn("bdu");
        Assert.Equal(HttpStatusCode.OK, (await bdu.GetAsync("/Leads")).StatusCode);

        await PostStatus(bdu, id, ("newStatus", LeadStatus.Lost), ("remarks", "Lost on price"));
        Assert.Equal(LeadStatus.Submitted, Leads.Get(id)!.Status);                  // no reason -> rejected
        await PostStatus(bdu, id, ("newStatus", LeadStatus.Dropped), ("remarks", "Not for us"), ("lossReason", "Other"));
        Assert.Equal(LeadStatus.Submitted, Leads.Get(id)!.Status);                  // "Other" without text -> rejected
        await PostStatus(bdu, id, ("newStatus", LeadStatus.UnderReview), ("remarks", ""));
        Assert.Equal(LeadStatus.Submitted, Leads.Get(id)!.Status);                  // remark required
        await PostStatus(bdu, id, ("newStatus", LeadStatus.UnderReview), ("remarks", "Checking eligibility"));
        Assert.Equal(LeadStatus.UnderReview, Leads.Get(id)!.Status);

        var coo = await _app.SignIn("coo");                                          // LeadViewer role
        Assert.Equal(HttpStatusCode.OK, (await coo.GetAsync("/Leads/Dashboard")).StatusCode);
        await PostStatus(coo, id, ("newStatus", LeadStatus.Won), ("remarks", "LoA received"));
        Assert.Equal(LeadStatus.UnderReview, Leads.Get(id)!.Status);               // Won needs value and date
        await PostStatus(coo, id, ("newStatus", LeadStatus.Won), ("remarks", "LoA received"), ("awardedValueLakh", "42.5"), ("awardDate", "2026-10-15"));
        var won = Leads.Get(id)!;
        Assert.Equal(LeadStatus.Won, won.Status);
        Assert.Equal(42.5, won.AwardedValueLakh);
        await PostStatus(bdu, id, ("newStatus", LeadStatus.Lost), ("remarks", "Client cancelled award"), ("lossReason", "Client cancelled"));
        var lost = Leads.Get(id)!;
        Assert.Equal(LeadStatus.Lost, lost.Status);                                  // backward/any move allowed with a remark
        Assert.Equal("Client cancelled", lost.LossReason);
        var timeline = Leads.Timeline(id);
        Assert.Equal(new[] { "Submitted", "StatusChanged", "StatusChanged", "StatusChanged" }, timeline.Select(e => e.EventType));
        Assert.All(timeline.Skip(1), e => Assert.False(string.IsNullOrEmpty(e.Remarks)));
    }

    [Fact]
    public async Task Owner_must_be_a_BDU_member()
    {
        var (id, _) = await Submit(await _app.SignIn("staff"));
        var lead = Leads.Get(id)!;
        var coo = _app.Principal("coo");
        Assert.Throws<WorkflowException>(() => Leads.AssignOwner(lead, _app.User("publisher").Id, lead.RowVersion, coo));
        Leads.AssignOwner(lead, _app.User("bdu").Id, lead.RowVersion, coo);
        Assert.Equal(_app.User("bdu").Id, Leads.Get(id)!.OwnerUserId);
    }

    [Fact]
    public async Task Timeline_is_append_only()
    {
        var (id, _) = await Submit(await _app.SignIn("staff"), files: new[] { ("p.pdf", Pdf) });
        var db = _app.Get<Db>();
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => db.Exec("UPDATE LeadEvents SET Remarks = 'changed' WHERE LeadId = @id", new { id }));
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => db.Exec("DELETE FROM LeadEvents WHERE LeadId = @id", new { id }));
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => db.Exec("DELETE FROM LeadAttachments"));
        Assert.Equal(2, Leads.Timeline(id).Count); // submitted + attachment, untouched
        Assert.Single(Leads.Attachments(id));
    }

    [Fact]
    public async Task Submitter_follow_up_only_while_Submitted_or_Under_Review()
    {
        var staff = await _app.SignIn("staff");
        var (id, _) = await Submit(staff);
        async Task<HttpStatusCode> Note()
        {
            var token = await TestApp.Token(staff, "/Leads/New");
            using var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("follow-up"), "note" } };
            return (await staff.PostAsync($"/Leads/Details/{id}?handler=Note", form)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Redirect, await Note());
        var lead = Leads.Get(id)!;
        Leads.ChangeStatus(lead, new LeadUpdate(LeadStatus.Preparing, "Preparing documents", null, null, null, null), lead.RowVersion, _app.Principal("bdu"));
        Assert.Equal(HttpStatusCode.Forbidden, await Note());
    }

    [Fact]
    public async Task Excel_export_is_a_valid_workbook_with_the_filtered_rows()
    {
        await Submit(await _app.SignIn("staff"), title: "Alpha survey", client: "Client A");
        await Submit(await _app.SignIn("staff"), title: "Beta study", client: "Client B");
        var bdu = await _app.SignIn("bdu");
        var r = await bdu.GetAsync("/Leads?handler=Export&q=Alpha");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType!.MediaType);
        using var zip = new System.IO.Compression.ZipArchive(await r.Content.ReadAsStreamAsync());
        var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Assert.Contains("Alpha survey", sheet);
        Assert.DoesNotContain("Beta study", sheet);
    }
}
