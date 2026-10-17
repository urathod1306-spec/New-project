using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public sealed class UserRepository(Db db)
{
    public AppUser? Get(long id) => db.Single<AppUser>("SELECT * FROM Users WHERE Id = @id", new { id });

    public AppUser? GetByUsername(string username) =>
        db.Single<AppUser>("SELECT * FROM Users WHERE Username = @username", new { username });

    public List<AppUser> All() => db.Query<AppUser>("SELECT * FROM Users ORDER BY IsActive DESC, DisplayName");

    public List<AppUser> ActiveApprovers(long excludeUserId) => db.Query<AppUser>(
        "SELECT * FROM Users WHERE IsActive = 1 AND (',' || Roles || ',') LIKE '%,Approver,%' AND Id <> @excludeUserId ORDER BY DisplayName",
        new { excludeUserId });

    public long Count() => db.Scalar<long>("SELECT COUNT(*) FROM Users");

    public long Create(AppUser u, string actor)
    {
        u.SecurityStamp = PasswordHasher.NewStamp();
        u.CreatedAt = Db.Now();
        u.UpdatedBy = actor;
        u.UpdatedAt = u.CreatedAt;
        u.Roles = Infrastructure.Roles.Join(u.RoleList);
        u.Id = db.Insert("""
            INSERT INTO Users (Username, DisplayName, Email, Vertical, Roles, PasswordHash, MustChangePassword, IsActive, SecurityStamp, CreatedAt, UpdatedBy, UpdatedAt)
            VALUES (@Username, @DisplayName, @Email, @Vertical, @Roles, @PasswordHash, @MustChangePassword, @IsActive, @SecurityStamp, @CreatedAt, @UpdatedBy, @UpdatedAt)
            """, u);
        return u.Id;
    }

    /// <summary>Updates profile and roles. A new security stamp signs the user out of other sessions when roles change.</summary>
    public void Update(AppUser u, string actor, bool rotateStamp)
    {
        u.Roles = Infrastructure.Roles.Join(u.RoleList);
        db.Exec("""
            UPDATE Users SET DisplayName = @DisplayName, Email = @Email, Vertical = @Vertical, Roles = @Roles, IsActive = @IsActive,
                   SecurityStamp = CASE WHEN @rotate = 1 THEN @stamp ELSE SecurityStamp END,
                   UpdatedBy = @actor, UpdatedAt = @now
            WHERE Id = @Id
            """, new { u.DisplayName, u.Email, u.Vertical, u.Roles, u.IsActive, u.Id, rotate = rotateStamp, stamp = PasswordHasher.NewStamp(), actor, now = Db.Now() });
    }

    public void SetPassword(long id, string password, bool mustChange, string actor) => db.Exec("""
        UPDATE Users SET PasswordHash = @hash, MustChangePassword = @mustChange, FailedLogins = 0, LockoutUntil = NULL,
               SecurityStamp = @stamp, UpdatedBy = @actor, UpdatedAt = @now WHERE Id = @id
        """, new { id, hash = PasswordHasher.Hash(password), mustChange, stamp = PasswordHasher.NewStamp(), actor, now = Db.Now() });

    public void Unlock(long id, string actor) => db.Exec(
        "UPDATE Users SET FailedLogins = 0, LockoutUntil = NULL, UpdatedBy = @actor, UpdatedAt = @now WHERE Id = @id",
        new { id, actor, now = Db.Now() });

    public void RecordFailedLogin(AppUser u, int maxFailed, int lockoutMinutes)
    {
        var failed = u.FailedLogins + 1;
        string? until = failed >= maxFailed ? DateTime.UtcNow.AddMinutes(lockoutMinutes).ToString("o") : null;
        db.Exec("UPDATE Users SET FailedLogins = @failed, LockoutUntil = COALESCE(@until, LockoutUntil) WHERE Id = @Id",
            new { failed = until != null ? 0 : failed, until, u.Id });
        u.FailedLogins = failed;
        u.LockoutUntil = until ?? u.LockoutUntil;
    }

    public void RecordSuccessfulLogin(long id) => db.Exec(
        "UPDATE Users SET FailedLogins = 0, LockoutUntil = NULL, LastLoginAt = @now WHERE Id = @id", new { id, now = Db.Now() });

    /// <summary>PhotoVersion is the cache-busting number in photo URLs; 0 means no photo.</summary>
    public void BumpPhotoVersion(long id, bool removed = false) => db.Exec("UPDATE Users SET PhotoVersion = @v WHERE Id = @id",
        new { id, v = removed ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

    public long CountActiveAdmins() =>
        db.Scalar<long>("SELECT COUNT(*) FROM Users WHERE IsActive = 1 AND (',' || Roles || ',') LIKE '%,Administrator,%'");
}
