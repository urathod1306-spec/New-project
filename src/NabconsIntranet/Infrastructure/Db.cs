using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Thin, dependency-free data access over SQLite. Every statement is parameterised;
/// rows are mapped to classes by matching column names to property names.
/// </summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(IntranetOptions options)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    public static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    public void Initialize()
    {
        using var c = Open();
        using (var wal = c.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            wal.ExecuteNonQuery();
        }
        Exec(c, null, Schema.Sql);
    }

    // ---- convenience overloads that open their own connection ----
    public List<T> Query<T>(string sql, object? args = null) where T : new()
    {
        using var c = Open();
        return Query<T>(c, null, sql, args);
    }

    public T? Single<T>(string sql, object? args = null) where T : class, new() => Query<T>(sql, args).FirstOrDefault();

    public int Exec(string sql, object? args = null)
    {
        using var c = Open();
        return Exec(c, null, sql, args);
    }

    public long Insert(string sql, object? args = null)
    {
        using var c = Open();
        return Insert(c, null, sql, args);
    }

    public T? Scalar<T>(string sql, object? args = null)
    {
        using var c = Open();
        return Scalar<T>(c, null, sql, args);
    }

    public List<string> Strings(string sql, object? args = null)
    {
        using var c = Open();
        using var cmd = Command(c, null, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        return list;
    }

    // ---- connection/transaction aware versions ----
    public static List<T> Query<T>(SqliteConnection c, SqliteTransaction? tx, string sql, object? args = null) where T : new()
    {
        using var cmd = Command(c, tx, sql, args);
        using var r = cmd.ExecuteReader();
        var props = PropertyMap(typeof(T));
        var cols = new PropertyInfo?[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++)
            props.TryGetValue(r.GetName(i), out cols[i]);
        var list = new List<T>();
        while (r.Read())
        {
            var item = new T();
            for (int i = 0; i < cols.Length; i++)
            {
                var p = cols[i];
                if (p == null || r.IsDBNull(i)) continue;
                p.SetValue(item, Convert(r.GetValue(i), p.PropertyType));
            }
            list.Add(item);
        }
        return list;
    }

    public static int Exec(SqliteConnection c, SqliteTransaction? tx, string sql, object? args = null)
    {
        using var cmd = Command(c, tx, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public static long Insert(SqliteConnection c, SqliteTransaction? tx, string sql, object? args = null)
    {
        using var cmd = Command(c, tx, sql + "; SELECT last_insert_rowid();", args);
        return (long)cmd.ExecuteScalar()!;
    }

    public static T? Scalar<T>(SqliteConnection c, SqliteTransaction? tx, string sql, object? args = null)
    {
        using var cmd = Command(c, tx, sql, args);
        var v = cmd.ExecuteScalar();
        if (v == null || v is DBNull) return default;
        return (T)Convert(v, typeof(T))!;
    }

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql, object? args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        if (args != null)
        {
            foreach (var p in args.GetType().GetProperties())
                cmd.Parameters.AddWithValue("@" + p.Name, ToDb(p.GetValue(args)));
        }
        return cmd;
    }

    private static object ToDb(object? v) => v switch
    {
        null => DBNull.Value,
        bool b => b ? 1 : 0,
        DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => (double)m,
        _ => v,
    };

    private static object? Convert(object v, Type target)
    {
        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (t.IsInstanceOfType(v)) return v;
        if (t == typeof(bool)) return System.Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0;
        if (t == typeof(DateTime))
            return DateTime.Parse((string)v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (t == typeof(string)) return System.Convert.ToString(v, CultureInfo.InvariantCulture);
        return System.Convert.ChangeType(v, t, CultureInfo.InvariantCulture);
    }

    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> Maps = new();

    private static Dictionary<string, PropertyInfo> PropertyMap(Type t) => Maps.GetOrAdd(t, type =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase));
}
