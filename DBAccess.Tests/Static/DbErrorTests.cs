namespace DBAccess.Tests.Static;

public sealed class DbErrorTests
{
    [Fact]
    public void FromMessage_sets_message_and_no_cause()
    {
        var err = DbError.FromMessage("something went wrong");

        err.Message.Should().Be("something went wrong");
        err.Cause.IsNone.Should().BeTrue();
    }

    [Fact]
    public void FromException_captures_exception_message()
    {
        var ex  = new InvalidOperationException("boom");
        var err = DbError.FromException(ex);

        err.Message.Should().Be("boom");
        err.Cause.IsSome.Should().BeTrue();
        err.Cause.IfSome(c => c.Should().BeSameAs(ex));
    }

    [Fact]
    public void FromException_with_context_uses_custom_message()
    {
        var ex  = new TimeoutException("timed out");
        var err = DbError.FromException("Query failed", ex);

        err.Message.Should().Be("Query failed");
        err.Cause.IsSome.Should().BeTrue();
    }

    [Fact]
    public void ToString_with_no_cause_formats_message_only()
    {
        var err = DbError.FromMessage("row not found");
        err.ToString().Should().Be("[DbError] row not found");
    }

    [Fact]
    public void ToString_with_cause_includes_exception_type_and_message()
    {
        var ex  = new ArgumentException("bad arg");
        var err = DbError.FromException("context", ex);

        err.ToString().Should().Contain("[DbError] context");
        err.ToString().Should().Contain("ArgumentException");
        err.ToString().Should().Contain("bad arg");
    }

    [Fact]
    public void Two_errors_with_same_values_are_equal()
    {
        var a = DbError.FromMessage("x");
        var b = DbError.FromMessage("x");
        a.Should().Be(b);
    }

    [Fact]
    public void Two_errors_with_different_messages_are_not_equal()
    {
        var a = DbError.FromMessage("x");
        var b = DbError.FromMessage("y");
        a.Should().NotBe(b);
    }
}
