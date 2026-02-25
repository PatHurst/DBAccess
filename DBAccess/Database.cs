namespace DBAccess;

/// <summary>
/// Core database access layer, generic over the ADO.NET connection type.
/// Every public method returns <see cref="EitherAsync{DbError, T}"/> so all
/// operations compose via <c>Map</c> / <c>Bind</c> without any
/// <c>try/catch</c> at the call-site.
/// </summary>
/// <typeparam name="TConnection">
/// The concrete <see cref="IDbConnection"/> implementation to use, e.g.
/// <c>SqlConnection</c>, <c>NpgsqlConnection</c>, or <c>SqliteConnection</c>.
/// Must have a public parameterless constructor.
/// </typeparam>
/// <remarks>
/// <para>
/// Two constructors are available:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>Database&lt;TConnection&gt;(string connectionString)</c> — standard
///     path. Creates a fresh connection per operation using the connection string.
///   </item>
///   <item>
///     <c>Database&lt;TConnection&gt;(Func&lt;TConnection&gt; factory)</c> —
///     advanced path. Delegates connection creation entirely to the caller.
///     Useful when the provider requires constructor arguments, certificates,
///     or when a test fixture needs to supply a shared connection.
///   </item>
/// </list>
/// <para>Register as a singleton; the class holds no mutable state.</para>
/// <para>Example:</para>
/// <code>
/// services.AddSingleton(new Database&lt;NpgsqlConnection&gt;(connectionString));
/// </code>
/// </remarks>
public sealed class Database<TConnection>
    where TConnection : IDbConnection, new()
{
    private readonly Func<TConnection> _connectionFactory;

    // When true this instance created the connection and must dispose it.
    // When false the caller owns the connection lifetime; we must never dispose it.
    private readonly bool _ownsConnections;

    /// <summary>
    /// Initialises the database layer using a plain connection string.
    /// A new <typeparamref name="TConnection"/> is created and opened for
    /// each operation by setting <see cref="IDbConnection.ConnectionString"/>
    /// on a default-constructed instance.
    /// </summary>
    /// <param name="connectionString">
    /// The ADO.NET connection string passed to each new connection.
    /// </param>
    public Database(string connectionString)
    {
        _ownsConnections   = true;
        _connectionFactory = () =>
        {
            var conn = new TConnection { ConnectionString = connectionString };
            conn.Open();
            return conn;
        };
    }

    /// <summary>
    /// Initialises the database layer using a custom connection factory.
    /// The factory is responsible for returning a connection in any state;
    /// if the returned connection is not yet open it will be opened before use.
    /// The caller retains ownership — this class will <em>never</em> dispose
    /// connections produced by <paramref name="factory"/>.
    /// </summary>
    /// <param name="factory">
    /// Delegate that produces a <typeparamref name="TConnection"/> on each call.
    /// </param>
    public Database(Func<TConnection> factory)
    {
        _ownsConnections   = false;
        _connectionFactory = factory;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Connection creation / release
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Returns an open connection from the factory.</summary>
    private TConnection OpenConnection()
    {
        var conn = _connectionFactory();
        if (conn.State != ConnectionState.Open)
            conn.Open();
        return conn;
    }

    /// <summary>
    /// Disposes <paramref name="conn"/> only when this instance owns it.
    /// </summary>
    private void ReleaseConnection(TConnection conn)
    {
        if (_ownsConnections) conn.Dispose();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal plumbing
    // ──────────────────────────────────────────────────────────────────────────

    private EitherAsync<DbError, T> Run<T>(
        Func<TConnection, Task<T>> operation,
        string context = "Database operation") =>
        TryAsync(async () =>
        {
            var conn = OpenConnection();
            try
            {
                return await operation(conn).ConfigureAwait(false);
            }
            finally
            {
                ReleaseConnection(conn);
            }
        })
        .ToEither()
        .MapLeft(ex => DbError.FromException(context, ex));

    private EitherAsync<DbError, T> RunInTransaction<T>(
        Func<TConnection, IDbTransaction, Task<T>> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        string context = "Transaction") =>
        TryAsync(async () =>
        {
            var conn = OpenConnection();
            try
            {
                using var tx = conn.BeginTransaction(isolation);
                try
                {
                    var result = await operation(conn, tx).ConfigureAwait(false);
                    tx.Commit();
                    return result;
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            finally
            {
                ReleaseConnection(conn);
            }
        })
        .ToEither()
        .MapLeft(ex => DbError.FromException(context, ex));

    // ──────────────────────────────────────────────────────────────────────────
    // Query — zero or more rows
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a <c>SELECT</c> and projects every row through
    /// <paramref name="map"/>.
    /// </summary>
    /// <typeparam name="T">The projected row type.</typeparam>
    /// <param name="build">
    /// Receives the open connection; return a configured <see cref="IDbCommand"/>
    /// (use <see cref="CommandBuilder"/> for convenience).
    /// </param>
    /// <param name="map">
    /// Pure function that converts an <see cref="IDataRecord"/> to
    /// <typeparamref name="T"/>.
    /// </param>
    /// <returns>
    /// <see cref="EitherAsync{DbError, Seq}"/> containing the projected rows,
    /// or a <see cref="DbError"/> on failure.
    /// </returns>
    public EitherAsync<DbError, Seq<T>> Query<T>(
        Func<TConnection, IDbCommand> build,
        Func<IDataRecord, T> map) =>
        Run(conn =>
        {
            using var cmd = build(conn);
            using var rdr = cmd.ExecuteReader();
            var buf = new List<T>();
            while (rdr.Read()) buf.Add(map(rdr));
            return Task.FromResult(buf.ToSeq());
        }, "Query");

    // ──────────────────────────────────────────────────────────────────────────
    // QueryOne — exactly one row expected
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a <c>SELECT</c> and returns the first row, or a
    /// <see cref="DbError"/> when no rows are returned.
    /// </summary>
    /// <typeparam name="T">The projected row type.</typeparam>
    /// <param name="build">Factory for the <see cref="IDbCommand"/>.</param>
    /// <param name="map">Row projection function.</param>
    /// <returns>
    /// <see cref="Right{T}"/> with the first row, or <see cref="Left{DbError}"/>
    /// when zero rows are found.
    /// </returns>
    public EitherAsync<DbError, T> QueryOne<T>(
        Func<TConnection, IDbCommand> build,
        Func<IDataRecord, T> map) =>
        Run(conn =>
        {
            using var cmd = build(conn);
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read()) return Task.FromResult(map(rdr));
            throw new InvalidOperationException(
                "QueryOne expected at least one row but the query returned none.");
        }, "QueryOne");

    // ──────────────────────────────────────────────────────────────────────────
    // QueryOption — zero or one row
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a <c>SELECT</c> and wraps the first row in
    /// <see cref="Option{T}"/>. Returns <see cref="None"/> — not an error —
    /// when the query returns zero rows.
    /// </summary>
    /// <typeparam name="T">The projected row type.</typeparam>
    /// <param name="build">Factory for the <see cref="IDbCommand"/>.</param>
    /// <param name="map">Row projection function.</param>
    /// <returns>
    /// <see cref="Right{Option}"/> containing <see cref="Some{T}"/> or
    /// <see cref="None"/>, or <see cref="Left{DbError}"/> on failure.
    /// </returns>
    public EitherAsync<DbError, Option<T>> QueryOption<T>(
        Func<TConnection, IDbCommand> build,
        Func<IDataRecord, T> map) =>
        Run(conn =>
        {
            using var cmd = build(conn);
            using var rdr = cmd.ExecuteReader();
            var result = rdr.Read() ? Some(map(rdr)) : Option<T>.None;
            return Task.FromResult(result);
        }, "QueryOption");

    // ──────────────────────────────────────────────────────────────────────────
    // Scalar — single value result
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the command and returns the first column of the first row cast
    /// to <typeparamref name="T"/>. Useful for <c>INSERT … RETURNING id</c>,
    /// <c>COUNT(*)</c>, <c>MAX(…)</c>, etc.
    /// </summary>
    /// <typeparam name="T">The expected scalar type.</typeparam>
    /// <param name="build">Factory for the <see cref="IDbCommand"/>.</param>
    /// <returns>The scalar value, or a <see cref="DbError"/> on failure.</returns>
    public EitherAsync<DbError, T> Scalar<T>(
        Func<TConnection, IDbCommand> build) =>
        Run(conn =>
        {
            using var cmd = build(conn);
            var raw = cmd.ExecuteScalar()
                ?? throw new InvalidOperationException("Scalar query returned null.");
            return Task.FromResult((T)Convert.ChangeType(raw, typeof(T)));
        }, "Scalar");

    // ──────────────────────────────────────────────────────────────────────────
    // Execute — INSERT / UPDATE / DELETE
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a non-query command and returns the number of rows affected.
    /// </summary>
    /// <param name="build">Factory for the <see cref="IDbCommand"/>.</param>
    /// <returns>
    /// <see cref="Right{int}"/> with the affected-row count, or
    /// <see cref="Left{DbError}"/> on failure.
    /// </returns>
    public EitherAsync<DbError, int> Execute(
        Func<TConnection, IDbCommand> build) =>
        Run(conn =>
        {
            using var cmd = build(conn);
            return Task.FromResult(cmd.ExecuteNonQuery());
        }, "Execute");

    // ──────────────────────────────────────────────────────────────────────────
    // Transact — arbitrary work inside a single transaction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs an arbitrary block of ADO.NET work inside a single transaction.
    /// The callback receives the open <typeparamref name="TConnection"/> and
    /// active <see cref="IDbTransaction"/>. Commits on success; rolls back on
    /// any exception.
    /// </summary>
    /// <typeparam name="T">The return type of the transactional block.</typeparam>
    /// <param name="work">
    /// Async function that receives the open connection and transaction.
    /// </param>
    /// <param name="isolation">
    /// Transaction isolation level. Defaults to
    /// <see cref="IsolationLevel.ReadCommitted"/>.
    /// </param>
    /// <returns>
    /// <see cref="Right{T}"/> with the result of <paramref name="work"/>, or
    /// <see cref="Left{DbError}"/> if anything fails.
    /// </returns>
    public EitherAsync<DbError, T> Transact<T>(
        Func<TConnection, IDbTransaction, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted) =>
        RunInTransaction(work, isolation, "Transact");

    /// <summary>
    /// Convenience overload of <see cref="Transact{T}"/> for side-effecting
    /// operations that return <see cref="Unit"/>.
    /// </summary>
    /// <param name="work">Async side-effecting function.</param>
    /// <param name="isolation">Transaction isolation level.</param>
    /// <returns>
    /// <see cref="Right{Unit}"/> on success, <see cref="Left{DbError}"/>
    /// on failure.
    /// </returns>
    public EitherAsync<DbError, Unit> Transact(
        Func<TConnection, IDbTransaction, Task> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted) =>
        RunInTransaction(async (conn, tx) =>
        {
            await work(conn, tx).ConfigureAwait(false);
            return unit;
        }, isolation, "Transact");
}
