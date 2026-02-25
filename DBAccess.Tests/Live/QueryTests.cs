namespace DBAccess.Tests.Live;

/// <summary>
/// Integration tests for <see cref="Database{TConnection}.Query"/> and
/// <see cref="Database{TConnection}.QueryOption"/> against a live SQLite
/// in-memory database.
/// </summary>
public sealed class QueryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ClearTablesAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    // ── Shared mapper ─────────────────────────────────────────────────────────

    static TestProduct Map(IDataRecord r) => new(
        r.Get<long>("id"),
        r.Get<string>("name"),
        r.Get<double>("price"),
        r.GetOptionString("notes"));

    // ── Query ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_returns_empty_seq_when_table_is_empty()
    {
        var result = await fixture.Db.Query(
            conn => CommandBuilder.For(conn).WithSql("SELECT * FROM products").Build(),
            Map);

        result.IsRight.Should().BeTrue();
        result.IfRight(rows => rows.Should().BeEmpty());
    }

    [Fact]
    public async Task Query_returns_all_seeded_rows()
    {
        await fixture.SeedProductAsync("Widget", 9.99);
        await fixture.SeedProductAsync("Sprocket", 4.50);
        await fixture.SeedProductAsync("Bolt", 0.25);

        var result = await fixture.Db.Query(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products ORDER BY name")
                        .Build(),
            Map);

        result.IsRight.Should().BeTrue();
        result.IfRight(rows =>
        {
            rows.Count.Should().Be(3);
            rows.Map(r => r.Name).Should().ContainInOrder("Bolt", "Sprocket", "Widget");
        });
    }

    [Fact]
    public async Task Query_with_parameter_filters_correctly()
    {
        await fixture.SeedProductAsync("Widget", 9.99);
        await fixture.SeedProductAsync("Sprocket", 4.50);

        var result = await fixture.Db.Query(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE name = @name")
                        .WithParam("@name", "Widget")
                        .Build(),
            Map);

        result.IsRight.Should().BeTrue();
        result.IfRight(rows =>
        {
            rows.Count.Should().Be(1);
            rows.Head.Name.Should().Be("Widget");
        });
    }

    [Fact]
    public async Task Query_maps_nullable_notes_column_to_None()
    {
        await fixture.SeedProductAsync("NullNotesProduct", 1.00, notes: null);

        var result = await fixture.Db.Query(
            conn => CommandBuilder.For(conn).WithSql("SELECT * FROM products").Build(),
            Map);

        result.IfRight(rows => rows.Head.Notes.IsNone.Should().BeTrue());
    }

    [Fact]
    public async Task Query_maps_populated_notes_column_to_Some()
    {
        await fixture.SeedProductAsync("Annotated", 5.00, notes: "limited stock");

        var result = await fixture.Db.Query(
            conn => CommandBuilder.For(conn).WithSql("SELECT * FROM products").Build(),
            Map);

        result.IfRight(rows =>
        {
            rows.Head.Notes.IsSome.Should().BeTrue();
            rows.Head.Notes.IfSome(n => n.Should().Be("limited stock"));
        });
    }

    [Fact]
    public async Task Query_returns_Left_on_bad_sql()
    {
        var result = await fixture.Db.Query(
            conn => CommandBuilder.For(conn).WithSql("SELECT * FROM nonexistent_table").Build(),
            Map);

        result.IsLeft.Should().BeTrue();
        result.IfLeft(err => err.Cause.IsSome.Should().BeTrue());
    }

    // ── QueryOption ───────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryOption_returns_Some_when_row_exists()
    {
        var id = await fixture.SeedProductAsync("Gear", 2.75);

        var result = await fixture.Db.QueryOption(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE id = @id")
                        .WithParam("@id", id)
                        .Build(),
            Map);

        result.IsRight.Should().BeTrue();
        result.IfRight(opt =>
        {
            opt.IsSome.Should().BeTrue();
            opt.IfSome(p => p.Name.Should().Be("Gear"));
        });
    }

    [Fact]
    public async Task QueryOption_returns_None_when_no_row_matches()
    {
        var result = await fixture.Db.QueryOption(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE id = @id")
                        .WithParam("@id", 999999)
                        .Build(),
            Map);

        result.IsRight.Should().BeTrue();
        result.IfRight(opt => opt.IsNone.Should().BeTrue());
    }

    [Fact]
    public async Task QueryOption_None_is_not_an_error()
    {
        var result = await fixture.Db.QueryOption(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM products WHERE id = @id")
                        .WithParam("@id", -1)
                        .Build(),
            Map);

        result.IsRight.Should().BeTrue("a missing row is not a database error");
        result.IsLeft.Should().BeFalse();
    }

    private sealed record TestProduct(long Id, string Name, double Price, Option<string> Notes);
}
