using System.Security.Claims;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public sealed class WorkflowException(string message) : Exception(message);

public sealed record WorkflowAction(string Name, string Label, string ToState, bool RemarksRequired, string Style);

/// <summary>
/// General Administrative Approval Request workflow:
/// Draft → Submitted (with Reviewing Officer) → Under Review (with Sanctioning Authority)
///       → Approved / Returned / Rejected → Closed.
/// Every transition is recorded in the append-only ApprovalActions table and the audit log.
/// Optimistic concurrency (RowVersion) prevents two approvers acting on the same request at once.
/// </summary>
public sealed class RequestWorkflow(Db db, AuditService audit, INotificationService notifications)
{
    public const string FormName = "General Administrative Approval Request";

    public static readonly WorkflowAction Submit = new("Submit", "Submit for review", RequestStates.Submitted, false, "primary");
    public static readonly WorkflowAction Recommend = new("Recommend", "Recommend & forward to Sanctioning Authority", RequestStates.UnderReview, false, "primary");
    public static readonly WorkflowAction Approve = new("Approve", "Approve", RequestStates.Approved, false, "success");
    public static readonly WorkflowAction Return = new("Return", "Return for clarification", RequestStates.Returned, true, "warning");
    public static readonly WorkflowAction Reject = new("Reject", "Reject", RequestStates.Rejected, true, "danger");
    public static readonly WorkflowAction Close = new("Close", "Close request", RequestStates.Closed, false, "secondary");

    private static readonly WorkflowAction[] AllActions = { Submit, Recommend, Approve, Return, Reject, Close };

    private const string Select = """
        SELECT r.*, rv.DisplayName AS ReviewerName, ap.DisplayName AS ApproverName, asg.DisplayName AS AssignedToName
        FROM Requests r
        LEFT JOIN Users rv ON rv.Id = r.ReviewerUserId
        LEFT JOIN Users ap ON ap.Id = r.ApproverUserId
        LEFT JOIN Users asg ON asg.Id = r.AssignedToUserId
        """;

    public ApprovalRequest? Get(long id) => db.Single<ApprovalRequest>(Select + " WHERE r.Id = @id", new { id });

    public bool CanView(ApprovalRequest r, ClaimsPrincipal user)
    {
        var uid = user.UserId();
        return r.SubmitterUserId == uid || r.ReviewerUserId == uid || r.ApproverUserId == uid || user.IsAdmin() || user.IsAuditor();
    }

    public bool CanEdit(ApprovalRequest r, ClaimsPrincipal user) =>
        r.SubmitterUserId == user.UserId() && r.State is RequestStates.Draft or RequestStates.Returned;

    public List<WorkflowAction> AllowedActions(ApprovalRequest r, ClaimsPrincipal user)
    {
        var uid = user.UserId();
        var list = new List<WorkflowAction>();
        bool isSubmitter = r.SubmitterUserId == uid;
        switch (r.State)
        {
            case RequestStates.Draft:
            case RequestStates.Returned:
                if (isSubmitter) list.Add(Submit);
                break;
            case RequestStates.Submitted:
                if (r.ReviewerUserId == uid && r.AssignedToUserId == uid && user.IsApprover()) list.AddRange(new[] { Recommend, Return, Reject });
                break;
            case RequestStates.UnderReview:
                if (r.ApproverUserId == uid && r.AssignedToUserId == uid && user.IsApprover()) list.AddRange(new[] { Approve, Return, Reject });
                break;
            case RequestStates.Approved:
            case RequestStates.Rejected:
                if (isSubmitter || user.IsAdmin()) list.Add(Close);
                break;
        }
        return list;
    }

    public List<ApprovalRequest> Mine(long userId) =>
        db.Query<ApprovalRequest>(Select + " WHERE r.SubmitterUserId = @userId ORDER BY r.UpdatedAt DESC", new { userId });

    public List<ApprovalRequest> Inbox(long userId) => db.Query<ApprovalRequest>(Select + $"""
         WHERE r.AssignedToUserId = @userId AND r.State IN ('{RequestStates.Submitted}', '{RequestStates.UnderReview}')
         ORDER BY r.UpdatedAt
        """, new { userId });

    public long InboxCount(long userId) => db.Scalar<long>($"""
        SELECT COUNT(*) FROM Requests WHERE AssignedToUserId = @userId AND State IN ('{RequestStates.Submitted}', '{RequestStates.UnderReview}')
        """, new { userId });

    public List<ApprovalRequest> ActionedBy(long userId) => db.Query<ApprovalRequest>(Select + """
         WHERE r.Id IN (SELECT RequestId FROM ApprovalActions WHERE ActorUserId = @userId) AND r.SubmitterUserId <> @userId
         ORDER BY r.UpdatedAt DESC LIMIT 25
        """, new { userId });

    public List<ApprovalRequest> All(string? state) => db.Query<ApprovalRequest>(Select +
        (string.IsNullOrEmpty(state) ? "" : " WHERE r.State = @state") + " ORDER BY r.UpdatedAt DESC LIMIT 500", new { state });

    public Dictionary<string, long> CountsByState() => db.Query<StateCount>("SELECT State, COUNT(*) AS N FROM Requests GROUP BY State")
        .ToDictionary(x => x.State, x => x.N);

    private sealed class StateCount { public string State { get; set; } = ""; public long N { get; set; } }

    public List<ApprovalAction> History(long requestId) =>
        db.Query<ApprovalAction>("SELECT * FROM ApprovalActions WHERE RequestId = @requestId ORDER BY Id", new { requestId });

    public List<Attachment> Attachments(long requestId) =>
        db.Query<Attachment>("SELECT * FROM Attachments WHERE RequestId = @requestId ORDER BY Id", new { requestId });

    public Attachment? GetAttachment(long id) => db.Single<Attachment>("SELECT * FROM Attachments WHERE Id = @id", new { id });

    public void AddAttachment(long requestId, StoredFile f, ClaimsPrincipal user)
    {
        db.Insert("""
            INSERT INTO Attachments (RequestId, StoredPath, OriginalFileName, ContentType, SizeBytes, Sha256, UploadedByUserId, UploadedBy, UploadedAt)
            VALUES (@requestId, @StoredPath, @OriginalFileName, @ContentType, @SizeBytes, @Sha256, @uid, @name, @now)
            """, new { requestId, f.StoredPath, f.OriginalFileName, f.ContentType, f.SizeBytes, f.Sha256, uid = user.UserId(), name = user.DisplayName(), now = Db.Now() });
        audit.Log("Request.AttachmentUploaded", "Request", requestId, $"{f.OriginalFileName} ({f.SizeBytes} bytes, SHA-256 {f.Sha256})");
    }

    /// <summary>Creates a new draft and records the "Created" step.</summary>
    public long CreateDraft(ApprovalRequest r, ClaimsPrincipal user)
    {
        ValidateRouting(r, user.UserId());
        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        r.Id = Db.Insert(c, tx, """
            INSERT INTO Requests (FormType, Subject, RequestCategory, Amount, RequiredBy, Details, SubmitterUserId, SubmitterName, SubmitterVertical,
                                  ReviewerUserId, ApproverUserId, State, AssignedToUserId, RowVersion, CreatedAt, UpdatedAt)
            VALUES (@FormType, @Subject, @RequestCategory, @Amount, @RequiredBy, @Details, @uid, @name, @vertical,
                    @ReviewerUserId, @ApproverUserId, 'Draft', @uid, 1, @now, @now)
            """, new { r.FormType, r.Subject, r.RequestCategory, r.Amount, r.RequiredBy, r.Details, uid = user.UserId(), name = user.DisplayName(), vertical = user.Vertical(), r.ReviewerUserId, r.ApproverUserId, now });
        var number = $"NABCONS/REQ/{DateTime.UtcNow:yyyy}/{r.Id:D5}";
        Db.Exec(c, tx, "UPDATE Requests SET RequestNumber = @number WHERE Id = @Id", new { number, r.Id });
        AppendAction(c, tx, r.Id, "Created", null, RequestStates.Draft, user, "Submitter", null, now);
        tx.Commit();
        audit.Log("Request.Created", "Request", r.Id, $"{number}: {r.Subject}");
        return r.Id;
    }

    public void UpdateDraft(ApprovalRequest r, ClaimsPrincipal user, long expectedVersion)
    {
        ValidateRouting(r, user.UserId());
        var n = db.Exec($"""
            UPDATE Requests SET Subject = @Subject, RequestCategory = @RequestCategory, Amount = @Amount, RequiredBy = @RequiredBy, Details = @Details,
                   ReviewerUserId = @ReviewerUserId, ApproverUserId = @ApproverUserId, RowVersion = RowVersion + 1, UpdatedAt = @now
            WHERE Id = @Id AND SubmitterUserId = @uid AND RowVersion = @expectedVersion AND State IN ('{RequestStates.Draft}', '{RequestStates.Returned}')
            """, new { r.Subject, r.RequestCategory, r.Amount, r.RequiredBy, r.Details, r.ReviewerUserId, r.ApproverUserId, r.Id, uid = user.UserId(), expectedVersion, now = Db.Now() });
        if (n == 0) throw new WorkflowException("This request was changed by someone else or can no longer be edited. Please reload it.");
        audit.Log("Request.Edited", "Request", r.Id, r.Subject);
    }

    private void ValidateRouting(ApprovalRequest r, long submitterId)
    {
        if (r.ReviewerUserId == null || r.ApproverUserId == null)
            throw new WorkflowException("Select both a Reviewing Officer and a Sanctioning Authority.");
        if (r.ReviewerUserId == submitterId || r.ApproverUserId == submitterId)
            throw new WorkflowException("You cannot route a request to yourself.");
        if (r.ReviewerUserId == r.ApproverUserId)
            throw new WorkflowException("The Reviewing Officer and Sanctioning Authority must be different people.");
        var ok = db.Scalar<long>("SELECT COUNT(*) FROM Users WHERE Id IN (@a, @b) AND IsActive = 1 AND (',' || Roles || ',') LIKE '%,Approver,%'",
            new { a = r.ReviewerUserId, b = r.ApproverUserId });
        if (ok != 2) throw new WorkflowException("Both selected officers must be active users with the Approver role.");
    }

    public void Execute(long requestId, string actionName, string? remarks, long expectedVersion, ClaimsPrincipal user)
    {
        var r = Get(requestId) ?? throw new WorkflowException("Request not found.");
        if (r.RowVersion != expectedVersion)
            throw new WorkflowException("This request has just been updated by someone else. The latest version is shown below — please review it and try again.");
        var action = AllowedActions(r, user).FirstOrDefault(a => a.Name == actionName)
            ?? throw new WorkflowException($"The action '{actionName}' is not available to you for a request in state '{r.State}'.");
        remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        if (remarks?.Length > 2000) remarks = remarks[..2000];
        if (action.RemarksRequired && remarks == null) throw new WorkflowException($"Remarks are required to {action.Name.ToLowerInvariant()} a request.");

        long? assignTo = action.Name switch
        {
            "Submit" => r.ReviewerUserId,
            "Recommend" => r.ApproverUserId,
            "Return" => r.SubmitterUserId,
            _ => null,
        };
        if (action == Submit)
        {
            ValidateRouting(r, r.SubmitterUserId);
            if (string.IsNullOrWhiteSpace(r.Subject) || string.IsNullOrWhiteSpace(r.Details))
                throw new WorkflowException("Subject and details are required before submitting.");
        }
        var role = action.Name switch
        {
            "Submit" => "Submitter",
            "Recommend" => "Reviewing Officer",
            "Approve" => "Sanctioning Authority",
            "Return" or "Reject" => r.State == RequestStates.Submitted ? "Reviewing Officer" : "Sanctioning Authority",
            "Close" => r.SubmitterUserId == user.UserId() ? "Submitter" : "Administrator",
            _ => "User",
        };

        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var updated = Db.Exec(c, tx, """
            UPDATE Requests SET State = @to, AssignedToUserId = @assignTo, RowVersion = RowVersion + 1, UpdatedAt = @now,
                   SubmittedAt = CASE WHEN @to = 'Submitted' THEN @now ELSE SubmittedAt END,
                   ClosedAt = CASE WHEN @to = 'Closed' THEN @now ELSE ClosedAt END
            WHERE Id = @requestId AND RowVersion = @expectedVersion AND State = @from
            """, new { to = action.ToState, assignTo, now, requestId, expectedVersion, from = r.State });
        if (updated == 0)
            throw new WorkflowException("This request has just been updated by someone else. The latest version is shown below — please review it and try again.");
        AppendAction(c, tx, requestId, action.Name, r.State, action.ToState, user, role, remarks, now);
        NotifyFor(c, tx, r, action, remarks, user);
        tx.Commit();
        audit.Log("Request." + action.Name, "Request", requestId, $"{r.RequestNumber}: {r.State} -> {action.ToState}" + (remarks != null ? $"; remarks: {remarks}" : ""));
    }

    private static void AppendAction(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, long requestId, string action,
        string? from, string to, ClaimsPrincipal user, string role, string? remarks, string now) =>
        Db.Insert(c, tx, """
            INSERT INTO ApprovalActions (RequestId, Action, FromState, ToState, ActorUserId, ActorName, ActorRole, Remarks, Timestamp)
            VALUES (@requestId, @action, @from, @to, @uid, @name, @role, @remarks, @now)
            """, new { requestId, action, from, to, uid = user.UserId(), name = user.DisplayName(), role, remarks, now });

    /// <summary>In-app notifications for a workflow step, written in the same transaction as the step.</summary>
    private void NotifyFor(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, ApprovalRequest r, WorkflowAction action,
        string? remarks, ClaimsPrincipal user)
    {
        var actor = NotificationActor.From(user);
        var link = $"/Requests/Details/{r.Id}";
        var what = $"{r.RequestNumber}: {r.Subject}";
        NotificationMessage M(string type, string title) => new(type, title, remarks, link, "Request", r.Id);
        switch (action.Name)
        {
            case "Submit":
                if (r.ReviewerUserId is long rev) notifications.Notify(c, tx, new[] { rev }, actor, M("Request.Assigned", $"Request for your review — {what}"));
                break;
            case "Recommend":
                if (r.ApproverUserId is long ap) notifications.Notify(c, tx, new[] { ap }, actor, M("Request.Assigned", $"Request for your sanction — {what}"));
                notifications.Notify(c, tx, new[] { r.SubmitterUserId }, actor, M("Request.Recommended", $"Your request was recommended — {what}"));
                break;
            case "Approve":
                notifications.Notify(c, tx, new[] { r.SubmitterUserId }, actor, M("Request.Approved", $"Your request was approved — {what}"));
                break;
            case "Return":
                notifications.Notify(c, tx, new[] { r.SubmitterUserId }, actor, M("Request.Returned", $"Your request was returned for clarification — {what}"));
                break;
            case "Reject":
                notifications.Notify(c, tx, new[] { r.SubmitterUserId }, actor, M("Request.Rejected", $"Your request was rejected — {what}"));
                break;
        }
    }

    public static IEnumerable<WorkflowAction> Actions => AllActions;
}
