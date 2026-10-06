using System.Reflection;
using Npgsql;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// A database for one test. It's a temporary SQLite file, or a schema of its own on PostgreSQL when
/// TEST_POSTGRES_CONNECTION is set, so the same tests check the behavior of the database that's deployed.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    public OrmLiteConnectionFactory Factory { get; }

    private readonly string? sqlitePath;
    private readonly string? adminConnection;
    private readonly string? schema;

    private TestDatabase(OrmLiteConnectionFactory factory, string? sqlitePath, string? adminConnection, string? schema)
    {
        Factory = factory;
        this.sqlitePath = sqlitePath;
        this.adminConnection = adminConnection;
        this.schema = schema;
    }

    public static TestDatabase Create()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (configured.IsNullOrEmpty())
        {
            // Tests seed rows without their related rows, so foreign keys aren't enforced
            var path = Path.Combine(Path.GetTempPath(), $"next-saas-test-{Guid.NewGuid():N}.db");
            return new TestDatabase(
                new OrmLiteConnectionFactory($"Data Source={path};Cache=Shared;Foreign Keys=False", SqliteDialect.Provider),
                path, null, null);
        }

        var schema = "next_saas_" + Guid.NewGuid().ToString("N");
        using (var admin = new NpgsqlConnection(configured))
        {
            admin.Open();
            using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            create.ExecuteNonQuery();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(configured!) { SearchPath = schema }.ConnectionString;
        return new TestDatabase(new OrmLiteConnectionFactory(connectionString, PostgreSqlDialect.Provider), null, configured, schema);
    }

    /// <summary>
    /// Create the tables and the tables they reference, without enforcing their foreign keys, as tests seed
    /// rows without their related rows.
    /// </summary>
    public void CreateTables(params IEnumerable<Type> tables)
    {
        var all = new List<Type>();
        void Add(Type table)
        {
            if (all.Contains(table)) return;
            // Tables are created after the tables they reference
            foreach (var referenced in table.GetProperties()
                         .Select(x => x.FirstAttribute<ServiceStack.DataAnnotations.ReferencesAttribute>()?.Type)
                         .Where(x => x != null && x != table))
                Add(referenced!);
            all.Add(table);
        }
        foreach (var table in tables)
            Add(table);

        using (var db = Factory.Open())
        {
            foreach (var table in all)
                db.CreateTable(overwrite: false, table);
        }
        DropForeignKeys();
    }

    private void DropForeignKeys()
    {
        if (schema == null) return;
        using var db = Factory.Open();
        var constraints = db.Select<(string Table, string Name)>(
            """
            SELECT c.relname, con.conname
              FROM pg_constraint con
              JOIN pg_class c ON c.oid = con.conrelid
              JOIN pg_namespace n ON n.oid = con.connamespace
             WHERE con.contype = 'f' AND n.nspname = @schema
            """, new { schema });
        foreach (var (table, name) in constraints)
            db.ExecuteSql($"ALTER TABLE \"{schema}\".\"{table}\" DROP CONSTRAINT \"{name}\"");
    }

    public void Dispose()
    {
        if (schema != null)
        {
            NpgsqlConnection.ClearAllPools();
            using var admin = new NpgsqlConnection(adminConnection);
            admin.Open();
            using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            drop.ExecuteNonQuery();
            return;
        }
        File.Delete(sqlitePath!);
        File.Delete(sqlitePath + "-shm");
        File.Delete(sqlitePath + "-wal");
    }
}
