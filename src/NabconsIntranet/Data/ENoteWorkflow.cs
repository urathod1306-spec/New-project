using System.Security.Claims;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>What the signed-in user may do with a note right now (drives the action panel and is re-checked on every POST).</summary>
public sealed record ENotePermissions(bool IsInitiator, bool IsHolder, bool CanEdit, bool CanMark, bool CanComment, bool CanAttach,
    bool CanSendBack, bool CanReturn, bool CanDecide, bool CanWithdraw, bool CanClose, bool CanRemoveDraftFiles, bool CanPdf);

/// <summary>
/// e-Note movement. There is no fixed hierarchy: whoever holds a note marks it to any active user. One holder at a time;
/// only the holder acts. Every step is one short transaction that updates the note (optimistic concurrency on RowVersion),
/// appends to the append-only movement / comment tables, updates the access list and search index, and writes the
/// notifications. The hash-chained audit entry is written straight after the commit (the existing pattern).
/// </summary>
public sealed class ENoteWorkflow(Db db, ENoteRepository notes, ENoteOptions o, AuditService audit, INotificationService notifications,
    ChangeTracker tracker, MasterRepository masters, FileStore files, Fmt fmt, IntranetOptions io, ILogger<ENoteWorkflow> log)
{
    public const string Changed = "This note has changed — someone acted on it a moment ago. The latest version is shown below; please check it and try again.";

    public ENotePermissions Permissions(ENote n, ClaimsPrincipal u)
    {
        var uid = u.UserId();
        bool init = n.InitiatorUserId == uid, holder = n.CurrentHolderUserId == uid;
        bool moving = n.State == ENoteStates.InMovement;
        return new ENotePermissions(
            IsInitiator: init,
            IsHolder: holder,
            CanEdit: init && n.State is ENoteStates.Draft or ENoteStates.Returned,
            CanMark: holder && n.State is ENoteStates.Draft or ENoteStates.InMovement or ENoteStates.Returned,
            CanComment: holder && n.State is ENoteStates.InMovement or ENoteStates.Returned,
            CanAttach: holder && n.State is ENoteStates.Draft or ENoteStates.InMovement or ENoteStates.Returned,
            CanSendBack: holder && !init && moving,
            CanReturn: holder && !init && moving,
            CanDecide: holder && !init && moving,
            CanWithdraw: init && n.State is ENoteStates.Draft or ENoteStates.InMovement or ENoteStates.Returned,
            CanClose: init && ENoteStates.CanCloseFrom(n.State),
            CanRemoveDraftFiles: init && !n.WasSubmitted && n.State == ENoteStates.Draft,
            CanPdf: init && ENoteStates.IsDecided(n.State));
    }

    // ------------------------------------------------------------------ drafting

    public sealed record DraftInput(string? Subject, string? Category, string? Body, bool Confidential, bool Declaration);

    private (string subject, string? category, string body) Validate(DraftInput d, bool requireDeclaration)
    {
        var subject = (d.Subject ?? "").Trim();
        var body = (d.Body ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (subject.Length == 0) throw new WorkflowException("Enter the subject of the note.");
        if (subject.Length > 300) throw new WorkflowException("The subject can be at most 300 characters.");
        if (body.Length == 0) throw new WorkflowException("Write the note text.");
        if (body.Length > o.MaxBodyChars) throw new WorkflowException($"The note text can be at most {o.MaxBodyChars:N0} characters (it has {body.Length:N0}).");
        var category = string.IsNullOrWhiteSpace(d.Category) ? null : d.Category.Trim();
        if (category == null) throw new WorkflowException("Choose a category.");
        if (!masters.Names(ENoteMaster.Category).Contains(category)) throw new WorkflowException("Choose a category from the list.");
        if (requireDeclaration && !d.Declaration)
            throw new WorkflowException("Confirm that this note is not a disciplinary, vigilance or complaint matter (those stay on the physical file).");
        return (subject, category, body);
    }

    public long Create(DraftInput d, ClaimsPrincipal user)
    {
        var (subject, category, body) = Validate(d, requireDeclaration: true);
        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var me = ENoteRepository.Person(c, tx, user.UserId()) ?? throw new WorkflowException("Your account was not found.");
        var id = Db.Insert(c, tx, """
            INSERT INTO ENotes (Subject, Category, InitiatorUserId, InitiatorName, InitiatorDesignation, InitiatorVertical, CurrentHolderUserId,
                CurrentHolderName, HeldSince, State, DraftBody, LastSeq, IsConfidential, DeclarationAccepted, CreatedAt, UpdatedAt, RowVersion)
            VALUES (@subject, @category, @uid, @name, @desig, @vertical, @uid, @name, @now, 'Draft', @body, 1, @conf, 1, @now, @now, 1)
            """, new { subject, category, uid = me.Id, name = me.Name, desig = me.Designation, vertical = me.Vertical, now, body, conf = d.Confidential });
        Db.Insert(c, tx, """
            INSERT INTO ENoteMovements (NoteId, Seq, Action, FromUserId, FromName, FromDesignation, FromState, ToState, Timestamp)
            VALUES (@id, 1, 'Created', @uid, @name, @desig, NULL, 'Draft', @now)
            """, new { id, uid = me.Id, name = me.Name, desig = me.Designation, now });
        AddParticipant(c, tx, id, me.Id, now);
        Index(c, tx, id, null, subject, category, body);
        tx.Commit();
        tracker.TouchUser(me.Id);
        audit.Log("ENote.Created", "ENote", id, $"Draft #{id}: {subject}" + (d.Confidential ? " (confidential)" : ""));
        return id;
    }

    /// <summary>Initiator edits a draft, or the text of a note returned to them. The confidential flag is locked after the first submission.</summary>
    public void EditDraft(long id, DraftInput d, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanEdit) throw new WorkflowException("Only the initiator can edit a note, and only while it is a draft or has been returned to them.");
        var (subject, category, body) = Validate(d, requireDeclaration: !n.WasSubmitted);
        var conf = n.WasSubmitted ? n.IsConfidential : d.Confidential;
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var ok = Db.Exec(c, tx, """
            UPDATE ENotes SET Subject = @subject, Category = @category, DraftBody = @body, IsConfidential = @conf,
                   RowVersion = RowVersion + 1, UpdatedAt = @now
            WHERE Id = @id AND RowVersion = @rowVersion AND InitiatorUserId = @uid AND State IN ('Draft', 'ReturnedToInitiator')
            """, new { subject, category, body, conf, now = Db.Now(), id, rowVersion, uid = user.UserId() });
        if (ok == 0) throw new WorkflowException(Changed);
        // a draft is indexed with its working text; a submitted note keeps its submitted text in the index until re-submitted
        if (!n.WasSubmitted) Index(c, tx, id, n.NoteNumber, subject, category, body);
        tx.Commit();
        audit.Log("ENote.DraftEdited", "ENote", id, $"{n.DisplayNumber}: {subject}" + (n.State == ENoteStates.Returned ? " (after return)" : ""));
    }

    // ------------------------------------------------------------------ movement

    /// <summary>
    /// "Mark to": the holder sends the note to any active user. From a draft this is the first submission (number and
    /// body version 1); after a return it is a re-submission (a new body version if the text changed).
    /// </summary>
    public void MarkTo(long id, long toUserId, string? remarks, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        var p = Permissions(n, user);
        if (!p.CanMark) throw new WorkflowException("Only the person currently holding the note can mark it onward.");
        remarks = Clean(remarks);
        if (!p.IsInitiator && remarks == null) throw new WorkflowException("Add your remarks before marking the note onward.");
        if (toUserId == user.UserId()) throw new WorkflowException("You already hold this note — choose someone else.");
        if (toUserId == n.InitiatorUserId) throw new WorkflowException("To send the note to its initiator, use “Return to initiator”.");
        if (!n.WasSubmitted && !n.DeclarationAccepted) throw new WorkflowException("Confirm the declaration on the draft first.");

        var now = Db.Now();
        string action, auditAction;
        string? number = n.NoteNumber;
        long? newVersion = null;
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            var to = ENoteRepository.Person(c, tx, toUserId);
            if (to == null || !to.IsActive) throw new WorkflowException("Choose an active intranet user.");
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            if (n.State == ENoteStates.Draft)
            {
                action = "Submitted"; auditAction = "ENote.Submitted";
                Validate(new DraftInput(n.Subject, n.Category, n.DraftBody, n.IsConfidential, true), requireDeclaration: false);
                number = AssignNumber(c, tx, n);
                newVersion = AddVersion(c, tx, n, n.DraftBody!, me, now);
                Db.Exec(c, tx, "UPDATE ENotes SET SubmittedAt = @now WHERE Id = @id", new { now, id });
            }
            else if (n.State == ENoteStates.Returned)
            {
                action = "Resubmitted"; auditAction = "ENote.Resubmitted";
                var current = Db.Scalar<string>(c, tx, "SELECT BodyText FROM ENoteBodyVersions WHERE NoteId = @id ORDER BY Version DESC LIMIT 1", new { id });
                var body = (n.DraftBody ?? current ?? "").Trim();
                if (body.Length == 0) throw new WorkflowException("Write the note text.");
                if (body.Length > o.MaxBodyChars) throw new WorkflowException($"The note text can be at most {o.MaxBodyChars:N0} characters.");
                if (body != current) newVersion = AddVersion(c, tx, n, body, me, now);
            }
            else { action = "MarkedTo"; auditAction = "ENote.MarkedTo"; }

            var seq = Move(c, tx, n, rowVersion, new Transition(ENoteStates.InMovement, to.Id, to.Name, me.Id, true), now);
            Movement(c, tx, id, seq, action, me, to, n.State, ENoteStates.InMovement, remarks, now);
            AddParticipant(c, tx, id, to.Id, now);
            if (newVersion != null || number != n.NoteNumber)
                Index(c, tx, id, number, n.Subject, n.Category, newVersion != null ? (n.DraftBody ?? "") : CurrentBody(c, tx, id));
            notifications.Notify(c, tx, new[] { to.Id }, new NotificationActor(me.Id, me.Name), new NotificationMessage("ENote.Received",
                $"e-Note {number} marked to you — {n.Subject}", remarks, $"/ENotes/{id}", "ENote", id));
            tx.Commit();
        }
        tracker.TouchUser(n.InitiatorUserId);
        tracker.Touch(ChangeTracker.ENoteQueue);
        audit.Log(auditAction, "ENote", id, $"{number}: marked to user {toUserId}" + (newVersion != null ? $"; text version {newVersion}" : "") + (remarks != null ? $"; remarks: {remarks}" : ""));
        if (auditAction != "ENote.MarkedTo") audit.Log("ENote.MarkedTo", "ENote", id, $"{number}: to user {toUserId}");
    }

    /// <summary>Send back to the person who marked the note to the holder. If that is the initiator, it is a return to the initiator.</summary>
    public void SendBack(long id, string? remarks, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanSendBack) throw new WorkflowException("Only the current holder can send the note back.");
        remarks = Clean(remarks) ?? throw new WorkflowException("Add your remarks before sending the note back.");
        var target = n.PreviousHolderUserId ?? n.InitiatorUserId;
        if (target == n.InitiatorUserId) { ToInitiator(n, "SentBack", "ENote.SentBack", remarks, rowVersion, user); return; }
        var now = Db.Now();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            var to = ENoteRepository.Person(c, tx, target);
            if (to == null || !to.IsActive) throw new WorkflowException("The person who sent you this note is no longer an active user. Mark it to someone else, or return it to the initiator.");
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            var seq = Move(c, tx, n, rowVersion, new Transition(ENoteStates.InMovement, to.Id, to.Name, me.Id, true), now);
            Movement(c, tx, id, seq, "SentBack", me, to, n.State, ENoteStates.InMovement, remarks, now);
            notifications.Notify(c, tx, new[] { to.Id }, new NotificationActor(me.Id, me.Name), new NotificationMessage("ENote.SentBack",
                $"e-Note {n.NoteNumber} sent back to you — {n.Subject}", remarks, $"/ENotes/{id}", "ENote", id));
            tx.Commit();
        }
        tracker.TouchUser(n.InitiatorUserId);
        audit.Log("ENote.SentBack", "ENote", id, $"{n.NoteNumber}: sent back to user {target}; remarks: {remarks}");
    }

    public void ReturnToInitiator(long id, string? remarks, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanReturn) throw new WorkflowException("Only the current holder can return the note to its initiator.");
        remarks = Clean(remarks) ?? throw new WorkflowException("Add your remarks before returning the note to its initiator.");
        ToInitiator(n, "ReturnedToInitiator", "ENote.ReturnedToInitiator", remarks, rowVersion, user);
    }

    private void ToInitiator(ENote n, string action, string auditAction, string remarks, long rowVersion, ClaimsPrincipal user)
    {
        var now = Db.Now();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            var init = ENoteRepository.Person(c, tx, n.InitiatorUserId)!;
            var seq = Move(c, tx, n, rowVersion, new Transition(ENoteStates.Returned, init.Id, init.Name, me.Id, true), now);
            // the initiator edits from the latest submitted text
            Db.Exec(c, tx, "UPDATE ENotes SET DraftBody = (SELECT BodyText FROM ENoteBodyVersions WHERE NoteId = @id ORDER BY Version DESC LIMIT 1) WHERE Id = @id", new { id = n.Id });
            Movement(c, tx, n.Id, seq, action, me, init, n.State, ENoteStates.Returned, remarks, now);
            notifications.Notify(c, tx, new[] { init.Id }, new NotificationActor(me.Id, me.Name), new NotificationMessage("ENote.Returned",
                $"e-Note {n.NoteNumber} returned to you — {n.Subject}", remarks, $"/ENotes/{n.Id}", "ENote", n.Id));
            tx.Commit();
        }
        audit.Log(auditAction, "ENote", n.Id, $"{n.NoteNumber}: returned to the initiator; remarks: {remarks}");
    }

    public void Decide(long id, bool approve, string? remarks, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (n.InitiatorUserId == user.UserId()) throw new WorkflowException("The initiator cannot approve or reject their own note.");
        if (!Permissions(n, user).CanDecide) throw new WorkflowException("Only the current holder can approve or reject the note.");
        remarks = Clean(remarks) ?? throw new WorkflowException(approve ? "Record your decision (remarks) to approve the note." : "Give the reason for rejecting the note.");
        var state = approve ? ENoteStates.Approved : ENoteStates.Rejected;
        var now = Db.Now();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            var init = ENoteRepository.Person(c, tx, n.InitiatorUserId)!;
            var seq = Move(c, tx, n, rowVersion, new Transition(state, init.Id, init.Name, me.Id, true) { Outcome = state, Decide = true, Remarks = remarks, Me = me }, now);
            Movement(c, tx, id, seq, state, me, init, n.State, state, remarks, now);
            notifications.Notify(c, tx, Participants(c, tx, id), new NotificationActor(me.Id, me.Name), new NotificationMessage("ENote." + state,
                $"e-Note {n.NoteNumber} {(approve ? "approved" : "rejected")} by {me.Name} — {n.Subject}", remarks, $"/ENotes/{id}", "ENote", id));
            tx.Commit();
        }
        tracker.Touch(ChangeTracker.ENoteQueue);
        audit.Log("ENote." + state, "ENote", id, $"{n.NoteNumber}: {state}; remarks: {remarks}");
    }

    public void Withdraw(long id, string? reason, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanWithdraw) throw new WorkflowException("Only the initiator can withdraw a note, and only before a final decision.");
        reason = Clean(reason) ?? throw new WorkflowException("Give the reason for withdrawing the note.");
        var now = Db.Now();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            var seq = Move(c, tx, n, rowVersion, new Transition(ENoteStates.Withdrawn, me.Id, me.Name, n.CurrentHolderUserId, true) { Outcome = ENoteStates.Withdrawn }, now);
            Movement(c, tx, id, seq, "Withdrawn", me, me, n.State, ENoteStates.Withdrawn, reason, now);
            notifications.Notify(c, tx, Participants(c, tx, id), new NotificationActor(me.Id, me.Name), new NotificationMessage("ENote.Withdrawn",
                $"e-Note {n.DisplayNumber} withdrawn by the initiator — {n.Subject}", reason, $"/ENotes/{id}", "ENote", id));
            tx.Commit();
        }
        if (n.CurrentHolderUserId is long h) tracker.TouchUser(h);
        audit.Log("ENote.Withdrawn", "ENote", id, $"{n.DisplayNumber}: withdrawn; reason: {reason}");
    }

    public void Close(long id, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanClose) throw new WorkflowException("Only the initiator can close/file a note, after it is approved, rejected or withdrawn.");
        var now = Db.Now();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            var seq = Move(c, tx, n, rowVersion, new Transition(ENoteStates.Closed, null, null, n.PreviousHolderUserId, false) { Close = true }, now);
            Movement(c, tx, id, seq, "Closed", me, null, n.State, ENoteStates.Closed, null, now);
            tx.Commit();
        }
        tracker.TouchUser(user.UserId());
        audit.Log("ENote.Closed", "ENote", id, $"{n.DisplayNumber}: closed / filed ({n.Outcome})");
    }

    public void Comment(long id, string? text, long rowVersion, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanComment) throw new WorkflowException("Only the current holder can add a comment.");
        text = Clean(text, 4000) ?? throw new WorkflowException("Write your comment.");
        var now = Db.Now();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            var seq = Db.Scalar<long>(c, tx, """
                UPDATE ENotes SET LastSeq = LastSeq + 1, RowVersion = RowVersion + 1, UpdatedAt = @now
                WHERE Id = @id AND RowVersion = @rowVersion AND CurrentHolderUserId = @uid RETURNING LastSeq
                """, new { now, id, rowVersion, uid = me.Id });
            if (seq == 0) throw new WorkflowException(Changed);
            Db.Insert(c, tx, """
                INSERT INTO ENoteComments (NoteId, Seq, AuthorUserId, AuthorName, AuthorDesignation, Text, Timestamp)
                VALUES (@id, @seq, @uid, @name, @desig, @text, @now)
                """, new { id, seq, uid = me.Id, name = me.Name, desig = me.Designation, text, now });
            notifications.Notify(c, tx, new[] { n.InitiatorUserId }, new NotificationActor(me.Id, me.Name), new NotificationMessage("ENote.Commented",
                $"{me.Name} commented on your e-Note {n.DisplayNumber}", text, $"/ENotes/{id}", "ENote", id));
            tx.Commit();
        }
        audit.Log("ENote.Commented", "ENote", id, $"{n.DisplayNumber}: {text}");
    }

    // ------------------------------------------------------------------ attachments

    /// <summary>
    /// Saves the files first (outside any transaction), then records them in one short transaction that re-checks the
    /// holder, the file count and the per-note quota. If that fails, the saved files are deleted again.
    /// </summary>
    public async Task<int> AttachAsync(long id, IReadOnlyList<IFormFile> uploads, long? replacesId, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanAttach) throw new WorkflowException("Only the current holder can add attachments.");
        var list = uploads.Where(f => f.Length > 0 || !string.IsNullOrEmpty(f.FileName)).ToList();
        if (list.Count == 0) throw new WorkflowException("Choose a file to attach.");
        if (replacesId != null && list.Count != 1) throw new WorkflowException("Attach exactly one file to replace an earlier one.");
        var (count, bytes) = notes.Usage(id);
        if (count + list.Count > o.MaxFilesPerNote) throw new WorkflowException($"A note can have at most {o.MaxFilesPerNote} attachments ({count} already).");
        if (bytes + list.Sum(f => f.Length) > o.MaxNoteBytes) throw new WorkflowException($"All attachments of a note together can be at most {o.MaxNoteTotalMB} MB.");

        var saved = new List<StoredFile>();
        try
        {
            foreach (var f in list) saved.Add(await files.SaveAsync(f, "enotes", o.AllowedExtensions, o.MaxFileBytes));
        }
        catch (UploadRejectedException ex)
        {
            foreach (var s in saved) DeleteStored(s.StoredPath);
            audit.Log("Upload.Rejected", "ENote", id, ex.Message);
            throw new WorkflowException(ex.Message);
        }

        var now = Db.Now();
        try
        {
            using var c = db.Open();
            using var tx = c.BeginTransaction(deferred: false);
            var cur = Db.Query<ENote>(c, tx, "SELECT * FROM ENotes WHERE Id = @id", new { id }).First();
            if (cur.CurrentHolderUserId != user.UserId() || !(cur.State is ENoteStates.Draft or ENoteStates.InMovement or ENoteStates.Returned))
                throw new WorkflowException(Changed);
            var used = Db.Query<UsageRow>(c, tx, "SELECT COUNT(*) AS N, COALESCE(SUM(SizeBytes), 0) AS B FROM ENoteAttachments WHERE NoteId = @id", new { id }).First();
            if (used.N + saved.Count > o.MaxFilesPerNote || used.B + saved.Sum(s => s.SizeBytes) > o.MaxNoteBytes)
                throw new WorkflowException("The attachment limit for this note has been reached.");
            var me = ENoteRepository.Person(c, tx, user.UserId())!;
            var stage = cur.WasSubmitted ? $"With {me.Name}" + (me.Designation != null ? $", {me.Designation}" : "") : "Draft";
            long last = 0;
            foreach (var s in saved)
                last = Db.Insert(c, tx, """
                    INSERT INTO ENoteAttachments (NoteId, StoredPath, OriginalFileName, ContentType, SizeBytes, Sha256, UploadedByUserId, UploadedByName, UploadedAt, StageSeq, Stage)
                    VALUES (@id, @StoredPath, @OriginalFileName, @ContentType, @SizeBytes, @Sha256, @uid, @name, @now, @seq, @stage)
                    """, new { id, s.StoredPath, s.OriginalFileName, s.ContentType, s.SizeBytes, s.Sha256, uid = me.Id, name = me.Name, now, seq = cur.LastSeq, stage });
            if (replacesId is long old)
            {
                var ok = Db.Exec(c, tx, "UPDATE ENoteAttachments SET SupersededById = @last WHERE Id = @old AND NoteId = @id AND UploadedByUserId = @uid AND SupersededById IS NULL",
                    new { last, old, id, uid = me.Id });
                if (ok == 0) throw new WorkflowException("You can only replace a current attachment that you uploaded yourself.");
            }
            Db.Exec(c, tx, "UPDATE ENotes SET UpdatedAt = @now WHERE Id = @id", new { now, id });
            if (cur.WasSubmitted)
                notifications.Notify(c, tx, new[] { cur.InitiatorUserId }, new NotificationActor(me.Id, me.Name), new NotificationMessage("ENote.AttachmentAdded",
                    $"{me.Name} attached {(saved.Count == 1 ? saved[0].OriginalFileName : saved.Count + " files")} to your e-Note {cur.DisplayNumber}", null, $"/ENotes/{id}", "ENote", id));
            tx.Commit();
        }
        catch
        {
            foreach (var s in saved) DeleteStored(s.StoredPath);
            throw;
        }
        foreach (var s in saved) audit.Log("ENote.AttachmentAdded", "ENote", id, $"{n.DisplayNumber}: {s.OriginalFileName} ({s.SizeBytes} bytes, SHA-256 {s.Sha256})");
        if (replacesId != null) audit.Log("ENote.AttachmentSuperseded", "ENote", id, $"{n.DisplayNumber}: attachment {replacesId} superseded by {saved[0].OriginalFileName}");
        return saved.Count;
    }

    private sealed class UsageRow { public long N { get; set; } public long B { get; set; } }

    /// <summary>Marks one of the user's own attachments as superseded by a newer one already on the note.</summary>
    public void Supersede(long id, long attachmentId, long byId, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!notes.CanView(n, user) || n.State == ENoteStates.Closed) throw new WorkflowException("This note can no longer be changed.");
        if (attachmentId == byId) throw new WorkflowException("Choose a different, newer attachment.");
        var a = notes.Attachment(attachmentId);
        var b = notes.Attachment(byId);
        if (a == null || b == null || a.NoteId != id || b.NoteId != id || a.UploadedByUserId != user.UserId() || b.Id < a.Id)
            throw new WorkflowException("You can mark only your own attachment as superseded, by a newer attachment on the same note.");
        var ok = db.Exec("UPDATE ENoteAttachments SET SupersededById = @byId WHERE Id = @attachmentId AND SupersededById IS NULL", new { byId, attachmentId });
        if (ok == 0) throw new WorkflowException("That attachment is already marked as superseded.");
        audit.Log("ENote.AttachmentSuperseded", "ENote", id, $"{n.DisplayNumber}: {a.OriginalFileName} superseded by {b.OriginalFileName}");
    }

    /// <summary>Before the first submission the initiator may remove draft attachments (the database refuses this afterwards).</summary>
    public void RemoveDraftAttachment(long id, long attachmentId, ClaimsPrincipal user)
    {
        var n = notes.Get(id) ?? throw new WorkflowException("Note not found.");
        if (!Permissions(n, user).CanRemoveDraftFiles) throw new WorkflowException("Attachments can be removed only from a draft that has never been submitted.");
        var a = notes.Attachment(attachmentId);
        if (a == null || a.NoteId != id) throw new WorkflowException("Attachment not found.");
        db.Exec("DELETE FROM ENoteAttachments WHERE Id = @attachmentId AND NoteId = @id", new { attachmentId, id });
        DeleteStored(a.StoredPath);
        audit.Log("ENote.DraftAttachmentRemoved", "ENote", id, $"Draft #{id}: {a.OriginalFileName}");
    }

    private void DeleteStored(string storedPath)
    {
        try
        {
            var root = Path.GetFullPath(io.DocumentsPath) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(io.DocumentsPath, storedPath));
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) { log.LogWarning(ex, "Could not delete file {Path}", storedPath); }
    }

    // ------------------------------------------------------------------ helpers

    public sealed record Transition(string State, long? Holder, string? HolderName, long? Prev, bool ResetHeld)
    {
        public string? Outcome { get; init; }
        public bool Decide { get; init; }
        public bool Close { get; init; }
        public string? Remarks { get; init; }
        public ENotePerson? Me { get; init; }
    }

    /// <summary>Applies a state change with optimistic concurrency and returns the note-sheet sequence number of the step.</summary>
    private static long Move(SqliteConnection c, SqliteTransaction tx, ENote n, long rowVersion, Transition t, string now)
    {
        var seq = Db.Scalar<long>(c, tx, """
            UPDATE ENotes SET State = @State, CurrentHolderUserId = @Holder, CurrentHolderName = @HolderName, PreviousHolderUserId = @Prev,
                   HeldSince = CASE WHEN @ResetHeld = 1 THEN @now ELSE HeldSince END,
                   Outcome = COALESCE(@Outcome, Outcome),
                   DecidedAt = CASE WHEN @Decide = 1 THEN @now ELSE DecidedAt END,
                   DecidedByUserId = CASE WHEN @Decide = 1 THEN @meId ELSE DecidedByUserId END,
                   DecidedByName = CASE WHEN @Decide = 1 THEN @meName ELSE DecidedByName END,
                   DecidedByDesignation = CASE WHEN @Decide = 1 THEN @meDesig ELSE DecidedByDesignation END,
                   DecisionRemarks = CASE WHEN @Decide = 1 THEN @Remarks ELSE DecisionRemarks END,
                   ClosedAt = CASE WHEN @Close = 1 THEN @now ELSE ClosedAt END,
                   LastAgeingAlertOn = NULL,
                   LastSeq = LastSeq + 1, RowVersion = RowVersion + 1, UpdatedAt = @now
            WHERE Id = @Id AND RowVersion = @rowVersion AND State = @from
            RETURNING LastSeq
            """, new
        {
            t.State, t.Holder, t.HolderName, t.Prev, t.ResetHeld, t.Outcome, t.Decide, t.Close, t.Remarks, now, n.Id, rowVersion, from = n.State,
            meId = t.Me?.Id, meName = t.Me?.Name, meDesig = t.Me?.Designation,
        });
        if (seq == 0) throw new WorkflowException(Changed);
        return seq;
    }

    private static void Movement(SqliteConnection c, SqliteTransaction tx, long id, long seq, string action, ENotePerson from, ENotePerson? to,
        string fromState, string toState, string? remarks, string now) =>
        Db.Insert(c, tx, """
            INSERT INTO ENoteMovements (NoteId, Seq, Action, FromUserId, FromName, FromDesignation, ToUserId, ToName, ToDesignation, FromState, ToState, Remarks, Timestamp)
            VALUES (@id, @seq, @action, @fid, @fname, @fdesig, @tid, @tname, @tdesig, @fromState, @toState, @remarks, @now)
            """, new { id, seq, action, fid = from.Id, fname = from.Name, fdesig = from.Designation, tid = to?.Id, tname = to?.Name, tdesig = to?.Designation, fromState, toState, remarks, now });

    private static void AddParticipant(SqliteConnection c, SqliteTransaction tx, long id, long userId, string now) =>
        Db.Exec(c, tx, "INSERT OR IGNORE INTO ENoteParticipants (NoteId, UserId, FirstInvolvedAt) VALUES (@id, @userId, @now)", new { id, userId, now });

    private static List<long> Participants(SqliteConnection c, SqliteTransaction tx, long id) =>
        Db.Query<IdRow>(c, tx, "SELECT UserId AS Id FROM ENoteParticipants WHERE NoteId = @id", new { id }).Select(r => r.Id).ToList();

    private sealed class IdRow { public long Id { get; set; } }

    private static string CurrentBody(SqliteConnection c, SqliteTransaction tx, long id) =>
        Db.Scalar<string>(c, tx, "SELECT BodyText FROM ENoteBodyVersions WHERE NoteId = @id ORDER BY Version DESC LIMIT 1", new { id }) ?? "";

    private static long AddVersion(SqliteConnection c, SqliteTransaction tx, ENote n, string body, ENotePerson me, string now)
    {
        var version = Db.Scalar<long>(c, tx, "SELECT COALESCE(MAX(Version), 0) + 1 FROM ENoteBodyVersions WHERE NoteId = @id", new { id = n.Id });
        Db.Insert(c, tx, """
            INSERT INTO ENoteBodyVersions (NoteId, Version, BodyText, Sha256, CreatedByUserId, CreatedBy, CreatedAt)
            VALUES (@id, @version, @body, @sha, @uid, @name, @now)
            """, new { id = n.Id, version, body, sha = ENoteRepository.Sha(body), uid = me.Id, name = me.Name, now });
        Db.Exec(c, tx, "UPDATE ENotes SET CurrentBodyVersion = @version WHERE Id = @id", new { version, id = n.Id });
        return version;
    }

    /// <summary>NCS/EN/&lt;vertical code or GEN&gt;/&lt;FY&gt;/&lt;serial&gt;, serial per code per financial year (atomic, as for leads).</summary>
    private string AssignNumber(SqliteConnection c, SqliteTransaction tx, ENote n)
    {
        var code = Db.Scalar<string>(c, tx, "SELECT Code FROM MasterItems WHERE Type = 'Vertical' AND Name = @v COLLATE NOCASE AND Code IS NOT NULL AND Code <> '' LIMIT 1",
            new { v = n.InitiatorVertical ?? "" });
        code = ENoteMaster.CleanCode(code) ?? "GEN";
        var fy = LeadRepository.FinancialYear(fmt.Today);
        var serial = Db.Scalar<long>(c, tx, """
            INSERT INTO ENoteSerials (Code, FinancialYear, LastSerial) VALUES (@code, @fy, 1)
            ON CONFLICT(Code, FinancialYear) DO UPDATE SET LastSerial = LastSerial + 1
            RETURNING LastSerial
            """, new { code, fy });
        var number = FormatNumber(o.NumberPrefix, code, fy, serial);
        Db.Exec(c, tx, "UPDATE ENotes SET NoteNumber = @number, NumberCode = @code, FinancialYear = @fy, Serial = @serial WHERE Id = @id",
            new { number, code, fy, serial, id = n.Id });
        return number;
    }

    public static string FormatNumber(string prefix, string code, string fy, long serial) => $"{prefix.TrimEnd('/')}/{code}/{fy}/{serial:D4}";

    private static void Index(SqliteConnection c, SqliteTransaction tx, long id, string? number, string subject, string? category, string body)
    {
        Db.Exec(c, tx, "DELETE FROM ENoteSearch WHERE rowid = @id", new { id });
        Db.Exec(c, tx, "INSERT INTO ENoteSearch (rowid, NoteNumber, Subject, Category, Body) VALUES (@id, @number, @subject, @category, @body)",
            new { id, number, subject, category, body });
    }

    private static string? Clean(string? s, int max = 2000)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace("\r\n", "\n").Trim();
        return s.Length > max ? s[..max] : s;
    }

    // ------------------------------------------------------------------ daily job

    /// <summary>Holders of notes pending more than AgeingAlertDays get one reminder per day (run by the daily housekeeping).</summary>
    public List<string> AgeingAlerts()
    {
        var today = fmt.Today.ToString("yyyy-MM-dd");
        var cut = DateTime.UtcNow.AddDays(-Math.Max(1, o.AgeingAlertDays)).ToString("o");
        var due = db.Query<ENote>("""
            SELECT * FROM ENotes WHERE State IN ('InMovement', 'ReturnedToInitiator') AND HeldSince < @cut
              AND (LastAgeingAlertOn IS NULL OR LastAgeingAlertOn < @today) LIMIT 5000
            """, new { cut, today });
        foreach (var chunk in due.Chunk(200))
        {
            using var c = db.Open();
            using var tx = c.BeginTransaction(deferred: false);
            foreach (var n in chunk)
            {
                var days = (int)fmt.DaysSince(n.HeldSince);
                notifications.Notify(c, tx, new[] { n.CurrentHolderUserId ?? 0 }, new NotificationActor(0, "NABCONS Intranet"), new NotificationMessage("ENote.Ageing",
                    $"e-Note {n.DisplayNumber} has been with you for {days} days — {n.Subject}", null, $"/ENotes/{n.Id}", "ENote", n.Id));
                Db.Exec(c, tx, "UPDATE ENotes SET LastAgeingAlertOn = @today WHERE Id = @id", new { today, id = n.Id });
            }
            tx.Commit();
        }
        return due.Count > 0 ? new List<string> { $"e-Note ageing reminders: {due.Count}" } : new();
    }

    /// <summary>Reports (never deletes) stored e-Note files with no database record, e.g. after a failed save.</summary>
    public List<string> OrphanFiles()
    {
        var folder = Path.Combine(io.DocumentsPath, "enotes");
        if (!Directory.Exists(folder)) return new();
        var known = db.Strings("SELECT StoredPath FROM ENoteAttachments").Concat(db.Strings("SELECT PdfPath FROM ENotes WHERE PdfPath IS NOT NULL"))
            .Select(p => p.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(io.DocumentsPath, f).Replace('\\', '/'))
            .Where(r => !known.Contains(r) && !r.StartsWith("enotes/pdf/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (orphans.Count == 0) return new();
        log.LogWarning("e-Note files without a database record: {Files}", string.Join(", ", orphans.Take(50)));
        return new List<string> { $"e-Note orphan files (not deleted; see the log): {orphans.Count}" };
    }
}

public static class ENoteMaster
{
    public const string Category = "ENoteCategory";

    /// <summary>Vertical codes: 2–8 letters/digits, upper case.</summary>
    public static string? CleanCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = new string(code.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return c.Length is >= 2 and <= 8 ? c : null;
    }
}
