using System.Globalization;
using System.Reflection;
using System.Text;
using NabconsIntranet.Data;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace NabconsIntranet.Infrastructure;

/// <summary>Everything that goes into an archival PDF (and into the record hash printed in its footer).</summary>
public sealed record ENoteRecord(ENote Note, ENoteBodyVersion? Body, List<ENoteSheetEntry> Sheet, List<ENoteAttachment> Attachments)
{
    /// <summary>
    /// SHA-256 over a fixed text form of the record: number, subject, initiator, states, the body version and its hash,
    /// every note-sheet entry, every attachment's hash and the decision. Anyone can recompute it from the database.
    /// </summary>
    public string Hash()
    {
        var n = Note;
        var sb = new StringBuilder("NABCONS e-Note record v1\n");
        void L(params object?[] parts) => sb.Append(string.Join("|", parts.Select(p => p?.ToString() ?? ""))).Append('\n');
        L("note", n.NoteNumber, n.Subject, n.Category, n.InitiatorName, n.InitiatorDesignation, n.InitiatorVertical, n.CreatedAt, n.SubmittedAt);
        L("state", n.State, n.Outcome, n.IsConfidential ? "confidential" : "");
        L("body", Body?.Version, Body?.Sha256);
        foreach (var e in Sheet) L("sheet", e.Seq, e.Kind, e.Action, e.Name, e.Designation, e.Timestamp, e.ToName, e.ToDesignation, e.ToState, e.Text);
        foreach (var a in Attachments) L("file", a.Id, a.OriginalFileName, a.SizeBytes, a.Sha256, a.UploadedByName, a.UploadedAt, a.Stage, a.SupersededById);
        L("decision", n.DecidedByName, n.DecidedByDesignation, n.DecidedAt, n.DecisionRemarks);
        L("closed", n.ClosedAt);
        return ENoteRepository.Sha(sb.ToString());
    }
}

/// <summary>
/// Archival PDF of a decided e-Note, generated on the server with SkiaSharp (already bundled) and HarfBuzz text shaping,
/// using the Noto Sans and Noto Sans Devanagari fonts embedded in the program, so Hindi and English both print
/// correctly without any installed fonts. Generated once per final state and stored in documents\enotes\pdf.
/// At most two PDFs are generated at the same time.
/// </summary>
public sealed class ENotePdfService(Db db, ENoteRepository notes, IntranetOptions io, AuditService audit, Fmt fmt, IWebHostEnvironment env)
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public ENoteRecord Record(ENote n) => new(n, notes.Version(n.Id, null), notes.Sheet(n.Id), notes.Attachments(n.Id));

    private static string Key(ENote n) => n.State + "|" + n.Outcome;

    /// <summary>Returns the stored PDF for the note's current final state, generating it the first time.</summary>
    public async Task<(string fullPath, string sha256)> GetOrCreateAsync(long noteId)
    {
        var n = notes.Get(noteId) ?? throw new WorkflowException("Note not found.");
        if (!ENoteStates.IsDecided(n.State)) throw new WorkflowException("The PDF is available once the note is approved, rejected, withdrawn or closed.");
        if (Cached(n) is { } hit) return hit;
        await Gate.WaitAsync();
        try
        {
            n = notes.Get(noteId)!;
            if (Cached(n) is { } again) return again;
            var record = Record(n);
            var hash = record.Hash();
            var bytes = Render(record, hash);
            var relative = $"enotes/pdf/{n.Id}-{n.State.ToLowerInvariant()}.pdf";
            var full = Path.Combine(io.DocumentsPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var tmp = full + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, full, overwrite: true);
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            db.Exec("UPDATE ENotes SET PdfPath = @relative, PdfSha256 = @sha, PdfState = @key, RecordSha256 = @hash WHERE Id = @id",
                new { relative, sha, key = Key(n), hash, id = n.Id });
            audit.Log("ENote.PdfGenerated", "ENote", n.Id, $"{n.DisplayNumber}: {n.FinalLabel}; PDF SHA-256 {sha}; record SHA-256 {hash}");
            return (full, sha);
        }
        finally { Gate.Release(); }
    }

    private (string, string)? Cached(ENote n)
    {
        if (n.PdfState != Key(n) || n.PdfPath == null || n.PdfSha256 == null) return null;
        var full = Path.Combine(io.DocumentsPath, n.PdfPath);
        return File.Exists(full) ? (full, n.PdfSha256) : null;
    }

    // ================================================================== rendering

    private const float W = 595, H = 842, ML = 56, MR = 56, MT = 50, MB = 72, CW = W - ML - MR;

    private sealed class Fonts : IDisposable
    {
        public readonly SKTypeface Latin, LatinBold, Deva, DevaBold;
        public readonly SKShaper SLatin, SLatinBold, SDeva, SDevaBold;
        public Fonts()
        {
            Latin = Load("NotoSans-Regular.ttf"); LatinBold = Load("NotoSans-Bold.ttf");
            Deva = Load("NotoSansDevanagari-Regular.ttf"); DevaBold = Load("NotoSansDevanagari-Bold.ttf");
            SLatin = new SKShaper(Latin); SLatinBold = new SKShaper(LatinBold); SDeva = new SKShaper(Deva); SDevaBold = new SKShaper(DevaBold);
        }
        private static SKTypeface Load(string name)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fonts." + name) ?? throw new InvalidOperationException("Missing font " + name);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return SKTypeface.FromData(SKData.CreateCopy(ms.ToArray()));
        }
        public void Dispose()
        {
            foreach (var s in new[] { SLatin, SLatinBold, SDeva, SDevaBold }) s.Dispose();
            foreach (var t in new[] { Latin, LatinBold, Deva, DevaBold }) t.Dispose();
        }
    }

    private sealed record Style(float Size, bool Bold, SKColor Color);
    private static readonly SKColor Ink = new(0x1d, 0x27, 0x33), Muted = new(0x5d, 0x6b, 0x7a), Brand = new(0x0b, 0x4b, 0x28), Accent = new(0x8a, 0x16, 0x16);
    private static readonly Style Body = new(10.5f, false, Ink), Small = new(8.5f, false, Muted), Label = new(9f, true, Muted), H1 = new(15f, true, Brand),
        H2 = new(11.5f, true, Brand), Strong = new(10.5f, true, Ink), Tiny = new(7f, false, Muted);

    private abstract record Op;
    private sealed record TextOp(float X, float Y, string Text, Style S) : Op;
    private sealed record LineOp(float X1, float Y1, float X2, float Y2, SKColor Color, float Width) : Op;
    private sealed record RectOp(SKRect R, SKColor Fill) : Op;
    private sealed record ImageOp(SKRect R) : Op;

    /// <summary>Lays text out page by page (two passes, so the footer can say "Page x of y").</summary>
    private sealed class Layout(Fonts f)
    {
        public readonly List<List<Op>> Pages = new() { new() };
        public float Y = MT;
        private readonly Dictionary<(string, bool, float), float> _widths = new();
        private List<Op> Page => Pages[^1];

        public void NewPage() { Pages.Add(new()); Y = MT + 18; }
        public void Need(float h) { if (Y + h > H - MB) NewPage(); }
        public void Add(Op op) => Page.Add(op);

        public static bool IsDeva(char ch) => ch is >= 'ऀ' and <= 'ॿ' or >= '꣠' and <= 'ꣿ' or >= '᳐' and <= '᳿' or '‌' or '‍';

        /// <summary>Splits text into runs of Devanagari / other, so each run is shaped with the right font.</summary>
        public static IEnumerable<(string text, bool deva)> Runs(string s)
        {
            if (s.Length == 0) yield break;
            var sb = new StringBuilder();
            bool? cur = null;
            foreach (var ch in s)
            {
                var d = IsDeva(ch) || (cur == true && (char.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark));
                if (cur != null && d != cur) { yield return (sb.ToString(), cur.Value); sb.Clear(); }
                cur = d; sb.Append(ch);
            }
            if (sb.Length > 0) yield return (sb.ToString(), cur!.Value);
        }

        public (SKShaper shaper, SKFont font) Font(bool deva, Style st)
        {
            var tf = deva ? (st.Bold ? f.DevaBold : f.Deva) : (st.Bold ? f.LatinBold : f.Latin);
            var sh = deva ? (st.Bold ? f.SDevaBold : f.SDeva) : (st.Bold ? f.SLatinBold : f.SLatin);
            return (sh, new SKFont(tf, st.Size) { Subpixel = true });
        }

        public float Width(string text, Style st)
        {
            if (_widths.TryGetValue((text, st.Bold, st.Size), out var w)) return w;
            w = 0;
            foreach (var (run, deva) in Runs(text))
            {
                var (sh, font) = Font(deva, st);
                using (font) w += sh.Shape(run, font).Width;
            }
            return _widths[(text, st.Bold, st.Size)] = w;
        }

        /// <summary>Greedy word wrap; words longer than the line (e.g. hashes) are broken by character.</summary>
        public List<string> Wrap(string text, Style st, float width)
        {
            var lines = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = "";
                foreach (var word0 in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var word = word0;
                    while (Width(word, st) > width)
                    {
                        if (line.Length > 0) { lines.Add(line); line = ""; }
                        var e = StringInfo.GetTextElementEnumerator(word);
                        var take = new StringBuilder();
                        while (e.MoveNext() && Width(take + (string)e.Current, st) <= width) take.Append((string)e.Current);
                        if (take.Length == 0) take.Append(word[0]);
                        lines.Add(take.ToString());
                        word = word[take.Length..];
                    }
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Width(candidate, st) <= width) line = candidate;
                    else { lines.Add(line); line = word; }
                }
                lines.Add(line);
            }
            return lines;
        }

        public void Text(string text, Style st, float x = 0, float width = CW, float after = 2, string? hanging = null)
        {
            var lh = st.Size * 1.42f;
            var lines = Wrap(text, st, width - x - (hanging != null ? 22 : 0));
            for (var i = 0; i < lines.Count; i++)
            {
                Need(lh);
                if (i == 0 && hanging != null) Add(new TextOp(ML + x, Y + st.Size, hanging, st));
                Add(new TextOp(ML + x + (hanging != null ? 22 : 0), Y + st.Size, lines[i], st));
                Y += lh;
            }
            Y += after;
        }

        public void Rule(float gapBefore = 4, float gapAfter = 8, SKColor? color = null)
        {
            Y += gapBefore; Need(2);
            Add(new LineOp(ML, Y, W - MR, Y, color ?? new SKColor(0xdd, 0xe3, 0xea), 0.8f));
            Y += gapAfter;
        }

        public void Pair(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var lh = Body.Size * 1.42f;
            var lines = Wrap(value, Body, CW - 130);
            Need(lh * Math.Min(lines.Count, 3));
            Add(new TextOp(ML, Y + Body.Size, label, Label));
            foreach (var l in lines) { Need(lh); Add(new TextOp(ML + 130, Y + Body.Size, l, Body)); Y += lh; }
            Y += 1;
        }
    }

    private string When(string? iso) => fmt.DateTime(iso);

    private static string Movement(ENoteSheetEntry e) => e.Action switch
    {
        "Created" => "Draft created",
        "Submitted" => $"Submitted and marked to {Who(e)}",
        "Resubmitted" => $"Re-submitted after revision and marked to {Who(e)}",
        "MarkedTo" => $"Marked to {Who(e)}",
        "SentBack" => e.ToState == ENoteStates.Returned ? $"Sent back to the initiator, {Who(e)}" : $"Sent back to {Who(e)}",
        "ReturnedToInitiator" => $"Returned to the initiator, {Who(e)}",
        ENoteStates.Approved => "APPROVED",
        ENoteStates.Rejected => "REJECTED",
        "Withdrawn" => "Withdrawn by the initiator",
        "Closed" => "Closed / filed by the initiator",
        _ => e.Action ?? "",
    };

    private static string Who(ENoteSheetEntry e) => e.ToName + (string.IsNullOrEmpty(e.ToDesignation) ? "" : $" ({e.ToDesignation})");

    public byte[] Render(ENoteRecord r, string recordHash)
    {
        var n = r.Note;
        using var fonts = new Fonts();
        var L = new Layout(fonts);

        // ---- 1. organisation header
        L.Add(new ImageOp(new SKRect(ML, MT - 4, ML + 46, MT + 42)));
        L.Add(new TextOp(ML + 58, MT + 14, io.OrganisationName, new Style(13, true, Brand)));
        L.Add(new TextOp(ML + 58, MT + 30, "e-Note — record for archival", new Style(9.5f, true, Accent)));
        L.Y = MT + 50;
        L.Add(new LineOp(ML, L.Y, W - MR, L.Y, Accent, 2f));
        L.Y += 14;

        // ---- 2. particulars
        L.Text(n.Subject, H1, after: 6);
        L.Pair("Note number", n.NoteNumber);
        L.Pair("Category", n.Category);
        L.Pair("Initiated by", n.InitiatorName + (n.InitiatorDesignation != null ? ", " + n.InitiatorDesignation : "") + (n.InitiatorVertical != null ? " — " + n.InitiatorVertical : ""));
        L.Pair("Created", When(n.CreatedAt));
        L.Pair("Submitted", When(n.SubmittedAt));
        L.Pair("Final state", n.FinalLabel + (n.IsConfidential ? " · Confidential" : ""));
        L.Rule();

        // ---- 3. the note (approved or last submitted text), paragraphs numbered
        L.Text("Note" + (r.Body != null ? $" (text version {r.Body.Version})" : ""), H2, after: 4);
        var paras = ENoteRepository.Paragraphs(r.Body?.BodyText);
        if (paras.Count == 0) L.Text("(no text)", Small);
        for (var i = 0; i < paras.Count; i++) L.Text(paras[i], Body, after: 6, hanging: (i + 1).ToString(CultureInfo.InvariantCulture) + ".");
        L.Rule();

        // ---- 4. note-sheet: every comment and movement in order
        L.Text("Note-sheet", H2, after: 4);
        foreach (var e in r.Sheet)
        {
            L.Need(40);
            var who = e.Name + (string.IsNullOrEmpty(e.Designation) ? "" : ", " + e.Designation);
            var when = When(e.Timestamp);
            var y0 = L.Y;
            L.Text(who, Strong, width: CW - 120, after: 0);
            L.Add(new TextOp(W - MR - L.Width(when, Small), y0 + Small.Size + 1, when, Small));
            if (e.Kind == "Movement") L.Text(Movement(e), new Style(9.5f, true, e.Action is ENoteStates.Approved ? Brand : e.Action is ENoteStates.Rejected ? Accent : Muted), x: 10, after: 1);
            if (!string.IsNullOrWhiteSpace(e.Text)) L.Text(e.Text!, Body, x: 10, after: 1);
            L.Rule(2, 6);
        }

        // ---- 5. decision
        if (n.DecidedAt != null)
        {
            L.Need(70);
            L.Text("Decision", H2, after: 4);
            L.Pair("Decision", n.Outcome);
            L.Pair("Decided by", n.DecidedByName + (n.DecidedByDesignation != null ? ", " + n.DecidedByDesignation : ""));
            L.Pair("Date and time", When(n.DecidedAt));
            L.Pair("Remarks", n.DecisionRemarks);
            L.Rule();
        }
        else if (n.Outcome == ENoteStates.Withdrawn)
        {
            L.Text("Withdrawn by the initiator — no decision was taken.", Strong);
            L.Rule();
        }

        // ---- 6. attachment schedule (files are not merged into the PDF)
        L.Text("Attachment schedule", H2, after: 4);
        if (r.Attachments.Count == 0) L.Text("No attachments.", Small);
        var k = 0;
        foreach (var a in r.Attachments)
        {
            L.Need(44);
            L.Text(a.OriginalFileName + (a.SupersededById != null ? "  (superseded by a newer version)" : ""), Strong, hanging: (++k).ToString(CultureInfo.InvariantCulture) + ".", after: 0);
            L.Text($"Uploaded by {a.UploadedByName}, {When(a.UploadedAt)} · stage: {a.Stage ?? "—"} · {Fmt.Size(a.SizeBytes)}", Small, x: 22, after: 0);
            L.Text("SHA-256 " + a.Sha256, Tiny, x: 22, after: 5);
        }
        if (n.ClosedAt != null) { L.Rule(); L.Pair("Closed / filed", When(n.ClosedAt)); }

        // ---- render with footers
        using var logo = LoadLogo();
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata
        {
            Title = $"{n.NoteNumber} — {n.Subject}", Author = n.InitiatorName, Creator = "NABCONS Intranet", Producer = "NABCONS Intranet",
            Subject = "e-Note archival record", Creation = DateTime.Now, Modified = DateTime.Now,
        }))
        {
            using var paint = new SKPaint { IsAntialias = true };
            var total = L.Pages.Count;
            for (var p = 0; p < total; p++)
            {
                var c = doc.BeginPage(W, H);
                if (p > 0) Draw(c, L, new TextOp(ML, MT + 4, $"{n.NoteNumber} — {Truncate(n.Subject, 90)}", Small), paint);
                foreach (var op in L.Pages[p])
                {
                    switch (op)
                    {
                        case TextOp t: Draw(c, L, t, paint); break;
                        case LineOp l: paint.Color = l.Color; paint.StrokeWidth = l.Width; c.DrawLine(l.X1, l.Y1, l.X2, l.Y2, paint); break;
                        case RectOp rr: paint.Color = rr.Fill; c.DrawRect(rr.R, paint); break;
                        case ImageOp im when logo != null: c.DrawImage(logo, im.R, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)); break;
                    }
                }
                // ---- 7. footer on every page
                paint.Color = new SKColor(0xdd, 0xe3, 0xea); paint.StrokeWidth = 0.8f;
                c.DrawLine(ML, H - MB + 18, W - MR, H - MB + 18, paint);
                Draw(c, L, new TextOp(ML, H - MB + 32, "System-generated record from NABCONS Intranet — not digitally signed", new Style(8f, true, Muted)), paint);
                var pg = $"Page {p + 1} of {total}";
                Draw(c, L, new TextOp(W - MR - L.Width(pg, new Style(8f, true, Muted)), H - MB + 32, pg, new Style(8f, true, Muted)), paint);
                Draw(c, L, new TextOp(ML, H - MB + 44, $"{n.NoteNumber} · Record SHA-256: {recordHash}", Tiny), paint);
                doc.EndPage();
            }
            doc.Close();
        }
        return ms.ToArray();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static void Draw(SKCanvas c, Layout l, TextOp t, SKPaint paint)
    {
        paint.Color = t.S.Color;
        paint.Style = SKPaintStyle.Fill;
        var x = t.X;
        foreach (var (run, deva) in Layout.Runs(t.Text))
        {
            var (sh, font) = l.Font(deva, t.S);
            using (font)
            {
                c.DrawShapedText(sh, run, x, t.Y, SKTextAlign.Left, font, paint);
                x += sh.Shape(run, font).Width;
            }
        }
    }

    private SKImage? LoadLogo()
    {
        try
        {
            var path = Path.Combine(env.WebRootPath ?? Path.Combine(io.ContentRoot, "wwwroot"), "img", "nabcons-logo.png");
            if (!File.Exists(path)) return null;
            // scaled down to print size first, so the PDF stays small
            using var full = SKBitmap.Decode(path);
            if (full == null) return null;
            var scale = Math.Min(1f, 180f / Math.Max(full.Width, full.Height));
            using var small = full.Resize(new SKImageInfo(Math.Max(1, (int)(full.Width * scale)), Math.Max(1, (int)(full.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return small != null ? SKImage.FromBitmap(small) : null;
        }
        catch { return null; }
    }
}
