namespace DBAccess.Tests.Live;

/// <summary>
/// Integration tests for <see cref="Database{TConnection}.QueryOne"/>,
/// <see cref="Database{TConnection}.Scalar"/>, and
/// <see cref="Database{TConnection}.Execute"/>.
/// </summary>
public sealed class MutationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ClearTablesAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    static TestProduct Map(IDataRecord r) => new(
        r.Get<long>("id"),
        r.Get<string>("name"),
        r.Get<double>("price"));

    // ── QueryOne ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryOne_returns_Right_when_exactly_one_row_found()
    {
        var id = await fixture.SeedProductAsync("Cog", 1.10);

        var result = await fixture.Db.QueryOne(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE id = @id")
                        .WithParam("@id", id)
                        .Build(),
            Map);

        result.IsRight.Should().BeTrue();
        result.IfRight(p =>
        {
            p.Id.Should().Be(id);
            p.Name.Should().Be("Cog");
        });
    }

    [Fact]
    public async Task QueryOne_returns_Left_when_no_rows_match()
    {
        var result = await fixture.Db.QueryOne(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE id = @id")
                        .WithParam("@id", 888888)
                        .Build(),
            Map);

        result.IsLeft.Should().BeTrue();
        result.IfLeft(err => err.Message.Should().Contain("QueryOne"));
    }

    [Fact]
    public async Task QueryOne_returns_first_row_when_multiple_exist()
    {
        await fixture.SeedProductAsync("Alpha", 1.00);
        await fixture.SeedProductAsync("Beta",  2.00);

        // No WHERE — both rows match; QueryOne returns the first.
        var result = await fixture.Db.QueryOne(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products ORDER BY name")
                        .Build(),
            Map);

        result.IsRight.Should().BeTrue();
        result.IfRight(p => p.Name.Should().Be("Alpha"));
    }

    // ── Scalar ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scalar_returns_inserted_id()
    {
        var result = await fixture.Db.Execute(
            conn => CommandBuilder.For(conn)
                        .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                        .WithParam("@name",  "NewPart")
                        .WithParam("@price", 7.77)
                        .Build());

        result.IsRight.Should().BeTrue();

        var id = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT last_insert_rowid()")
                        .Build());

        id.IsRight.Should().BeTrue();
        id.IfRight(v => v.Should().BeGreaterThan(0));
    }

    [Fact]
    public async Task Scalar_returns_count_of_rows()
    {
        await fixture.SeedProductAsync("A", 1.0);
        await fixture.SeedProductAsync("B", 2.0);

        var result = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT COUNT(*) FROM products")
                        .Build());

        result.IsRight.Should().BeTrue();
        result.IfRight(count => count.Should().Be(2));
    }

    [Fact]
    public async Task Scalar_returns_Left_when_query_returns_null()
    {
        // MAX on an empty table returns NULL in SQLite.
        var result = await fixture.Db.Scalar<long>(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT MAX(id) FROM products")
                        .Build());

        result.IsLeft.Should().BeTrue();
        result.IfLeft(err => err.Cause.IsSome.Should().BeTrue());
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_inserts_row_and_returns_rows_affected()
    {
        var result = await fixture.Db.Execute(
            conn => CommandBuilder.For(conn)
                        .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                        .WithParam("@name",  "Inserted")
                        .WithParam("@price", 3.33)
                        .Build());

        result.IsRight.Should().BeTrue();
        result.IfRight(rows => rows.Should().Be(1));
    }

    [Fact]
    public async Task Execute_updates_row_and_returns_rows_affected()
    {
        var id = await fixture.SeedProductAsync("Old Name", 1.00);

        var result = await fixture.Db.Execute(
            conn => CommandBuilder.For(conn)
                        .WithSql("UPDATE products SET name = @name WHERE id = @id")
                        .WithParam("@name", "New Name")
                        .WithParam("@id",   id)
                        .Build());

        result.IsRight.Should().BeTrue();
        result.IfRight(rows => rows.Should().Be(1));

        // Verify via a follow-up query.
        var row = await fixture.Db.QueryOne(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE id = @id")
                        .WithParam("@id", id)
                        .Build(),
            Map);
        row.IfRight(p => p.Name.Should().Be("New Name"));
    }

    [Fact]
    public async Task Execute_deletes_row_and_returns_rows_affected()
    {
        var id = await fixture.SeedProductAsync("ToDelete", 9.00);

        var result = await fixture.Db.Execute(
            conn => CommandBuilder.For(conn)
                        .WithSql("DELETE FROM products WHERE id = @id")
                        .WithParam("@id", id)
                        .Build());

        result.IsRight.Should().BeTrue();
        result.IfRight(rows => rows.Should().Be(1));

        // Confirm it's gone.
        var check = await fixture.Db.QueryOption(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE id = @id")
                        .WithParam("@id", id)
                        .Build(),
            Map);
        check.IfRight(opt => opt.IsNone.Should().BeTrue());
    }

    [Fact]
    public async Task Execute_returns_zero_when_no_rows_match()
    {
        var result = await fixture.Db.Execute(
            conn => CommandBuilder.For(conn)
                        .WithSql("DELETE FROM products WHERE id = @id")
                        .WithParam("@id", 999999)
                        .Build());

        result.IsRight.Should().BeTrue();
        result.IfRight(rows => rows.Should().Be(0));
    }

    [Fact]
    public async Task FailOnZeroRows_after_Execute_returns_Left_when_nothing_deleted()
    {
        var result = await fixture.Db
            .Execute(conn => CommandBuilder.For(conn)
                                 .WithSql("DELETE FROM products WHERE id = @id")
                                 .WithParam("@id", 999999)
                                 .Build())
            .FailOnZeroRows(DbError.FromMessage("Product not found"));

        result.IsLeft.Should().BeTrue();
        result.IfLeft(err => err.Message.Should().Be("Product not found"));
    }

    [Fact]
    public async Task FailOnZeroRows_after_Execute_returns_Right_Unit_when_row_deleted()
    {
        var id = await fixture.SeedProductAsync("Ephemeral", 0.01);

        var result = await fixture.Db
            .Execute(conn => CommandBuilder.For(conn)
                                 .WithSql("DELETE FROM products WHERE id = @id")
                                 .WithParam("@id", id)
                                 .Build())
            .FailOnZeroRows(DbError.FromMessage("Product not found"));

        result.IsRight.Should().BeTrue();
    }

    private sealed record TestProduct(long Id, string Name, double Price);
}
