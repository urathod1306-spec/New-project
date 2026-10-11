using System.Security.Claims;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public sealed class CircularFilter
{
    public string? Q { get; set; }
    public string? Category { get; set; }
    public string? Vertical { get; set; }
    public int? Year { get; set; }
    public string? Status { get; set; }
    public bool IncludeWithdrawn { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class CircularRepository(Db db)
{
    public const int PageSize = 20;

    private const string Select = """
        SELECT c.*, s.CircularNumber AS SupersededByNumber, p.CircularNumber AS SupersedesNumber
        FROM Circulars c
        LEFT JOIN Circulars s ON s.Id = c.SupersededById
        LEFT JOIN Circulars p ON p.Id = c.SupersedesId
        """;

    /// <summary>Visibility rule applied to every read: what this user may see.</summary>
    private static (string sql, string? vertical, bool all) Scope(ClaimsPrincipal user)
    {
        if (user.SeesAllCirculars()) return ("1 = 1", null, true);
        return ($"""
            c.Status <> '{CircularStatus.Draft}' AND (c.Visibility = '{Visibility.AllStaff}'
              OR (c.Visibility = '{Visibility.Vertical}' AND c.IssuingVertical = @userVertical))
            """, user.Vertical() ?? "\u0000none", false);
    }

    public Paged<Circular> Search(CircularFilter f, ClaimsPrincipal user)
    {
        var (scope, userVertical, all) = Scope(user);
        var where = new List<string> { scope };
        var fts = SearchText.ToFtsQuery(f.Q);
        if (fts != null) where.Add("c.Id IN (SELECT rowid FROM CircularSearch WHERE CircularSearch MATCH @fts)");
        if (!string.IsNullOrWhiteSpace(f.Category)) where.Add("c.Category = @Category");
        if (!string.IsNullOrWhiteSpace(f.Vertical)) where.Add("c.IssuingVertical = @Vertical");
        if (f.Year is > 1900 and < 3000) where.Add("substr(c.IssueDate, 1, 4) = @year");
        if (all && !string.IsNullOrWhiteSpace(f.Status))
        {
            if (f.Status == "Superseded") where.Add($"c.Status = '{CircularStatus.Published}' AND c.SupersededById IS NOT NULL");
            else where.Add("c.Status = @Status");
        }
        else if (!f.IncludeWithdrawn) where.Add($"c.Status <> '{CircularStatus.Withdrawn}'");

        var sqlWhere = " WHERE " + string.Join(" AND ", where);
        var args = new
        {
            userVertical, fts, f.Category, f.Vertical, year = f.Year?.ToString(), f.Status,
            take = PageSize, skip = Math.Max(0, f.Page - 1) * PageSize,
        };
        return new Paged<Circular>
        {
            Total = db.Scalar<long>("SELECT COUNT(*) FROM Circulars c" + sqlWhere, args),
            Items = db.Query<Circular>(Select + sqlWhere + " ORDER BY c.IssueDate DESC, c.Id DESC LIMIT @take OFFSET @skip", args),
            Page = f.Page,
            PageSize = PageSize,
        };
    }

    public List<Circular> Latest(ClaimsPrincipal user, int count)
    {
        var (scope, userVertical, _) = Scope(user);
        return db.Query<Circular>(Select + $" WHERE {scope} AND c.Status = '{CircularStatus.Published}' ORDER BY c.IssueDate DESC, c.Id DESC LIMIT @count",
            new { userVertical, count });
    }

    /// <summary>Returns the circular only if this user is allowed to see it.</summary>
    public Circular? GetVisible(long id, ClaimsPrincipal user)
    {
        var (scope, userVertical, _) = Scope(user);
        return db.Single<Circular>(Select + $" WHERE c.Id = @id AND {scope}", new { id, userVertical });
    }

    public Circular? Get(long id) => db.Single<Circular>(Select + " WHERE c.Id = @id", new { id });

    public List<Circular> SupersededBy(long id) => db.Query<Circular>(Select + " WHERE c.SupersededById = @id", new { id });

    public List<Circular> PublishedForPicker(long excludeId) => db.Query<Circular>(
        $"SELECT * FROM Circulars WHERE Status = '{CircularStatus.Published}' AND SupersededById IS NULL AND Id <> @excludeId ORDER BY IssueDate DESC",
        new { excludeId });

    public bool NumberExists(string number, long exceptId) =>
        db.Scalar<long>("SELECT COUNT(*) FROM Circulars WHERE CircularNumber = @number COLLATE NOCASE AND Id <> @exceptId", new { number, exceptId }) > 0;

    public List<int> Years() => db.Strings("SELECT DISTINCT substr(IssueDate, 1, 4) FROM Circulars ORDER BY 1 DESC")
        .Select(s => int.TryParse(s, out var y) ? y : 0).Where(y => y > 0).ToList();

    public long CountPublished() => db.Scalar<long>($"SELECT COUNT(*) FROM Circulars WHERE Status = '{CircularStatus.Published}'");

    public List<CircularFile> Files(long circularId) =>
        db.Query<CircularFile>("SELECT * FROM CircularFiles WHERE CircularId = @circularId ORDER BY Version DESC", new { circularId });

    public CircularFile? GetFile(long fileId) => db.Single<CircularFile>("SELECT * FROM CircularFiles WHERE Id = @fileId", new { fileId });

    public long Create(Circular c)
    {
        c.CreatedAt = Db.Now();
        c.UpdatedAt = c.CreatedAt;
        c.UpdatedBy = c.CreatedBy;
        c.Id = db.Insert("""
            INSERT INTO Circulars (CircularNumber, Title, Category, IssuingVertical, IssueDate, EffectiveDate, Keywords, Synopsis, Visibility, Status, SupersedesId, CurrentVersion, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
            VALUES (@CircularNumber, @Title, @Category, @IssuingVertical, @IssueDate, @EffectiveDate, @Keywords, @Synopsis, @Visibility, 'Draft', @SupersedesId, 0, @CreatedBy, @CreatedAt, @UpdatedBy, @UpdatedAt)
            """, c);
        return c.Id;
    }

    public void UpdateMetadata(Circular c, string actor) => db.Exec("""
        UPDATE Circulars SET CircularNumber = @CircularNumber, Title = @Title, Category = @Category, IssuingVertical = @IssuingVertical,
               IssueDate = @IssueDate, EffectiveDate = @EffectiveDate, Keywords = @Keywords, Synopsis = @Synopsis,
               Visibility = @Visibility, SupersedesId = @SupersedesId, UpdatedBy = @actor, UpdatedAt = @now
        WHERE Id = @Id
        """, new { c.CircularNumber, c.Title, c.Category, c.IssuingVertical, c.IssueDate, c.EffectiveDate, c.Keywords, c.Synopsis, c.Visibility, c.SupersedesId, c.Id, actor, now = Db.Now() });

    public long AddFile(long circularId, StoredFile f, string actor)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var version = Db.Scalar<long>(c, tx, "SELECT COALESCE(MAX(Version), 0) + 1 FROM CircularFiles WHERE CircularId = @circularId", new { circularId });
        Db.Insert(c, tx, """
            INSERT INTO CircularFiles (CircularId, Version, StoredPath, OriginalFileName, ContentType, SizeBytes, Sha256, UploadedBy, UploadedAt)
            VALUES (@circularId, @version, @StoredPath, @OriginalFileName, @ContentType, @SizeBytes, @Sha256, @actor, @now)
            """, new { circularId, version, f.StoredPath, f.OriginalFileName, f.ContentType, f.SizeBytes, f.Sha256, actor, now = Db.Now() });
        Db.Exec(c, tx, "UPDATE Circulars SET CurrentVersion = @version, UpdatedBy = @actor, UpdatedAt = @now WHERE Id = @circularId",
            new { circularId, version, actor, now = Db.Now() });
        tx.Commit();
        return version;
    }

    /// <summary>Publishes a draft; if it supersedes an older circular, that one is marked superseded.</summary>
    public long? Publish(long id, string actor)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var now = Db.Now();
        Db.Exec(c, tx, $"UPDATE Circulars SET Status = '{CircularStatus.Published}', PublishedBy = @actor, PublishedAt = @now, UpdatedBy = @actor, UpdatedAt = @now WHERE Id = @id AND Status = '{CircularStatus.Draft}'",
            new { id, actor, now });
        var supersedes = Db.Scalar<long?>(c, tx, "SELECT SupersedesId FROM Circulars WHERE Id = @id", new { id });
        if (supersedes != null)
            Db.Exec(c, tx, "UPDATE Circulars SET SupersededById = @id, UpdatedBy = @actor, UpdatedAt = @now WHERE Id = @supersedes",
                new { id, supersedes, actor, now });
        tx.Commit();
        return supersedes;
    }

    public void Withdraw(long id, string reason, string actor) => db.Exec($"""
        UPDATE Circulars SET Status = '{CircularStatus.Withdrawn}', WithdrawnBy = @actor, WithdrawnAt = @now, WithdrawalReason = @reason,
               UpdatedBy = @actor, UpdatedAt = @now
        WHERE Id = @id AND Status = '{CircularStatus.Published}'
        """, new { id, reason, actor, now = Db.Now() });
}
