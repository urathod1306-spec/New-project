namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Database schema. Created automatically on first start. Approval history and the audit
/// log are protected by triggers so that rows can never be edited or deleted.
/// </summary>
public static class Schema
{
    public const int Version = 1;

    public const string Sql = """
CREATE TABLE IF NOT EXISTS SchemaInfo (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL);

CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY,
    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    DisplayName TEXT NOT NULL,
    Email TEXT,
    Vertical TEXT,
    Roles TEXT NOT NULL DEFAULT 'Staff',
    PasswordHash TEXT,
    MustChangePassword INTEGER NOT NULL DEFAULT 0,
    FailedLogins INTEGER NOT NULL DEFAULT 0,
    LockoutUntil TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    SecurityStamp TEXT NOT NULL,
    LastLoginAt TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedBy TEXT,
    UpdatedAt TEXT
);

CREATE TABLE IF NOT EXISTS MasterItems (
    Id INTEGER PRIMARY KEY,
    Type TEXT NOT NULL,
    Name TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,
    UNIQUE (Type, Name COLLATE NOCASE)
);

CREATE TABLE IF NOT EXISTS Staff (
    StaffId INTEGER PRIMARY KEY,
    EmployeeCode TEXT NOT NULL UNIQUE COLLATE NOCASE,
    FullName TEXT NOT NULL,
    Designation TEXT,
    Vertical TEXT,
    Location TEXT,
    OfficialEmail TEXT,
    Extension TEXT,
    Mobile TEXT,
    ShowMobile INTEGER NOT NULL DEFAULT 0,
    PhotographPath TEXT,
    ActiveFrom TEXT,
    ActiveTo TEXT,
    UpdatedBy TEXT,
    UpdatedAt TEXT
);
CREATE INDEX IF NOT EXISTS IX_Staff_Name ON Staff(FullName);

CREATE TABLE IF NOT EXISTS Circulars (
    Id INTEGER PRIMARY KEY,
    CircularNumber TEXT NOT NULL UNIQUE COLLATE NOCASE,
    Title TEXT NOT NULL,
    Category TEXT NOT NULL,
    IssuingVertical TEXT,
    IssueDate TEXT NOT NULL,
    EffectiveDate TEXT,
    Keywords TEXT,
    Synopsis TEXT,
    Visibility TEXT NOT NULL DEFAULT 'AllStaff',
    Status TEXT NOT NULL DEFAULT 'Draft',
    SupersedesId INTEGER REFERENCES Circulars(Id),
    SupersededById INTEGER REFERENCES Circulars(Id),
    CurrentVersion INTEGER NOT NULL DEFAULT 0,
    CreatedBy TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedBy TEXT,
    UpdatedAt TEXT,
    PublishedBy TEXT,
    PublishedAt TEXT,
    WithdrawnBy TEXT,
    WithdrawnAt TEXT,
    WithdrawalReason TEXT
);
CREATE INDEX IF NOT EXISTS IX_Circulars_Issue ON Circulars(IssueDate DESC);

CREATE TABLE IF NOT EXISTS CircularFiles (
    Id INTEGER PRIMARY KEY,
    CircularId INTEGER NOT NULL REFERENCES Circulars(Id),
    Version INTEGER NOT NULL,
    StoredPath TEXT NOT NULL,
    OriginalFileName TEXT NOT NULL,
    ContentType TEXT NOT NULL,
    SizeBytes INTEGER NOT NULL,
    Sha256 TEXT NOT NULL,
    UploadedBy TEXT NOT NULL,
    UploadedAt TEXT NOT NULL,
    UNIQUE (CircularId, Version)
);

CREATE VIRTUAL TABLE IF NOT EXISTS CircularSearch USING fts5(
    CircularNumber, Title, Category, IssuingVertical, Keywords, Synopsis,
    content='Circulars', content_rowid='Id', tokenize='unicode61');

CREATE TRIGGER IF NOT EXISTS Circulars_ai AFTER INSERT ON Circulars BEGIN
    INSERT INTO CircularSearch(rowid, CircularNumber, Title, Category, IssuingVertical, Keywords, Synopsis)
    VALUES (new.Id, new.CircularNumber, new.Title, new.Category, new.IssuingVertical, new.Keywords, new.Synopsis);
END;
CREATE TRIGGER IF NOT EXISTS Circulars_ad AFTER DELETE ON Circulars BEGIN
    INSERT INTO CircularSearch(CircularSearch, rowid, CircularNumber, Title, Category, IssuingVertical, Keywords, Synopsis)
    VALUES ('delete', old.Id, old.CircularNumber, old.Title, old.Category, old.IssuingVertical, old.Keywords, old.Synopsis);
END;
CREATE TRIGGER IF NOT EXISTS Circulars_au AFTER UPDATE ON Circulars BEGIN
    INSERT INTO CircularSearch(CircularSearch, rowid, CircularNumber, Title, Category, IssuingVertical, Keywords, Synopsis)
    VALUES ('delete', old.Id, old.CircularNumber, old.Title, old.Category, old.IssuingVertical, old.Keywords, old.Synopsis);
    INSERT INTO CircularSearch(rowid, CircularNumber, Title, Category, IssuingVertical, Keywords, Synopsis)
    VALUES (new.Id, new.CircularNumber, new.Title, new.Category, new.IssuingVertical, new.Keywords, new.Synopsis);
END;

CREATE TABLE IF NOT EXISTS Notices (
    Id INTEGER PRIMARY KEY,
    Title TEXT NOT NULL,
    Body TEXT,
    IsImportant INTEGER NOT NULL DEFAULT 0,
    StartsOn TEXT NOT NULL,
    EndsOn TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    PostedBy TEXT NOT NULL,
    PostedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Requests (
    Id INTEGER PRIMARY KEY,
    RequestNumber TEXT UNIQUE,
    FormType TEXT NOT NULL DEFAULT 'GeneralAdministrativeApproval',
    Subject TEXT NOT NULL,
    RequestCategory TEXT,
    Amount REAL,
    RequiredBy TEXT,
    Details TEXT NOT NULL,
    SubmitterUserId INTEGER NOT NULL REFERENCES Users(Id),
    SubmitterName TEXT NOT NULL,
    SubmitterVertical TEXT,
    ReviewerUserId INTEGER REFERENCES Users(Id),
    ApproverUserId INTEGER REFERENCES Users(Id),
    State TEXT NOT NULL,
    AssignedToUserId INTEGER REFERENCES Users(Id),
    RowVersion INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    SubmittedAt TEXT,
    UpdatedAt TEXT NOT NULL,
    ClosedAt TEXT
);
CREATE INDEX IF NOT EXISTS IX_Requests_Assigned ON Requests(AssignedToUserId, State);
CREATE INDEX IF NOT EXISTS IX_Requests_Submitter ON Requests(SubmitterUserId);

CREATE TABLE IF NOT EXISTS ApprovalActions (
    Id INTEGER PRIMARY KEY,
    RequestId INTEGER NOT NULL REFERENCES Requests(Id),
    Action TEXT NOT NULL,
    FromState TEXT,
    ToState TEXT NOT NULL,
    ActorUserId INTEGER NOT NULL,
    ActorName TEXT NOT NULL,
    ActorRole TEXT NOT NULL,
    Remarks TEXT,
    Timestamp TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ApprovalActions_Request ON ApprovalActions(RequestId);
CREATE TRIGGER IF NOT EXISTS ApprovalActions_no_update BEFORE UPDATE ON ApprovalActions
BEGIN SELECT RAISE(ABORT, 'Approval history is append-only'); END;
CREATE TRIGGER IF NOT EXISTS ApprovalActions_no_delete BEFORE DELETE ON ApprovalActions
BEGIN SELECT RAISE(ABORT, 'Approval history is append-only'); END;

CREATE TABLE IF NOT EXISTS Attachments (
    Id INTEGER PRIMARY KEY,
    RequestId INTEGER NOT NULL REFERENCES Requests(Id),
    StoredPath TEXT NOT NULL,
    OriginalFileName TEXT NOT NULL,
    ContentType TEXT NOT NULL,
    SizeBytes INTEGER NOT NULL,
    Sha256 TEXT NOT NULL,
    UploadedByUserId INTEGER NOT NULL,
    UploadedBy TEXT NOT NULL,
    UploadedAt TEXT NOT NULL
);
CREATE TRIGGER IF NOT EXISTS Attachments_no_delete BEFORE DELETE ON Attachments
BEGIN SELECT RAISE(ABORT, 'Attachments cannot be deleted'); END;

CREATE TABLE IF NOT EXISTS AuditLog (
    Id INTEGER PRIMARY KEY,
    Timestamp TEXT NOT NULL,
    Actor TEXT NOT NULL,
    Action TEXT NOT NULL,
    EntityType TEXT,
    EntityId TEXT,
    Details TEXT,
    IpAddress TEXT,
    PrevHash TEXT NOT NULL,
    Hash TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_AuditLog_Time ON AuditLog(Timestamp);
CREATE TRIGGER IF NOT EXISTS AuditLog_no_update BEFORE UPDATE ON AuditLog
BEGIN SELECT RAISE(ABORT, 'Audit log is append-only'); END;
CREATE TRIGGER IF NOT EXISTS AuditLog_no_delete BEFORE DELETE ON AuditLog
BEGIN SELECT RAISE(ABORT, 'Audit log is append-only'); END;

INSERT INTO SchemaInfo (Version, AppliedAt)
SELECT 1, strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE NOT EXISTS (SELECT 1 FROM SchemaInfo);
""";
}
