namespace DBAccess;

/// <summary>
/// Represents a typed database error. Carries either a plain message or a
/// wrapped <see cref="Exception"/>. Use the static factory methods so
/// call-sites remain expression-body friendly.
/// </summary>
/// <param name="Message">Human-readable description of the failure.</param>
/// <param name="Cause">The underlying exception, if one was thrown.</param>
public sealed record DbError(string Message, Option<Exception> Cause)
{
    /// <summary>Creates a <see cref="DbError"/> from a plain message string.</summary>
    public static DbError FromMessage(string message) =>
        new(message, None);

    /// <summary>Creates a <see cref="DbError"/> by capturing an exception.</summary>
    public static DbError FromException(Exception ex) =>
        new(ex.Message, Some(ex));

    /// <summary>Creates a <see cref="DbError"/> with a contextual message and root cause.</summary>
    public static DbError FromException(string message, Exception ex) =>
        new(message, Some(ex));

    /// <inheritdoc/>
    public override string ToString() =>
        Cause.Match(
            ex => $"[DbError] {Message} — {ex.GetType().Name}: {ex.Message}",
            ()  => $"[DbError] {Message}");
}
