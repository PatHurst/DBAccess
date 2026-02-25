namespace DBAccess;

/// <summary>
/// Fluent builder for <see cref="IDbCommand"/> instances. Keeps raw ADO.NET
/// ceremony out of repository code while remaining provider-agnostic.
/// </summary>
/// <example>
/// <code>
/// using var cmd = CommandBuilder.For(conn)
///     .WithSql("SELECT * FROM users WHERE id = @id")
///     .WithParam("@id", userId)
///     .Build();
/// </code>
/// </example>
public sealed class CommandBuilder
{
    private readonly IDbConnection _conn;
    private readonly IDbTransaction? _tx;
    private string _sql = string.Empty;
    private readonly List<(string Name, object? Value)> _params = [];

    private CommandBuilder(IDbConnection conn, IDbTransaction? tx)
    {
        _conn = conn;
        _tx   = tx;
    }

    /// <summary>Creates a <see cref="CommandBuilder"/> bound to an open connection.</summary>
    /// <param name="conn">An open <see cref="IDbConnection"/>.</param>
    public static CommandBuilder For(IDbConnection conn) => new(conn, null);

    /// <summary>Creates a <see cref="CommandBuilder"/> bound to an open connection and transaction.</summary>
    /// <param name="conn">An open <see cref="IDbConnection"/>.</param>
    /// <param name="tx">An active <see cref="IDbTransaction"/>.</param>
    public static CommandBuilder For(IDbConnection conn, IDbTransaction tx) => new(conn, tx);

    /// <summary>Sets the SQL command text.</summary>
    /// <param name="sql">The parameterised SQL string.</param>
    public CommandBuilder WithSql(string sql)
    {
        _sql = sql;
        return this;
    }

    /// <summary>Adds a named parameter with a value.</summary>
    /// <param name="name">Parameter name, e.g. <c>@id</c>.</param>
    /// <param name="value">Parameter value. <see langword="null"/> maps to <see cref="DBNull.Value"/>.</param>
    public CommandBuilder WithParam(string name, object? value)
    {
        _params.Add((name, value));
        return this;
    }

    /// <summary>
    /// Builds and returns a ready-to-execute <see cref="IDbCommand"/>.
    /// The caller is responsible for disposing it.
    /// </summary>
    public IDbCommand Build()
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = _sql;

        if (_tx is not null)
            cmd.Transaction = _tx;

        foreach (var (name, value) in _params)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value         = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        return cmd;
    }
}
