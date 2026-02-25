namespace DBAccess.Tests.Live;

/// <summary>
/// xUnit class fixture that owns a single, long-lived SQLite in-process
/// connection for the duration of a test class.
/// </summary>
/// <remarks>
/// <para>
/// SQLite's <c>:memory:</c> databases are tied to the connection that created
/// them — a second connection opens a completely independent database. To avoid
/// "no such table" failures when <see cref="Database{TConnection}"/> opens a
/// fresh connection per operation, this fixture creates one persistent
/// <see cref="SqliteConnection"/> and supplies it to
/// <see cref="Database{TConnection}"/> via the factory-delegate constructor.
/// Every operation therefore runs on the same underlying database.
/// </para>
/// <para>
/// This is the correct pattern for SQLite-backed tests. Against a real server
/// (PostgreSQL, SQL Server) use the normal string constructor and let each
/// operation draw its own pooled connection.
/// </para>
/// </remarks>
public sealed class SqliteFixture : IAsyncLifetime
{
    // One persistent connection — the in-memory database lives as long as this does.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    /// <summary>
    /// A <see cref="Database{SqliteConnection}"/> whose every operation runs on
    /// the single shared in-memory connection.
    /// </summary>
    public Database<SqliteConnection> Db { get; private set; } = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        // Wire Database to always return the same already-open connection.
        // The state-check guard in Database.OpenConnection() is a no-op here.
        Db = new Database<SqliteConnection>(() => _connection);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS products (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                name    TEXT    NOT NULL,
                price   REAL    NOT NULL,
                notes   TEXT    NULL
            );

            CREATE TABLE IF NOT EXISTS audit_log (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                action      TEXT    NOT NULL,
                entity_id   INTEGER NOT NULL,
                occurred_at TEXT    NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ── Seed / cleanup helpers ────────────────────────────────────────────────

    /// <summary>Inserts a product row and returns its generated id.</summary>
    public async Task<int> SeedProductAsync(string name, double price, string? notes = null)
    {
        using var insert = _connection.CreateCommand();
        insert.CommandText = "INSERT INTO products (name, price, notes) VALUES (@name, @price, @notes)";
        insert.Parameters.AddWithValue("@name",  name);
        insert.Parameters.AddWithValue("@price", price);
        insert.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync();

        using var rowid = _connection.CreateCommand();
        rowid.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(await rowid.ExecuteScalarAsync());
    }

    /// <summary>Deletes all rows from products and audit_log.</summary>
    public async Task ClearTablesAsync()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM products; DELETE FROM audit_log;";
        await cmd.ExecuteNonQueryAsync();
    }
}
