using System.Text;
using Microsoft.Data.Sqlite;

namespace NabconsIntranet.Tests;

/// <summary>
/// Seeds the brief's benchmark volume straight into a database (schema 5): 500 users, 20,000 e-Notes,
/// 200,000 movements, 40,000 comments, 300,000 notifications. Used by PerformanceTests and the load test.
/// Bench users are "bench0" … "bench499", password Nabcons@2026.
/// </summary>
public static class BenchSeed
{
    public const string Password = "Nabcons@2026";

    private static readonly string[] Verticals = { "BDU", "HR & Administration", "Finance & Accounts", "IT & Digital", "Rural Infrastructure" };
    private static readonly string[] Words = ("watershed evaluation district livelihood cooperative procurement vehicle survey training workshop budget sanction " +
        "approval consultant mapping report field visit quotation contract renewal office equipment project review study rural infrastructure " +
        "finance proposal committee meeting tour deliverables timeline").Split(' ');

    public static List<long> Users(SqliteConnection c, int total, string passwordHash)
    {
        using (var tx = c.BeginTransaction())
        {
            var existing = Scalar(c, tx, "SELECT COUNT(*) FROM Users WHERE Username LIKE 'bench%'");
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Users (Username, DisplayName, Vertical, Roles, PasswordHash, MustChangePassword, IsActive, SecurityStamp, CreatedAt, OpenDeskAfterSignIn)
                VALUES ($u, $n, $v, 'Staff', $h, 0, 1, 'bench', $now, 1)
                """;
            var pu = cmd.Parameters.Add("$u", SqliteType.Text); var pn = cmd.Parameters.Add("$n", SqliteType.Text); var pv = cmd.Parameters.Add("$v", SqliteType.Text);
            cmd.Parameters.AddWithValue("$h", passwordHash); cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            for (var i = (int)existing; i < total; i++) { pu.Value = "bench" + i; pn.Value = "Bench User " + i; pv.Value = Verticals[i % Verticals.Length]; cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
        var ids = new List<long>();
        using var q = c.CreateCommand(); q.CommandText = "SELECT Id FROM Users WHERE Username LIKE 'bench%' ORDER BY Id";
        using var r = q.ExecuteReader(); while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    private static long Scalar(SqliteConnection c, SqliteTransaction? tx, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string Text(Random rnd, int words)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < words; i++) { sb.Append(Words[rnd.Next(Words.Length)]).Append(i % 14 == 13 ? ".\n\n" : " "); }
        return sb.ToString().Trim();
    }

    public static void Notes(SqliteConnection c, List<long> users, int notes = 20_000, int movementsPerNote = 10, int commentsPerNote = 2)
    {
        var rnd = new Random(11);
        var now = DateTime.UtcNow;
        var fy = $"{(now.Month >= 4 ? now.Year : now.Year - 1)}-{((now.Month >= 4 ? now.Year : now.Year - 1) + 1) % 100:D2}";
        using var tx = c.BeginTransaction();
        SqliteCommand Cmd(string sql) { var k = c.CreateCommand(); k.Transaction = tx; k.CommandText = sql; return k; }
        var start = Scalar(c, tx, "SELECT COALESCE(MAX(Serial), 0) FROM ENotes WHERE NumberCode = 'BENCH'");
        using var note = Cmd("""
            INSERT INTO ENotes (NoteNumber, NumberCode, FinancialYear, Serial, Subject, Category, InitiatorUserId, InitiatorName, InitiatorVertical,
                CurrentHolderUserId, CurrentHolderName, PreviousHolderUserId, HeldSince, State, Outcome, CurrentBodyVersion, LastSeq, IsConfidential,
                DeclarationAccepted, CreatedAt, SubmittedAt, DecidedAt, ClosedAt, UpdatedAt, RowVersion)
            VALUES ($num, 'BENCH', $fy, $serial, $subject, 'Procurement', $init, $initName, $vert, $holder, $holderName, $prev, $held, $state, $outcome, 1, $seq, $conf,
                1, $created, $created, $decided, $closed, $updated, 1)
            RETURNING Id
            """);
        string[] names = { "$num", "$fy", "$serial", "$subject", "$init", "$initName", "$vert", "$holder", "$holderName", "$prev", "$held", "$state", "$outcome", "$seq", "$conf", "$created", "$decided", "$closed", "$updated" };
        foreach (var n in names) note.Parameters.Add(new SqliteParameter(n, null));
        using var body = Cmd("INSERT INTO ENoteBodyVersions (NoteId, Version, BodyText, Sha256, CreatedByUserId, CreatedBy, CreatedAt) VALUES ($id, 1, $t, 'BENCH', $u, 'Bench', $at)");
        using var move = Cmd("""
            INSERT INTO ENoteMovements (NoteId, Seq, Action, FromUserId, FromName, FromDesignation, ToUserId, ToName, ToDesignation, FromState, ToState, Remarks, Timestamp)
            VALUES ($id, $seq, $a, $f, $fn, 'Officer', $t, $tn, 'Officer', 'InMovement', 'InMovement', $r, $at)
            """);
        using var comment = Cmd("INSERT INTO ENoteComments (NoteId, Seq, AuthorUserId, AuthorName, AuthorDesignation, Text, Timestamp) VALUES ($id, $seq, $u, $n, 'Officer', $t, $at)");
        using var part = Cmd("INSERT OR IGNORE INTO ENoteParticipants (NoteId, UserId, FirstInvolvedAt) VALUES ($id, $u, $at)");
        using var fts = Cmd("INSERT INTO ENoteSearch (rowid, NoteNumber, Subject, Category, Body) VALUES ($id, $num, $s, 'Procurement', $b)");
        foreach (var k in new[] { body, move, comment, part, fts })
            foreach (var p in k.CommandText.Split(new[] { ' ', ',', '(', ')', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.StartsWith('$')).Distinct())
                k.Parameters.Add(new SqliteParameter(p, null));
        void Set(SqliteCommand k, string n, object? v) => k.Parameters[n].Value = v ?? DBNull.Value;

        for (var i = 0; i < notes; i++)
        {
            var init = users[rnd.Next(users.Count)];
            var chain = new List<long> { init };
            while (chain.Count < movementsPerNote + 1) { var u = users[rnd.Next(users.Count)]; if (u != chain[^1] && u != init) chain.Add(u); }
            var roll = rnd.Next(100);
            var state = roll < 30 ? "InMovement" : roll < 55 ? "Approved" : roll < 80 ? "Closed" : roll < 90 ? "Rejected" : "ReturnedToInitiator";
            var outcome = state is "Approved" or "Closed" ? "Approved" : state == "Rejected" ? "Rejected" : null;
            var created = now.AddMinutes(-rnd.Next(60, 60 * 24 * 365));
            var holder = state == "InMovement" ? chain[^1] : state == "Closed" ? (long?)null : init;
            var at = created.AddHours(movementsPerNote);
            var subject = "Bench note " + i + " — " + Text(rnd, 6);
            var num = $"NCS/EN/BENCH/{fy}/{start + i + 1:D5}";
            Set(note, "$num", num); Set(note, "$fy", fy); Set(note, "$serial", start + i + 1); Set(note, "$subject", subject);
            Set(note, "$init", init); Set(note, "$initName", "Bench User"); Set(note, "$vert", Verticals[rnd.Next(Verticals.Length)]);
            Set(note, "$holder", holder); Set(note, "$holderName", holder != null ? "Bench User" : null); Set(note, "$prev", chain[^2]);
            Set(note, "$held", at.AddDays(-rnd.Next(0, 20)).ToString("o")); Set(note, "$state", state); Set(note, "$outcome", outcome);
            Set(note, "$seq", movementsPerNote + commentsPerNote + 1); Set(note, "$conf", rnd.Next(20) == 0 ? 1 : 0);
            Set(note, "$created", created.ToString("o")); Set(note, "$decided", outcome != null ? at.ToString("o") : null);
            Set(note, "$closed", state == "Closed" ? at.ToString("o") : null); Set(note, "$updated", at.ToString("o"));
            var id = Convert.ToInt64(note.ExecuteScalar());
            var text = Text(rnd, 220);
            Set(body, "$id", id); Set(body, "$t", text); Set(body, "$u", init); Set(body, "$at", created.ToString("o")); body.ExecuteNonQuery();
            Set(fts, "$id", id); Set(fts, "$num", num); Set(fts, "$s", subject); Set(fts, "$b", text); fts.ExecuteNonQuery();
            for (var m = 0; m < movementsPerNote; m++)
            {
                Set(move, "$id", id); Set(move, "$seq", m + 1); Set(move, "$a", m == 0 ? "Submitted" : "MarkedTo"); Set(move, "$f", chain[m]); Set(move, "$fn", "Bench User");
                Set(move, "$t", chain[m + 1]); Set(move, "$tn", "Bench User"); Set(move, "$r", "Please see. " + Text(rnd, 8)); Set(move, "$at", created.AddHours(m).ToString("o"));
                move.ExecuteNonQuery();
            }
            for (var k = 0; k < commentsPerNote; k++)
            {
                Set(comment, "$id", id); Set(comment, "$seq", movementsPerNote + k + 1); Set(comment, "$u", chain[1 + k % movementsPerNote]); Set(comment, "$n", "Bench User");
                Set(comment, "$t", Text(rnd, 30)); Set(comment, "$at", at.ToString("o")); comment.ExecuteNonQuery();
            }
            foreach (var u in chain) { Set(part, "$id", id); Set(part, "$u", u); Set(part, "$at", created.ToString("o")); part.ExecuteNonQuery(); }
        }
        tx.Commit();
    }

    public static void Notifications(SqliteConnection c, List<long> users, int total = 300_000)
    {
        var rnd = new Random(7);
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Notifications (UserId, Type, Title, Link, ActorName, CreatedAt, ReadAt) VALUES ($u, 'ENote.Received', $t, '/ENotes', 'Bench', $at, $read)";
        var pu = cmd.Parameters.Add("$u", SqliteType.Integer); var pt = cmd.Parameters.Add("$t", SqliteType.Text);
        var pa = cmd.Parameters.Add("$at", SqliteType.Text); var pr = cmd.Parameters.Add("$read", SqliteType.Text);
        var now = DateTime.UtcNow;
        for (var i = 0; i < total; i++)
        {
            var at = now.AddMinutes(-rnd.Next(0, 60 * 24 * 300));
            pu.Value = users[rnd.Next(users.Count)]; pt.Value = "Bench notification " + i; pa.Value = at.ToString("o");
            pr.Value = rnd.Next(10) < 8 ? at.AddHours(2).ToString("o") : DBNull.Value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static void All(SqliteConnection c, string passwordHash)
    {
        var users = Users(c, 500, passwordHash);
        Notes(c, users);
        Notifications(c, users);
    }
}
