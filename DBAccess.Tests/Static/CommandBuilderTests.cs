namespace DBAccess.Tests.Static;

/// <summary>
/// Tests for <see cref="CommandBuilder"/> using an SQLite in-memory connection.
/// These tests do not exercise SQL execution — they verify that the builder
/// produces a correctly configured <see cref="IDbCommand"/>.
/// </summary>
public sealed class CommandBuilderTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public CommandBuilderTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void For_returns_builder_without_transaction()
    {
        using var cmd = CommandBuilder.For(_conn)
            .WithSql("SELECT 1")
            .Build();

        cmd.CommandText.Should().Be("SELECT 1");
        cmd.Transaction.Should().BeNull();
    }

    [Fact]
    public void For_with_transaction_sets_transaction_on_command()
    {
        using var tx  = _conn.BeginTransaction();
        using var cmd = CommandBuilder.For(_conn, tx)
            .WithSql("SELECT 1")
            .Build();

        cmd.Transaction.Should().BeSameAs(tx);
    }

    [Fact]
    public void WithSql_sets_command_text()
    {
        using var cmd = CommandBuilder.For(_conn)
            .WithSql("SELECT id FROM users WHERE id = @id")
            .Build();

        cmd.CommandText.Should().Be("SELECT id FROM users WHERE id = @id");
    }

    [Fact]
    public void WithParam_adds_parameter_with_correct_name_and_value()
    {
        using var cmd = CommandBuilder.For(_conn)
            .WithSql("SELECT * FROM t WHERE id = @id")
            .WithParam("@id", 99)
            .Build();

        cmd.Parameters.Count.Should().Be(1);
        var p = (IDataParameter)cmd.Parameters[0]!;
        p.ParameterName.Should().Be("@id");
        p.Value.Should().Be(99);
    }

    [Fact]
    public void WithParam_maps_null_value_to_DBNull()
    {
        using var cmd = CommandBuilder.For(_conn)
            .WithSql("INSERT INTO t (notes) VALUES (@notes)")
            .WithParam("@notes", null)
            .Build();

        var p = (IDataParameter)cmd.Parameters[0]!;
        p.Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void Multiple_WithParam_calls_add_multiple_parameters()
    {
        using var cmd = CommandBuilder.For(_conn)
            .WithSql("SELECT * FROM t WHERE a = @a AND b = @b AND c = @c")
            .WithParam("@a", 1)
            .WithParam("@b", "hello")
            .WithParam("@c", 3.14m)
            .Build();

        cmd.Parameters.Count.Should().Be(3);
    }

    [Fact]
    public void Build_can_be_called_multiple_times_producing_independent_commands()
    {
        var builder = CommandBuilder.For(_conn).WithSql("SELECT 1");

        using var cmd1 = builder.Build();
        using var cmd2 = builder.Build();

        cmd1.Should().NotBeSameAs(cmd2);
        cmd1.CommandText.Should().Be(cmd2.CommandText);
    }
}
