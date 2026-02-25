namespace DBAccess.Tests.Live;

/// <summary>
/// A separate fixture for <see cref="TransactionTests"/> that creates a fresh
/// in-memory database and connection for each test class instantiation.
/// </summary>
/// <remarks>
/// Transaction tests call <c>Transact</c> which opens (and commits/rolls back)
/// a transaction on the connection. Because all tests share one connection via
/// <see cref="SqliteFixture"/>, concurrent or sequential transactions on that
/// connection can conflict. This fixture gives each test class its own
/// independent connection to avoid that entirely.
/// </remarks>
public sealed class TransactionFixture : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    /// <summary>Database wired to the isolated in-memory connection.</summary>
    public Database<SqliteConnection> Db { get; private set; } = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
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

    /// <summary>Inserts a product row and returns its generated id.</summary>
    public async Task<int> SeedProductAsync(string name, double price)
    {
        using var insert = _connection.CreateCommand();
        insert.CommandText = "INSERT INTO products (name, price) VALUES (@name, @price)";
        insert.Parameters.AddWithValue("@name",  name);
        insert.Parameters.AddWithValue("@price", price);
        await insert.ExecuteNonQueryAsync();

        using var rowid = _connection.CreateCommand();
        rowid.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(await rowid.ExecuteScalarAsync());
    }

    /// <summary>Deletes all rows from both tables.</summary>
    public async Task ClearTablesAsync()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM products; DELETE FROM audit_log;";
        await cmd.ExecuteNonQueryAsync();
    }
}
