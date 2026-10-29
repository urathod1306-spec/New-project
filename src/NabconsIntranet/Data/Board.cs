using System.Security.Claims;
using System.Security.Cryptography;
using NabconsIntranet.Infrastructure;
using SkiaSharp;

namespace NabconsIntranet.Data;

/// <summary>Settings from the "MessageBoard" section of appsettings.json (safe defaults when absent).</summary>
public sealed class BoardOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxChars { get; set; } = 2000;
    public int MaxPhotosPerPost { get; set; } = 4;
    public int MaxPhotoMB { get; set; } = 5;
    public int MaxPinned { get; set; } = 3;
    /// <summary>Simple guard against accidental floods: posts per person per hour.</summary>
    public int PostsPerHour { get; set; } = 10;
    public const int FullSide = 1600, ThumbSide = 480;
}

public sealed class BoardPost
{
    public long Id { get; set; }
    public long AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string? AuthorDesignation { get; set; }
    public string? AuthorVertical { get; set; }
    public string Body { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public bool IsPinned { get; set; }
    public string? PinnedAt { get; set; }
    public string? PinnedBy { get; set; }
    public bool IsDeleted { get; set; }
    public string? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public string? DeleteReason { get; set; }
    // joined
    public long AuthorPhotoVersion { get; set; }
    public List<BoardPhoto> Photos { get; set; } = new();
}

public sealed class BoardPhoto
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public string FileName { get; set; } = "";
    public long Width { get; set; }
    public long Height { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public long SortOrder { get; set; }
}

/// <summary>
/// Message board: every signed-in user may post a message with photos; administrators pin up to three posts and
/// remove posts. Removing hides a post (the row stays for the audit trail, a trigger forbids deleting it) and deletes
/// its photo files. Photos are re-encoded like staff photos (upright, metadata removed) into data\uploads\board.
/// </summary>
public sealed class BoardRepository(Db db, BoardOptions o, IntranetOptions io, AuditService audit, ChangeTracker tracker, Fmt fmt)
{
    public BoardOptions Options => o;
    public string Folder => Path.Combine(io.UploadsPath, "board");

    private const string Select = """
        SELECT p.*, COALESCE(u.PhotoVersion, 0) AS AuthorPhotoVersion FROM BoardPosts p LEFT JOIN Users u ON u.Id = p.AuthorUserId
        """;

    public BoardPost? Get(long id)
    {
        var p = db.Single<BoardPost>(Select + " WHERE p.Id = @id", new { id });
        if (p != null) p.Photos = db.Query<BoardPhoto>("SELECT * FROM BoardPhotos WHERE PostId = @id ORDER BY SortOrder", new { id });
        return p;
    }

    public BoardPhoto? Photo(long id) => db.Single<BoardPhoto>("SELECT * FROM BoardPhotos WHERE Id = @id", new { id });

    public List<BoardPost> Pinned() => WithPhotos(db.Query<BoardPost>(Select + " WHERE p.IsDeleted = 0 AND p.IsPinned = 1 ORDER BY p.PinnedAt DESC"));

    /// <summary>Unpinned posts, newest first.</summary>
    public Paged<BoardPost> Page(int page, int pageSize = 20)
    {
        page = Math.Max(1, page);
        return new Paged<BoardPost>
        {
            Items = WithPhotos(db.Query<BoardPost>(Select + " WHERE p.IsDeleted = 0 AND p.IsPinned = 0 ORDER BY p.CreatedAt DESC, p.Id DESC LIMIT @take OFFSET @skip",
                new { take = pageSize, skip = (page - 1) * pageSize })),
            Total = db.Scalar<long>("SELECT COUNT(*) FROM BoardPosts WHERE IsDeleted = 0 AND IsPinned = 0"),
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>For the home page: the pinned posts, then the newest, up to <paramref name="count"/>.</summary>
    public List<BoardPost> Highlights(int count) => WithPhotos(db.Query<BoardPost>(Select +
        " WHERE p.IsDeleted = 0 ORDER BY p.IsPinned DESC, p.CreatedAt DESC LIMIT @count", new { count }));

    private List<BoardPost> WithPhotos(List<BoardPost> posts)
    {
        if (posts.Count == 0) return posts;
        var ids = string.Join(",", posts.Select(p => p.Id));       // numeric ids only
        var photos = db.Query<BoardPhoto>($"SELECT * FROM BoardPhotos WHERE PostId IN ({ids}) ORDER BY PostId, SortOrder");
        foreach (var p in posts) p.Photos = photos.Where(x => x.PostId == p.Id).ToList();
        return posts;
    }

    // posts per person per hour (in memory; resets on restart, which is fine for a flood guard)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Queue<DateTime>> _recent = new();

    private bool AllowPost(long userId)
    {
        var q = _recent.GetOrAdd(userId, _ => new Queue<DateTime>());
        lock (q)
        {
            var now = DateTime.UtcNow;
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromHours(1)) q.Dequeue();
            if (q.Count >= Math.Max(1, o.PostsPerHour)) return false;
            q.Enqueue(now);
            return true;
        }
    }

    public bool CanDelete(BoardPost p, ClaimsPrincipal u) => u.IsAdmin() || p.AuthorUserId == u.UserId();

    /// <summary>Posts by others since the user last opened the board (for the menu badge; at most the last 30 days).</summary>
    public long NewSince(long userId) => db.Scalar<long>("""
        SELECT COUNT(*) FROM BoardPosts
        WHERE IsDeleted = 0 AND AuthorUserId <> @userId
          AND CreatedAt > COALESCE((SELECT BoardSeenAt FROM Users WHERE Id = @userId), @floor) AND CreatedAt > @floor
        """, new { userId, floor = DateTime.UtcNow.AddDays(-30).ToString("o") });

    public void MarkSeen(long userId)
    {
        db.Exec("UPDATE Users SET BoardSeenAt = @now WHERE Id = @userId", new { userId, now = Db.Now() });
        tracker.TouchUser(userId);
    }

    /// <summary>
    /// Creates a post. Photos are checked and written first (outside any transaction); the post and its photo rows are
    /// then recorded in one short transaction. If anything fails, the written photo files are removed again.
    /// </summary>
    public long Create(string? body, IReadOnlyList<IFormFile> uploads, ClaimsPrincipal user)
    {
        body = (body ?? "").Replace("\r\n", "\n").Trim();
        var files = uploads.Where(f => f.Length > 0).ToList();
        if (body.Length == 0 && files.Count == 0) throw new WorkflowException("Write a message or add a photo.");
        if (body.Length > o.MaxChars) throw new WorkflowException($"A message can be at most {o.MaxChars:N0} characters (yours has {body.Length:N0}).");
        if (files.Count > o.MaxPhotosPerPost) throw new WorkflowException($"Add at most {o.MaxPhotosPerPost} photos to one post.");
        if (!AllowPost(user.UserId()))
            throw new WorkflowException($"You can post at most {o.PostsPerHour} messages an hour. Please try again later.");

        var written = new List<(string file, int w, int h, long size, string sha)>();
        try
        {
            foreach (var f in files)
            {
                using var stream = f.OpenReadStream();
                SKBitmap bmp;
                try { bmp = PhotoService.DecodeUpright(stream, f.Length, o.MaxPhotoMB); }
                catch (UploadRejectedException ex) { throw new WorkflowException($"“{Path.GetFileName(f.FileName)}”: {ex.Message}"); }
                using (bmp)
                {
                    var name = $"{DateTime.UtcNow:yyyy}/{Guid.NewGuid():N}";
                    var full = Path.Combine(Folder, name + ".jpg");
                    var (w, h) = PhotoService.WriteFitted(bmp, BoardOptions.FullSide, full);
                    written.Add((name, w, h, new FileInfo(full).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full)))));
                    PhotoService.WriteFitted(bmp, BoardOptions.ThumbSide, Path.Combine(Folder, name + "_t.jpg"));
                }
            }

            var me = ENoteRepository.Person(db.Open(), null, user.UserId(), dispose: true);
            long id;
            using (var c = db.Open())
            using (var tx = c.BeginTransaction(deferred: false))
            {
                id = Db.Insert(c, tx, """
                    INSERT INTO BoardPosts (AuthorUserId, AuthorName, AuthorDesignation, AuthorVertical, Body, CreatedAt)
                    VALUES (@uid, @name, @desig, @vertical, @body, @now)
                    """, new { uid = user.UserId(), name = me?.Name ?? user.DisplayName(), desig = me?.Designation, vertical = me?.Vertical ?? user.Vertical(), body, now = Db.Now() });
                var k = 0;
                foreach (var p in written)
                    Db.Insert(c, tx, "INSERT INTO BoardPhotos (PostId, FileName, Width, Height, SizeBytes, Sha256, SortOrder) VALUES (@id, @file, @w, @h, @size, @sha, @k)",
                        new { id, p.file, p.w, p.h, p.size, p.sha, k = k++ });
                tx.Commit();
            }
            tracker.Touch(ChangeTracker.BoardQueue);
            audit.Log("Board.Posted", "BoardPost", id, $"{Preview(body)}" + (written.Count > 0 ? $" (+{written.Count} photo(s))" : ""));
            return id;
        }
        catch
        {
            foreach (var p in written) DeleteFiles(p.file);
            throw;
        }
    }

    /// <summary>Hides a post (administrators, or the author for their own post) and deletes its photo files.</summary>
    public void Delete(long id, string? reason, ClaimsPrincipal user)
    {
        var p = Get(id) ?? throw new WorkflowException("Post not found.");
        if (!CanDelete(p, user)) throw new WorkflowException("Only an administrator or the author can remove a post.");
        if (p.IsDeleted) throw new WorkflowException("This post has already been removed.");
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 300)];
        var n = db.Exec("UPDATE BoardPosts SET IsDeleted = 1, IsPinned = 0, DeletedAt = @now, DeletedBy = @by, DeleteReason = @reason WHERE Id = @id AND IsDeleted = 0",
            new { id, now = Db.Now(), by = user.DisplayName(), reason });
        if (n == 0) throw new WorkflowException("This post has already been removed.");
        foreach (var ph in p.Photos) DeleteFiles(ph.FileName);
        tracker.Touch(ChangeTracker.BoardQueue);
        audit.Log("Board.Removed", "BoardPost", id, $"Post by {p.AuthorName}: {Preview(p.Body)}" + (p.AuthorUserId == user.UserId() ? " (by the author)" : "") + (reason != null ? "; reason: " + reason : ""));
    }

    /// <summary>Administrators pin up to MaxPinned posts (3 by default) to the top of the board.</summary>
    public void SetPinned(long id, bool pin, ClaimsPrincipal user)
    {
        if (!user.IsAdmin()) throw new WorkflowException("Only an administrator can pin posts.");
        var p = Get(id) ?? throw new WorkflowException("Post not found.");
        if (p.IsDeleted) throw new WorkflowException("This post has been removed.");
        if (p.IsPinned == pin) return;
        using (var c = db.Open())
        using (var tx = c.BeginTransaction(deferred: false))
        {
            if (pin && Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM BoardPosts WHERE IsPinned = 1 AND IsDeleted = 0") >= o.MaxPinned)
                throw new WorkflowException($"At most {o.MaxPinned} posts can be pinned. Unpin one first.");
            Db.Exec(c, tx, "UPDATE BoardPosts SET IsPinned = @pin, PinnedAt = CASE WHEN @pin = 1 THEN @now ELSE NULL END, PinnedBy = CASE WHEN @pin = 1 THEN @by ELSE NULL END WHERE Id = @id",
                new { pin, now = Db.Now(), by = user.DisplayName(), id });
            tx.Commit();
        }
        tracker.Touch(ChangeTracker.BoardQueue);
        audit.Log(pin ? "Board.Pinned" : "Board.Unpinned", "BoardPost", id, $"Post by {p.AuthorName}: {Preview(p.Body)}");
    }

    public string PhotoPath(BoardPhoto p, bool thumb)
    {
        var root = Path.GetFullPath(Folder) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(Folder, p.FileName + (thumb ? "_t.jpg" : ".jpg")));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : "";
    }

    private void DeleteFiles(string name)
    {
        foreach (var suffix in new[] { ".jpg", "_t.jpg" })
        {
            try { var f = Path.Combine(Folder, name + suffix); if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
        }
    }

    private static string Preview(string body) => body.Length <= 80 ? body.Replace('\n', ' ') : body[..79].Replace('\n', ' ') + "…";
}
