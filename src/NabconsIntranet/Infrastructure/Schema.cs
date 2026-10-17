namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Database schema. Created automatically on first start. Approval history and the audit
/// log are protected by triggers so that rows can never be edited or deleted.
/// </summary>
public static class Schema
{
    /// <summary>Latest schema version this program understands (1 + number of migrations).</summary>
    public static int Version => Migrations.Length == 0 ? 1 : Migrations.Max(m => m.Version);

    /// <summary>
    /// Database upgrades, applied in order on start-up to existing databases (and to new ones after
    /// the base schema below). RULES — so live data is never lost:
    ///   * only ADD: new tables, new columns (with a DEFAULT), new indexes; never DROP or rename;
    ///   * never edit or remove an entry once released; add a new one with the next version number;
    ///   * each step runs in a transaction, after an automatic snapshot of the database.
    /// Example: (2, "Add Phone to Notices", "ALTER TABLE Notices ADD COLUMN Phone TEXT;"),
    /// </summary>
    public static readonly (int Version, string Description, string Sql)[] Migrations =
    {
        (2, "Self-registration requests (v1.1.0)", """
            CREATE TABLE IF NOT EXISTS RegistrationRequests (
                Id INTEGER PRIMARY KEY,
                Mobile TEXT NOT NULL,
                StaffId INTEGER,
                EmployeeCode TEXT,
                FullName TEXT NOT NULL,
                Designation TEXT,
                Vertical TEXT,
                Location TEXT,
                OfficialEmail TEXT,
                PasswordHash TEXT,
                Status TEXT NOT NULL DEFAULT 'Pending',
                SubmittedAt TEXT NOT NULL,
                IpAddress TEXT,
                DecidedBy TEXT,
                DecidedAt TEXT,
                DecisionRemarks TEXT,
                CreatedUserId INTEGER
            );
            CREATE INDEX IF NOT EXISTS IX_RegistrationRequests_Mobile ON RegistrationRequests(Mobile, Status);
            """),
        // Adds every state and union-territory capital to the office/location list of an EXISTING installation.
        // Skips names already present (also "Mumbai (Head Office)"-style entries). On a brand-new database the
        // list is still empty here, and the first-run setup adds the same capitals itself.
        (3, "State and UT capitals added to office/location list (v1.1.1)", """
            WITH c(Name, Ord) AS (VALUES
                ('Agartala', 0),
                ('Aizawl', 1),
                ('Amaravati', 2),
                ('Bengaluru', 3),
                ('Bhopal', 4),
                ('Bhubaneswar', 5),
                ('Chandigarh', 6),
                ('Chennai', 7),
                ('Daman', 8),
                ('Dehradun', 9),
                ('Dispur', 10),
                ('Gandhinagar', 11),
                ('Gangtok', 12),
                ('Hyderabad', 13),
                ('Imphal', 14),
                ('Itanagar', 15),
                ('Jaipur', 16),
                ('Jammu', 17),
                ('Kavaratti', 18),
                ('Kohima', 19),
                ('Kolkata', 20),
                ('Leh', 21),
                ('Lucknow', 22),
                ('Mumbai', 23),
                ('New Delhi', 24),
                ('Panaji', 25),
                ('Patna', 26),
                ('Puducherry', 27),
                ('Raipur', 28),
                ('Ranchi', 29),
                ('Shillong', 30),
                ('Shimla', 31),
                ('Srinagar', 32),
                ('Sri Vijaya Puram (Port Blair)', 33),
                ('Thiruvananthapuram', 34)
            )
            INSERT INTO MasterItems (Type, Name, SortOrder, IsActive)
            SELECT 'Location', c.Name, 100 + c.Ord, 1 FROM c
            WHERE EXISTS (SELECT 1 FROM MasterItems WHERE Type = 'Location')
              AND NOT EXISTS (SELECT 1 FROM MasterItems m WHERE m.Type = 'Location'
                              AND (m.Name = c.Name COLLATE NOCASE OR m.Name LIKE c.Name || ' (%'));
            """),
        // v1.2.0: profile links/photos, vertical-change approvals, profile audit trail and the business-lead pipeline.
        // Additive only. Master-list values are inserted only when missing.
        (4, "Profiles, photos and business leads (v1.2.0)", """
            ALTER TABLE Users ADD COLUMN StaffId INTEGER;
            ALTER TABLE Users ADD COLUMN PhotoVersion INTEGER NOT NULL DEFAULT 0;
            CREATE INDEX IF NOT EXISTS IX_Users_StaffId ON Users(StaffId);

            CREATE TABLE IF NOT EXISTS ProfileChangeRequests (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER NOT NULL,
                StaffId INTEGER,
                Field TEXT NOT NULL,
                OldValue TEXT,
                NewValue TEXT,
                Status TEXT NOT NULL DEFAULT 'Pending',
                RequestedAt TEXT NOT NULL,
                DecidedBy TEXT,
                DecidedAt TEXT,
                DecisionRemarks TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_ProfileChangeRequests_Status ON ProfileChangeRequests(Status, UserId);

            CREATE TABLE IF NOT EXISTS ProfileAudit (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER NOT NULL,
                StaffId INTEGER,
                Field TEXT NOT NULL,
                OldValue TEXT,
                NewValue TEXT,
                ChangedBy TEXT NOT NULL,
                ChangedAt TEXT NOT NULL
            );
            CREATE TRIGGER IF NOT EXISTS ProfileAudit_no_update BEFORE UPDATE ON ProfileAudit
            BEGIN SELECT RAISE(ABORT, 'Profile audit is append-only'); END;
            CREATE TRIGGER IF NOT EXISTS ProfileAudit_no_delete BEFORE DELETE ON ProfileAudit
            BEGIN SELECT RAISE(ABORT, 'Profile audit is append-only'); END;

            CREATE TABLE IF NOT EXISTS LeadSerials (FinancialYear TEXT PRIMARY KEY, LastSerial INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS Leads (
                Id INTEGER PRIMARY KEY,
                Reference TEXT NOT NULL UNIQUE,
                FinancialYear TEXT NOT NULL,
                Serial INTEGER NOT NULL,
                Title TEXT NOT NULL,
                ClientName TEXT NOT NULL,
                ClientType TEXT,
                Sector TEXT,
                StateLocation TEXT,
                Source TEXT,
                EstimatedValueLakh REAL,
                DueDate TEXT,
                ContactName TEXT,
                ContactDesignation TEXT,
                ContactPhone TEXT,
                ContactEmail TEXT,
                Description TEXT NOT NULL,
                TenderNumber TEXT,
                Status TEXT NOT NULL,
                OwnerUserId INTEGER,
                SubmitterUserId INTEGER NOT NULL,
                SubmitterName TEXT NOT NULL,
                SubmitterVertical TEXT,
                LossReason TEXT,
                LossReasonText TEXT,
                AwardedValueLakh REAL,
                AwardDate TEXT,
                RowVersion INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UNIQUE (FinancialYear, Serial)
            );
            CREATE INDEX IF NOT EXISTS IX_Leads_Submitter ON Leads(SubmitterUserId);
            CREATE INDEX IF NOT EXISTS IX_Leads_Status ON Leads(Status);

            CREATE TABLE IF NOT EXISTS LeadEvents (
                Id INTEGER PRIMARY KEY,
                LeadId INTEGER NOT NULL REFERENCES Leads(Id),
                EventType TEXT NOT NULL,
                FromStatus TEXT,
                ToStatus TEXT,
                Remarks TEXT,
                Detail TEXT,
                ActorUserId INTEGER NOT NULL,
                ActorName TEXT NOT NULL,
                Timestamp TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_LeadEvents_Lead ON LeadEvents(LeadId);
            CREATE TRIGGER IF NOT EXISTS LeadEvents_no_update BEFORE UPDATE ON LeadEvents
            BEGIN SELECT RAISE(ABORT, 'Lead timeline is append-only'); END;
            CREATE TRIGGER IF NOT EXISTS LeadEvents_no_delete BEFORE DELETE ON LeadEvents
            BEGIN SELECT RAISE(ABORT, 'Lead timeline is append-only'); END;

            CREATE TABLE IF NOT EXISTS LeadAttachments (
                Id INTEGER PRIMARY KEY,
                LeadId INTEGER NOT NULL REFERENCES Leads(Id),
                Side TEXT NOT NULL,
                StoredPath TEXT NOT NULL,
                OriginalFileName TEXT NOT NULL,
                ContentType TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                Sha256 TEXT NOT NULL,
                UploadedByUserId INTEGER NOT NULL,
                UploadedBy TEXT NOT NULL,
                UploadedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_LeadAttachments_Lead ON LeadAttachments(LeadId);
            CREATE TRIGGER IF NOT EXISTS LeadAttachments_no_delete BEFORE DELETE ON LeadAttachments
            BEGIN SELECT RAISE(ABORT, 'Lead attachments cannot be deleted'); END;

            WITH m(Type, Name, Ord) AS (VALUES
                ('ClientType','Central Govt',1),('ClientType','State Govt',2),('ClientType','PSU',3),('ClientType','Bank/FI',4),
                ('ClientType','Cooperative',5),('ClientType','NGO/Trust',6),('ClientType','Private',7),
                ('ClientType','International/Multilateral',8),('ClientType','Other',9),
                ('LeadSector','Agriculture & Allied',1),('LeadSector','Rural Infrastructure',2),('LeadSector','Climate Change & Environment',3),
                ('LeadSector','Watershed & Natural Resources',4),('LeadSector','Financial Inclusion',5),('LeadSector','Microfinance & Livelihoods',6),
                ('LeadSector','Cooperatives & FPOs',7),('LeadSector','Skill Development',8),('LeadSector','IT & Digital',9),
                ('LeadSector','Impact Evaluation & Studies',10),('LeadSector','Capacity Building & Training',11),('LeadSector','Other',12),
                ('LeadSource','Tender portal',1),('LeadSource','Client referral',2),('LeadSource','NABARD RO/HO',3),
                ('LeadSource','Direct approach',4),('LeadSource','Repeat client',5),('LeadSource','Other',6),
                ('LeadLossReason','Price',1),('LeadLossReason','Technical score',2),('LeadLossReason','Eligibility',3),
                ('LeadLossReason','Client cancelled',4),('LeadLossReason','Not aligned',5),('LeadLossReason','Capacity',6),('LeadLossReason','Other',7)
            )
            INSERT INTO MasterItems (Type, Name, SortOrder, IsActive)
            SELECT m.Type, m.Name, m.Ord, 1 FROM m
            WHERE NOT EXISTS (SELECT 1 FROM MasterItems x WHERE x.Type = m.Type AND x.Name = m.Name COLLATE NOCASE);

            INSERT INTO MasterItems (Type, Name, SortOrder, IsActive)
            SELECT 'Vertical', 'BDU', 100, 1
            WHERE EXISTS (SELECT 1 FROM MasterItems WHERE Type = 'Vertical')
              AND NOT EXISTS (SELECT 1 FROM MasterItems WHERE Type = 'Vertical' AND Name = 'BDU' COLLATE NOCASE);
            """),
        // v1.3.0: in-app notifications, My Desk preference (and, in the same release, e-Note).
        // Released in 1.3.0: never edit this entry; add a new one.
        (5, "Notifications, My Desk and e-Note (v1.3.0)", """
            ALTER TABLE Users ADD COLUMN OpenDeskAfterSignIn INTEGER NOT NULL DEFAULT 1;

            CREATE TABLE IF NOT EXISTS Notifications (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER NOT NULL REFERENCES Users(Id),
                Type TEXT NOT NULL,
                Title TEXT NOT NULL,
                Message TEXT,
                Link TEXT,
                EntityType TEXT,
                EntityId INTEGER,
                ActorName TEXT,
                CreatedAt TEXT NOT NULL,
                ReadAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Notifications_UserRead ON Notifications(UserId, ReadAt, CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Notifications_UserTime ON Notifications(UserId, CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Notifications_Created ON Notifications(CreatedAt);

            CREATE INDEX IF NOT EXISTS IX_Leads_Owner ON Leads(OwnerUserId, Status, DueDate);
            CREATE INDEX IF NOT EXISTS IX_Requests_SubmitterState ON Requests(SubmitterUserId, State);

            -- e-Note: short code per vertical for note numbers (NCS/EN/<code>/<FY>/<serial>); empty = GEN
            ALTER TABLE MasterItems ADD COLUMN Code TEXT;

            CREATE TABLE IF NOT EXISTS ENoteSerials (
                Code TEXT NOT NULL,
                FinancialYear TEXT NOT NULL,
                LastSerial INTEGER NOT NULL,
                PRIMARY KEY (Code, FinancialYear)
            );

            CREATE TABLE IF NOT EXISTS ENotes (
                Id INTEGER PRIMARY KEY,
                NoteNumber TEXT UNIQUE,
                NumberCode TEXT,
                FinancialYear TEXT,
                Serial INTEGER,
                Subject TEXT NOT NULL,
                Category TEXT,
                InitiatorUserId INTEGER NOT NULL REFERENCES Users(Id),
                InitiatorName TEXT NOT NULL,
                InitiatorDesignation TEXT,
                InitiatorVertical TEXT,
                CurrentHolderUserId INTEGER REFERENCES Users(Id),
                CurrentHolderName TEXT,
                PreviousHolderUserId INTEGER,
                HeldSince TEXT,
                State TEXT NOT NULL,
                Outcome TEXT,
                DraftBody TEXT,
                CurrentBodyVersion INTEGER NOT NULL DEFAULT 0,
                LastSeq INTEGER NOT NULL DEFAULT 0,
                IsConfidential INTEGER NOT NULL DEFAULT 0,
                DeclarationAccepted INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                SubmittedAt TEXT,
                DecidedAt TEXT,
                DecidedByUserId INTEGER,
                DecidedByName TEXT,
                DecidedByDesignation TEXT,
                DecisionRemarks TEXT,
                ClosedAt TEXT,
                UpdatedAt TEXT NOT NULL,
                RowVersion INTEGER NOT NULL DEFAULT 1,
                PdfPath TEXT,
                PdfSha256 TEXT,
                PdfState TEXT,
                RecordSha256 TEXT,
                LastAgeingAlertOn TEXT,
                UNIQUE (NumberCode, FinancialYear, Serial)
            );
            CREATE INDEX IF NOT EXISTS IX_ENotes_Holder ON ENotes(CurrentHolderUserId, State, HeldSince);
            CREATE INDEX IF NOT EXISTS IX_ENotes_Initiator ON ENotes(InitiatorUserId, State, UpdatedAt);
            CREATE INDEX IF NOT EXISTS IX_ENotes_Approved ON ENotes(Outcome, IsConfidential, DecidedAt);
            CREATE INDEX IF NOT EXISTS IX_ENotes_StateTime ON ENotes(State, UpdatedAt);
            CREATE INDEX IF NOT EXISTS IX_ENotes_Submitted ON ENotes(SubmittedAt);

            CREATE TABLE IF NOT EXISTS ENoteBodyVersions (
                Id INTEGER PRIMARY KEY,
                NoteId INTEGER NOT NULL REFERENCES ENotes(Id),
                Version INTEGER NOT NULL,
                BodyText TEXT NOT NULL,
                Sha256 TEXT NOT NULL,
                CreatedByUserId INTEGER NOT NULL,
                CreatedBy TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UNIQUE (NoteId, Version)
            );
            CREATE TRIGGER IF NOT EXISTS ENoteBodyVersions_no_update BEFORE UPDATE ON ENoteBodyVersions
            BEGIN SELECT RAISE(ABORT, 'Submitted note text is append-only'); END;
            CREATE TRIGGER IF NOT EXISTS ENoteBodyVersions_no_delete BEFORE DELETE ON ENoteBodyVersions
            BEGIN SELECT RAISE(ABORT, 'Submitted note text is append-only'); END;

            CREATE TABLE IF NOT EXISTS ENoteMovements (
                Id INTEGER PRIMARY KEY,
                NoteId INTEGER NOT NULL REFERENCES ENotes(Id),
                Seq INTEGER NOT NULL,
                Action TEXT NOT NULL,
                FromUserId INTEGER,
                FromName TEXT,
                FromDesignation TEXT,
                ToUserId INTEGER,
                ToName TEXT,
                ToDesignation TEXT,
                FromState TEXT,
                ToState TEXT NOT NULL,
                Remarks TEXT,
                Timestamp TEXT NOT NULL,
                UNIQUE (NoteId, Seq)
            );
            CREATE INDEX IF NOT EXISTS IX_ENoteMovements_To ON ENoteMovements(ToUserId);
            CREATE TRIGGER IF NOT EXISTS ENoteMovements_no_update BEFORE UPDATE ON ENoteMovements
            BEGIN SELECT RAISE(ABORT, 'Note movements are append-only'); END;
            CREATE TRIGGER IF NOT EXISTS ENoteMovements_no_delete BEFORE DELETE ON ENoteMovements
            BEGIN SELECT RAISE(ABORT, 'Note movements are append-only'); END;

            CREATE TABLE IF NOT EXISTS ENoteComments (
                Id INTEGER PRIMARY KEY,
                NoteId INTEGER NOT NULL REFERENCES ENotes(Id),
                Seq INTEGER NOT NULL,
                AuthorUserId INTEGER NOT NULL,
                AuthorName TEXT NOT NULL,
                AuthorDesignation TEXT,
                Text TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                UNIQUE (NoteId, Seq)
            );
            CREATE TRIGGER IF NOT EXISTS ENoteComments_no_update BEFORE UPDATE ON ENoteComments
            BEGIN SELECT RAISE(ABORT, 'Note-sheet comments are append-only'); END;
            CREATE TRIGGER IF NOT EXISTS ENoteComments_no_delete BEFORE DELETE ON ENoteComments
            BEGIN SELECT RAISE(ABORT, 'Note-sheet comments are append-only'); END;

            CREATE TABLE IF NOT EXISTS ENoteAttachments (
                Id INTEGER PRIMARY KEY,
                NoteId INTEGER NOT NULL REFERENCES ENotes(Id),
                StoredPath TEXT NOT NULL,
                OriginalFileName TEXT NOT NULL,
                ContentType TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                Sha256 TEXT NOT NULL,
                UploadedByUserId INTEGER NOT NULL,
                UploadedByName TEXT NOT NULL,
                UploadedAt TEXT NOT NULL,
                StageSeq INTEGER NOT NULL DEFAULT 0,
                Stage TEXT,
                SupersededById INTEGER
            );
            CREATE INDEX IF NOT EXISTS IX_ENoteAttachments_Note ON ENoteAttachments(NoteId);
            -- Only draft attachments (note never submitted) may be removed; afterwards they are append-only.
            CREATE TRIGGER IF NOT EXISTS ENoteAttachments_no_delete BEFORE DELETE ON ENoteAttachments
            WHEN (SELECT SubmittedAt FROM ENotes WHERE Id = old.NoteId) IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'Attachments of a submitted note cannot be removed'); END;
            -- The only change allowed: marking a file "superseded by" a newer one, once.
            CREATE TRIGGER IF NOT EXISTS ENoteAttachments_no_update BEFORE UPDATE ON ENoteAttachments
            WHEN NOT (old.SupersededById IS NULL AND new.SupersededById IS NOT NULL AND new.NoteId = old.NoteId
                      AND new.StoredPath = old.StoredPath AND new.OriginalFileName = old.OriginalFileName AND new.ContentType = old.ContentType
                      AND new.SizeBytes = old.SizeBytes AND new.Sha256 = old.Sha256 AND new.UploadedByUserId = old.UploadedByUserId
                      AND new.UploadedByName = old.UploadedByName AND new.UploadedAt = old.UploadedAt AND new.StageSeq = old.StageSeq
                      AND new.Stage IS old.Stage)
            BEGIN SELECT RAISE(ABORT, 'Note attachments are append-only'); END;

            CREATE TABLE IF NOT EXISTS ENoteParticipants (
                NoteId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                FirstInvolvedAt TEXT NOT NULL,
                PRIMARY KEY (NoteId, UserId)
            ) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS IX_ENoteParticipants_User ON ENoteParticipants(UserId, NoteId);

            CREATE VIRTUAL TABLE IF NOT EXISTS ENoteSearch USING fts5(
                NoteNumber, Subject, Category, Body, content='', contentless_delete=1, tokenize='unicode61');

            WITH m(Name, Ord) AS (VALUES ('General administration',1),('Finance & accounts',2),('Procurement',3),
                ('HR (non-disciplinary)',4),('Projects & assignments',5),('IT',6),('Others',7))
            INSERT INTO MasterItems (Type, Name, SortOrder, IsActive)
            SELECT 'ENoteCategory', m.Name, m.Ord, 1 FROM m
            WHERE NOT EXISTS (SELECT 1 FROM MasterItems x WHERE x.Type = 'ENoteCategory' AND x.Name = m.Name COLLATE NOCASE);
            """),
    };

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
