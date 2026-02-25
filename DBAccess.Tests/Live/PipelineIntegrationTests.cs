namespace DBAccess.Tests.Live;

/// <summary>
/// Integration tests that exercise the pipeline extension helpers
/// (<see cref="DbPipelineExtensions"/>) wired to real database calls,
/// verifying end-to-end composition of query → option → either chains.
/// </summary>
public sealed class PipelineIntegrationTests(SqliteFixture fixture)
    : IClassFixture<SqliteFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ClearTablesAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    static TestProduct Map(IDataRecord r) => new(
        r.Get<long>("id"),
        r.Get<string>("name"),
        r.Get<double>("price"));

    // ── FailOnNone wired to real QueryOption ──────────────────────────────────

    [Fact]
    public async Task FailOnNone_resolves_to_Right_when_row_exists()
    {
        var id = await fixture.SeedProductAsync("Present", 3.00);

        var result = await fixture.Db
            .QueryOption(
                conn => CommandBuilder.For(conn)
                            .WithSql("SELECT * FROM products WHERE id = @id")
                            .WithParam("@id", id)
                            .Build(),
                Map)
            .FailOnNone(DbError.FromMessage($"Product {id} not found"));

        result.IsRight.Should().BeTrue();
        result.IfRight(p => p.Id.Should().Be(id));
    }

    [Fact]
    public async Task FailOnNone_resolves_to_Left_when_row_absent()
    {
        var result = await fixture.Db
            .QueryOption(
                conn => CommandBuilder.For(conn)
                            .WithSql("SELECT * FROM products WHERE id = @id")
                            .WithParam("@id", 777777)
                            .Build(),
                Map)
            .FailOnNone(DbError.FromMessage("Product 777777 not found"));

        result.IsLeft.Should().BeTrue();
        result.IfLeft(err => err.Message.Should().Contain("777777"));
    }

    // ── Sequential Bind chain ─────────────────────────────────────────────────

    [Fact]
    public async Task Bind_chain_insert_then_fetch_returns_persisted_record()
    {
        // Insert → get generated id → fetch full record via QueryOption → unwrap.
        var result = await fixture.Db
            .Execute(
                conn => CommandBuilder.For(conn)
                            .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
                            .WithParam("@name",  "Chained")
                            .WithParam("@price", 6.60)
                            .Build())
            .Bind(_ => fixture.Db.Scalar<long>(
                conn => CommandBuilder.For(conn)
                            .WithSql("SELECT last_insert_rowid()")
                            .Build()))
            .Bind(id => fixture.Db
                .QueryOption(
                    conn => CommandBuilder.For(conn)
                                .WithSql("SELECT * FROM products WHERE id = @id")
                                .WithParam("@id", id)
                                .Build(),
                    Map)
                .FailOnNone(DbError.FromMessage("Inserted row not found")));

        result.IsRight.Should().BeTrue();
        result.IfRight(p =>
        {
            p.Name.Should().Be("Chained");
            p.Price.Should().BeApproximately(6.60, 0.001);
        });
    }

    [Fact]
    public async Task Bind_chain_short_circuits_when_first_operation_fails()
    {
        var secondCalled = false;

        var result = await fixture.Db
            .Scalar<long>(
                conn => CommandBuilder.For(conn)
                            // Bad SQL — will return Left.
                            .WithSql("SELECT MAX(id) FROM products") // returns NULL on empty table → Left
                            .Build())
            .Bind(id =>
            {
                secondCalled = true;
                return fixture.Db.QueryOption(
                    conn => CommandBuilder.For(conn)
                                .WithSql("SELECT * FROM products WHERE id = @id")
                                .WithParam("@id", id)
                                .Build(),
                    Map);
            });

        result.IsLeft.Should().BeTrue();
        secondCalled.Should().BeFalse("Bind must not invoke the continuation after a Left");
    }

    // ── Map transformation after live query ───────────────────────────────────

    [Fact]
    public async Task Map_transforms_query_result_without_extra_db_round_trip()
    {
        await fixture.SeedProductAsync("Mapped", 10.00);

        var result = await fixture.Db
            .Query(
                conn => CommandBuilder.For(conn).WithSql("SELECT * FROM products").Build(),
                Map)
            .Map(rows => rows.Map(p => p.Name.ToUpperInvariant()).ToList());

        result.IsRight.Should().BeTrue();
        result.IfRight(names => names.Should().Contain("MAPPED"));
    }

    // ── MapErrorToString at the boundary ─────────────────────────────────────

    [Fact]
    public async Task MapErrorToString_converts_Left_DbError_to_string_at_boundary()
    {
        var result = await fixture.Db
            .QueryOption(
                conn => CommandBuilder.For(conn)
                            .WithSql("SELECT * FROM products WHERE id = @id")
                            .WithParam("@id", -1)
                            .Build(),
                Map)
            .FailOnNone(DbError.FromMessage("not found in live test"))
            .MapErrorToString();

        result.IsLeft.Should().BeTrue();
        result.IfLeft(s =>
        {
            s.Should().BeOfType<string>();
            s.Should().Contain("not found in live test");
        });
    }

    private sealed record TestProduct(long Id, string Name, double Price);
}
