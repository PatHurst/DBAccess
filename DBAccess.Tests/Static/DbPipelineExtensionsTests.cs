namespace DBAccess.Tests.Static;

public sealed class DbPipelineExtensionsTests
{
    // ── FailOnZeroRows ────────────────────────────────────────────────────────

    [Fact]
    public async Task FailOnZeroRows_returns_Right_Unit_when_rows_affected_is_positive()
    {
        var source = RightAsync<DbError, int>(3);
        var error  = DbError.FromMessage("not found");

        var result = await source.FailOnZeroRows(error);

        result.IsRight.Should().BeTrue();
    }

    [Fact]
    public async Task FailOnZeroRows_returns_Left_when_rows_affected_is_zero()
    {
        var source = RightAsync<DbError, int>(0);
        var error  = DbError.FromMessage("row not found");

        var result = await source.FailOnZeroRows(error);

        result.IsLeft.Should().BeTrue();
        result.IfLeft(e => e.Message.Should().Be("row not found"));
    }

    [Fact]
    public async Task FailOnZeroRows_propagates_existing_Left_unchanged()
    {
        var original = DbError.FromMessage("query failed");
        var source   = LeftAsync<DbError, int>(original);

        var result = await source.FailOnZeroRows(DbError.FromMessage("different error"));

        result.IsLeft.Should().BeTrue();
        result.IfLeft(e => e.Should().Be(original));
    }

    // ── FailOnNone ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FailOnNone_returns_Right_T_when_option_is_Some()
    {
        var source = RightAsync<DbError, Option<string>>(Some("hello"));
        var error  = DbError.FromMessage("not found");

        var result = await source.FailOnNone(error);

        result.IsRight.Should().BeTrue();
        result.IfRight(v => v.Should().Be("hello"));
    }

    [Fact]
    public async Task FailOnNone_returns_Left_when_option_is_None()
    {
        var source = RightAsync<DbError, Option<string>>(Option<string>.None);
        var error  = DbError.FromMessage("record missing");

        var result = await source.FailOnNone(error);

        result.IsLeft.Should().BeTrue();
        result.IfLeft(e => e.Message.Should().Be("record missing"));
    }

    [Fact]
    public async Task FailOnNone_propagates_existing_Left_unchanged()
    {
        var original = DbError.FromMessage("connection failed");
        var source   = LeftAsync<DbError, Option<int>>(original);

        var result = await source.FailOnNone(DbError.FromMessage("other error"));

        result.IsLeft.Should().BeTrue();
        result.IfLeft(e => e.Should().Be(original));
    }

    // ── MapErrorToString ──────────────────────────────────────────────────────

    [Fact]
    public async Task MapErrorToString_maps_Left_DbError_to_string()
    {
        var err    = DbError.FromMessage("oops");
        var source = LeftAsync<DbError, int>(err);

        var result = await source.MapErrorToString();

        result.IsLeft.Should().BeTrue();
        result.IfLeft(s => s.Should().Contain("oops"));
    }

    [Fact]
    public async Task MapErrorToString_preserves_Right_value()
    {
        var source = RightAsync<DbError, int>(42);

        var result = await source.MapErrorToString();

        result.IsRight.Should().BeTrue();
        result.IfRight(v => v.Should().Be(42));
    }

    // ── Pipeline composition ──────────────────────────────────────────────────

    [Fact]
    public async Task Pipeline_Map_Bind_FailOnNone_composes_correctly()
    {
        // Simulates: db returns Option<string>, we require it, then upper-case it.
        var pipeline =
            RightAsync<DbError, Option<string>>(Some("alice"))
                .FailOnNone(DbError.FromMessage("user not found"))
                .Map(name => name.ToUpperInvariant());

        var result = await pipeline;

        result.IsRight.Should().BeTrue();
        result.IfRight(v => v.Should().Be("ALICE"));
    }

    [Fact]
    public async Task Pipeline_short_circuits_to_Left_on_first_failure()
    {
        var callCount = 0;

        var pipeline =
            LeftAsync<DbError, int>(DbError.FromMessage("step 1 failed"))
                .Map(v => { callCount++; return v * 2; })
                .Bind(v => { callCount++; return RightAsync<DbError, int>(v); });

        var result = await pipeline;

        result.IsLeft.Should().BeTrue();
        callCount.Should().Be(0, "downstream steps must not execute after a Left");
    }
}
