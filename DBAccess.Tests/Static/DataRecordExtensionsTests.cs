using DBAccess.Tests.Fakes;

namespace DBAccess.Tests.Static;

public sealed class DataRecordExtensionsTests
{
    // ── FakeDataRecord plumbing ───────────────────────────────────────────────

    [Fact]
    public void FakeDataRecord_returns_correct_field_count()
    {
        var record = new FakeDataRecord(new() { ["a"] = 1, ["b"] = 2 });
        record.FieldCount.Should().Be(2);
    }

    [Fact]
    public void FakeDataRecord_GetOrdinal_is_case_insensitive()
    {
        var record = new FakeDataRecord(new() { ["MyCol"] = "val" });
        record.GetOrdinal("mycol").Should().Be(0);
        record.GetOrdinal("MYCOL").Should().Be(0);
    }

    [Fact]
    public void FakeDataRecord_GetOrdinal_throws_for_unknown_column()
    {
        var record = new FakeDataRecord(new() { ["id"] = 1 });
        var act = () => record.GetOrdinal("missing");
        act.Should().Throw<IndexOutOfRangeException>().WithMessage("*missing*");
    }

    [Fact]
    public void FakeDataRecord_IsDBNull_true_for_null_value()
    {
        var record = new FakeDataRecord(new() { ["bio"] = null });
        record.IsDBNull(0).Should().BeTrue();
    }

    [Fact]
    public void FakeDataRecord_IsDBNull_false_for_non_null_value()
    {
        var record = new FakeDataRecord(new() { ["name"] = "Alice" });
        record.IsDBNull(0).Should().BeFalse();
    }

    // ── Get<T> ────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_returns_int_column()
    {
        var record = new FakeDataRecord(new() { ["id"] = 42 });
        record.Get<int>("id").Should().Be(42);
    }

    [Fact]
    public void Get_returns_string_column()
    {
        var record = new FakeDataRecord(new() { ["name"] = "Widget" });
        record.Get<string>("name").Should().Be("Widget");
    }

    [Fact]
    public void Get_returns_decimal_column()
    {
        var record = new FakeDataRecord(new() { ["price"] = 9.99m });
        record.Get<decimal>("price").Should().Be(9.99m);
    }

    [Fact]
    public void Get_throws_InvalidCastException_for_null_column()
    {
        var record = new FakeDataRecord(new() { ["name"] = null });
        var act = () => record.Get<string>("name");
        act.Should().Throw<InvalidCastException>().WithMessage("*name*non-nullable*");
    }

    // ── GetOption<T> ──────────────────────────────────────────────────────────

    [Fact]
    public void GetOption_returns_Some_when_value_present()
    {
        var record = new FakeDataRecord(new() { ["bio"] = "developer" });
        var result = record.GetOption<string>("bio");
        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("developer"));
    }

    [Fact]
    public void GetOption_returns_None_when_column_is_null()
    {
        var record = new FakeDataRecord(new() { ["bio"] = null });
        var result = record.GetOption<string>("bio");
        result.IsNone.Should().BeTrue();
    }

    // ── GetOptionString ───────────────────────────────────────────────────────

    [Fact]
    public void GetOptionString_returns_Some_for_non_empty_string()
    {
        var record = new FakeDataRecord(new() { ["url"] = "https://example.com" });
        var result = record.GetOptionString("url");
        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public void GetOptionString_returns_None_for_null()
    {
        var record = new FakeDataRecord(new() { ["url"] = null });
        record.GetOptionString("url").IsNone.Should().BeTrue();
    }

    [Fact]
    public void GetOptionString_returns_None_for_empty_string()
    {
        var record = new FakeDataRecord(new() { ["url"] = "" });
        record.GetOptionString("url").IsNone.Should().BeTrue();
    }

    // ── Row mapper integration ────────────────────────────────────────────────

    [Fact]
    public void Row_mapper_using_Get_and_GetOption_produces_correct_record()
    {
        var record = new FakeDataRecord(new()
        {
            ["id"]    = 7,
            ["name"]  = "Sprocket",
            ["price"] = 4.50m,
            ["notes"] = null,
        });

        var product = MapProduct(record);

        product.Id.Should().Be(7);
        product.Name.Should().Be("Sprocket");
        product.Price.Should().Be(4.50m);
        product.Notes.IsNone.Should().BeTrue();
    }

    [Fact]
    public void Row_mapper_populates_optional_notes_when_present()
    {
        var record = new FakeDataRecord(new()
        {
            ["id"]    = 3,
            ["name"]  = "Bolt",
            ["price"] = 0.99m,
            ["notes"] = "stainless steel",
        });

        var product = MapProduct(record);

        product.Notes.IsSome.Should().BeTrue();
        product.Notes.IfSome(n => n.Should().Be("stainless steel"));
    }

    // Helper — mirrors what a real repository mapper would look like.
    private static TestProduct MapProduct(IDataRecord r) => new(
        r.Get<int>("id"),
        r.Get<string>("name"),
        r.Get<decimal>("price"),
        r.GetOption<string>("notes"));

    private sealed record TestProduct(long Id, string Name, decimal Price, Option<string> Notes);
}
