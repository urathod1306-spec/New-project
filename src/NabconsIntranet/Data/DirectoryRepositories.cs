using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public sealed class MasterRepository(Db db)
{
    public List<MasterItem> List(string type, bool activeOnly = true) => db.Query<MasterItem>(
        "SELECT * FROM MasterItems WHERE Type = @type " + (activeOnly ? "AND IsActive = 1 " : "") + "ORDER BY SortOrder, Name",
        new { type });

    /// <summary>Names for drop-downs. The office/location list (all state and UT capitals) is shown alphabetically.</summary>
    public List<string> Names(string type)
    {
        var names = List(type).Select(m => m.Name);
        return (type == MasterTypes.Location ? names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase) : names).ToList();
    }

    public MasterItem? Get(long id) => db.Single<MasterItem>("SELECT * FROM MasterItems WHERE Id = @id", new { id });

    public bool Exists(string type, string name) =>
        db.Scalar<long>("SELECT COUNT(*) FROM MasterItems WHERE Type = @type AND Name = @name COLLATE NOCASE", new { type, name }) > 0;

    public long Add(string type, string name, long sortOrder = 0) => db.Insert(
        "INSERT INTO MasterItems (Type, Name, SortOrder, IsActive) VALUES (@type, @name, @sortOrder, 1)",
        new { type, name = name.Trim(), sortOrder });

    public void SetActive(long id, bool active) => db.Exec("UPDATE MasterItems SET IsActive = @active WHERE Id = @id", new { id, active });

    public void Rename(long id, string name) => db.Exec("UPDATE MasterItems SET Name = @name WHERE Id = @id", new { id, name = name.Trim() });
}

public sealed class StaffRepository(Db db, Fmt fmt)
{
    public const int PageSize = 25;

    public Paged<StaffMember> Search(string? q, string? vertical, string? location, int page, bool includeInactive = false)
    {
        var where = new List<string>();
        var today = fmt.Today.ToString("yyyy-MM-dd");
        if (!includeInactive) where.Add("(ActiveTo IS NULL OR ActiveTo = '' OR ActiveTo >= @today)");
        if (!string.IsNullOrWhiteSpace(q))
            where.Add(@"(FullName LIKE @like ESCAPE '\' OR EmployeeCode LIKE @like ESCAPE '\' OR Designation LIKE @like ESCAPE '\'
                        OR OfficialEmail LIKE @like ESCAPE '\' OR Extension LIKE @like ESCAPE '\')");
        if (!string.IsNullOrWhiteSpace(vertical)) where.Add("Vertical = @vertical");
        if (!string.IsNullOrWhiteSpace(location)) where.Add("Location = @location");
        var sqlWhere = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";
        var args = new { today, like = SearchText.Like(q), vertical, location, take = PageSize, skip = Math.Max(0, page - 1) * PageSize };
        return new Paged<StaffMember>
        {
            Total = db.Scalar<long>("SELECT COUNT(*) FROM Staff" + sqlWhere, args),
            Items = db.Query<StaffMember>(WithPhoto + sqlWhere + " ORDER BY FullName LIMIT @take OFFSET @skip", args),
            Page = page,
            PageSize = PageSize,
        };
    }

    public List<StaffMember> All() => db.Query<StaffMember>("SELECT * FROM Staff ORDER BY FullName");

    /// <summary>Staff rows plus the photo of the linked (active) user account, if any.</summary>
    private const string WithPhoto = """
        SELECT Staff.*,
               (SELECT u.Id FROM Users u WHERE u.StaffId = Staff.StaffId AND u.IsActive = 1 AND u.PhotoVersion > 0 LIMIT 1) AS PhotoUserId,
               COALESCE((SELECT u.PhotoVersion FROM Users u WHERE u.StaffId = Staff.StaffId AND u.IsActive = 1 AND u.PhotoVersion > 0 LIMIT 1), 0) AS PhotoVersion
        FROM Staff
        """;

    /// <summary>A directory entry for the contact card — only entries the directory itself lists (active staff).</summary>
    public StaffMember? GetForCard(long id) => db.Single<StaffMember>(WithPhoto + " WHERE Staff.StaffId = @id AND (ActiveTo IS NULL OR ActiveTo = '' OR ActiveTo >= @today)",
        new { id, today = fmt.Today.ToString("yyyy-MM-dd") });

    public StaffMember? Get(long id) => db.Single<StaffMember>("SELECT * FROM Staff WHERE StaffId = @id", new { id });

    public StaffMember? GetByCode(string code) => db.Single<StaffMember>("SELECT * FROM Staff WHERE EmployeeCode = @code", new { code });

    public long Count() => db.Scalar<long>("SELECT COUNT(*) FROM Staff WHERE ActiveTo IS NULL OR ActiveTo = '' OR ActiveTo >= @today",
        new { today = fmt.Today.ToString("yyyy-MM-dd") });

    public List<string> DistinctValues(string column) => column is "Vertical" or "Location"
        ? db.Strings($"SELECT DISTINCT {column} FROM Staff WHERE {column} IS NOT NULL AND {column} <> '' ORDER BY {column}")
        : new();

    /// <summary>Insert or update by EmployeeCode. Returns true if a new row was created.</summary>
    public bool Upsert(StaffMember s, string actor)
    {
        s.UpdatedBy = actor;
        s.UpdatedAt = Db.Now();
        var existing = GetByCode(s.EmployeeCode);
        if (existing == null)
        {
            s.StaffId = db.Insert("""
                INSERT INTO Staff (EmployeeCode, FullName, Designation, Vertical, Location, OfficialEmail, Extension, Mobile, ShowMobile, PhotographPath, ActiveFrom, ActiveTo, UpdatedBy, UpdatedAt)
                VALUES (@EmployeeCode, @FullName, @Designation, @Vertical, @Location, @OfficialEmail, @Extension, @Mobile, @ShowMobile, @PhotographPath, @ActiveFrom, @ActiveTo, @UpdatedBy, @UpdatedAt)
                """, s);
            return true;
        }
        s.StaffId = existing.StaffId;
        db.Exec("""
            UPDATE Staff SET FullName = @FullName, Designation = @Designation, Vertical = @Vertical, Location = @Location,
                   OfficialEmail = @OfficialEmail, Extension = @Extension, Mobile = @Mobile, ShowMobile = @ShowMobile,
                   ActiveFrom = @ActiveFrom, ActiveTo = @ActiveTo, UpdatedBy = @UpdatedBy, UpdatedAt = @UpdatedAt
            WHERE StaffId = @StaffId
            """, s);
        return false;
    }

    public int DeactivateMissing(IReadOnlyCollection<string> keepCodes, string actor)
    {
        var yesterday = fmt.Today.AddDays(-1).ToString("yyyy-MM-dd");
        int n = 0;
        foreach (var s in All().Where(s => s.IsActive(fmt.Today) && !keepCodes.Contains(s.EmployeeCode, StringComparer.OrdinalIgnoreCase)))
        {
            n += db.Exec("UPDATE Staff SET ActiveTo = @yesterday, UpdatedBy = @actor, UpdatedAt = @now WHERE StaffId = @id",
                new { yesterday, actor, now = Db.Now(), id = s.StaffId });
        }
        return n;
    }
}

public sealed class NoticeRepository(Db db, Fmt fmt)
{
    public List<Notice> Current()
    {
        var today = fmt.Today.ToString("yyyy-MM-dd");
        return db.Query<Notice>("""
            SELECT * FROM Notices WHERE IsActive = 1 AND StartsOn <= @today AND (EndsOn IS NULL OR EndsOn = '' OR EndsOn >= @today)
            ORDER BY IsImportant DESC, StartsOn DESC, Id DESC LIMIT 10
            """, new { today });
    }

    public List<Notice> All() => db.Query<Notice>("SELECT * FROM Notices ORDER BY Id DESC LIMIT 200");

    public long Add(Notice n) => db.Insert("""
        INSERT INTO Notices (Title, Body, IsImportant, StartsOn, EndsOn, IsActive, PostedBy, PostedAt)
        VALUES (@Title, @Body, @IsImportant, @StartsOn, @EndsOn, 1, @PostedBy, @PostedAt)
        """, n);

    public void Deactivate(long id) => db.Exec("UPDATE Notices SET IsActive = 0 WHERE Id = @id", new { id });
}
