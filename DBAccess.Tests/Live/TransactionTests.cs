namespace DBAccess.Tests.Live;

/// <summary>
/// Integration tests for <see cref="Database{TConnection}.Transact{T}"/> and
/// the <c>Unit</c> overload, verifying commit, rollback, and isolation.
/// </summary>
public sealed class TransactionTests(TransactionFixture fixture) : IClassFixture<TransactionFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ClearTablesAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    // ── Commit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Transact_commits_all_commands_on_success()
    {
        var result = await fixture.Db.Transact(async (conn, tx) =>
        {
            using var c1 = CommandBuilder.For(conn, tx)
                .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                .WithParam("@name",  "TxProduct1")
                .WithParam("@price", 1.11)
                .Build();
            c1.ExecuteNonQuery();

            using var c2 = CommandBuilder.For(conn, tx)
                .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                .WithParam("@name",  "TxProduct2")
                .WithParam("@price", 2.22)
                .Build();
            c2.ExecuteNonQuery();

            await Task.CompletedTask;
        });

        result.IsRight.Should().BeTrue();

        var count = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn).WithSql("SELECT COUNT(*) FROM products").Build());
        count.IfRight(n => n.Should().Be(2));
    }

    [Fact]
    public async Task Transact_returns_typed_value_on_success()
    {
        var result = await fixture.Db.Transact(async (conn, tx) =>
        {
            using var insert = CommandBuilder.For(conn, tx)
                .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                .WithParam("@name",  "ReturnedId")
                .WithParam("@price", 5.55)
                .Build();
            insert.ExecuteNonQuery();

            using var rowid = CommandBuilder.For(conn, tx)
                .WithSql("SELECT last_insert_rowid()")
                .Build();
            var id = Convert.ToInt64(rowid.ExecuteScalar());

            await Task.CompletedTask;
            return id;
        });

        result.IsRight.Should().BeTrue();
        result.IfRight(id => id.Should().BeGreaterThan(0));
    }

    // ── Rollback ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Transact_rolls_back_all_commands_when_exception_thrown()
    {
        var result = await fixture.Db.Transact(async (conn, tx) =>
        {
            using var c1 = CommandBuilder.For(conn, tx)
                .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                .WithParam("@name",  "WillBeRolledBack")
                .WithParam("@price", 9.99)
                .Build();
            c1.ExecuteNonQuery();

            await Task.CompletedTask;

            // Simulate a failure mid-transaction.
            throw new InvalidOperationException("Simulated failure");
        });

        result.IsLeft.Should().BeTrue();
        result.IfLeft(err =>
        {
            err.Cause.IsSome.Should().BeTrue();
            err.Cause.IfSome(ex => ex.Should().BeOfType<InvalidOperationException>());
        });

        // Nothing should have been persisted.
        var count = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn).WithSql("SELECT COUNT(*) FROM products").Build());
        count.IfRight(n => n.Should().Be(0));
    }

    [Fact]
    public async Task Transact_rolls_back_when_bad_sql_throws()
    {
        await fixture.SeedProductAsync("SafeRow", 1.00);

        var result = await fixture.Db.Transact(async (conn, tx) =>
        {
            using var bad = CommandBuilder.For(conn, tx)
                .WithSql("DELETE FROM products WHERE id = 1")
                .Build();
            bad.ExecuteNonQuery();

            // This will throw — table does not exist.
            using var broken = CommandBuilder.For(conn, tx)
                .WithSql("INSERT INTO nonexistent_table (x) VALUES (1)")
                .Build();
            broken.ExecuteNonQuery();

            await Task.CompletedTask;
        });

        result.IsLeft.Should().BeTrue();

        // The DELETE above should have been rolled back.
        var count = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn).WithSql("SELECT COUNT(*) FROM products").Build());
        count.IfRight(n => n.Should().Be(1, "rollback must restore the deleted row"));
    }

    // ── Multi-step transactional pipeline ────────────────────────────────────

    [Fact]
    public async Task Transact_with_audit_log_inserts_both_rows_atomically()
    {
        var result = await fixture.Db.Transact(async (conn, tx) =>
        {
            using var product = CommandBuilder.For(conn, tx)
                .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                .WithParam("@name",  "Audited Widget")
                .WithParam("@price", 12.00)
                .Build();
            product.ExecuteNonQuery();

            using var rowid = CommandBuilder.For(conn, tx)
                .WithSql("SELECT last_insert_rowid()")
                .Build();
            var productId = Convert.ToInt64(rowid.ExecuteScalar());

            using var audit = CommandBuilder.For(conn, tx)
                .WithSql("INSERT INTO audit_log (action, entity_id, occurred_at) VALUES (@action, @id, @ts)")
                .WithParam("@action", "create")
                .WithParam("@id",     productId)
                .WithParam("@ts",     DateTimeOffset.UtcNow.ToString("O"))
                .Build();
            audit.ExecuteNonQuery();

            await Task.CompletedTask;
            return productId;
        });

        result.IsRight.Should().BeTrue();

        var productCount = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn).WithSql("SELECT COUNT(*) FROM products").Build());
        productCount.IfRight(n => n.Should().Be(1));

        var auditCount = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn).WithSql("SELECT COUNT(*) FROM audit_log").Build());
        auditCount.IfRight(n => n.Should().Be(1));
    }

    // ── Isolation level overload ──────────────────────────────────────────────

    [Fact]
    public async Task Transact_accepts_custom_isolation_level()
    {
        // SQLite only supports Serializable; this validates the overload is
        // callable and doesn't throw from the isolation-level argument path.
        var result = await fixture.Db.Transact(
            async (conn, tx) =>
            {
                using var cmd = CommandBuilder.For(conn, tx)
                    .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                    .WithParam("@name",  "IsolationTest")
                    .WithParam("@price", 0.01)
                    .Build();
                cmd.ExecuteNonQuery();
                await Task.CompletedTask;
            },
            IsolationLevel.Serializable);

        result.IsRight.Should().BeTrue();
    }
}
