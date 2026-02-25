namespace DBAccess;

/// <summary>
/// Pipeline helpers that extend <see cref="EitherAsync{DbError,T}"/> with
/// common patterns used in repository and service layers.
/// </summary>
public static class DbPipelineExtensions
{
    /// <summary>
    /// Converts a zero-row result into a <see cref="DbError"/>. Useful when
    /// <see cref="Database{TConnection}.Execute"/> returns 0 and you want to
    /// treat that as a failure (e.g. row not found on UPDATE/DELETE).
    /// </summary>
    /// <param name="source">An either wrapping an affected-row count.</param>
    /// <param name="error">Error to produce when the count is zero.</param>
    /// <returns>
    /// <see cref="Right{Unit}"/> when at least one row was affected, or
    /// <see cref="Left{DbError}"/> with <paramref name="error"/>.
    /// </returns>
    public static EitherAsync<DbError, Unit> FailOnZeroRows(
        this EitherAsync<DbError, int> source,
        DbError error) =>
        source.Bind(rows => rows > 0
            ? RightAsync<DbError, Unit>(unit)
            : LeftAsync<DbError, Unit>(error));

    /// <summary>
    /// Unwraps <see cref="Option{T}"/> inside an either, turning <see cref="None"/>
    /// into a <see cref="Left{DbError}"/>.
    /// </summary>
    /// <typeparam name="T">The inner value type.</typeparam>
    /// <param name="source">An either wrapping an <see cref="Option{T}"/>.</param>
    /// <param name="error">Error to produce when the option is <see cref="None"/>.</param>
    /// <returns>
    /// <see cref="Right{T}"/> when the option has a value, otherwise
    /// <see cref="Left{DbError}"/> with <paramref name="error"/>.
    /// </returns>
    public static EitherAsync<DbError, T> FailOnNone<T>(
        this EitherAsync<DbError, Option<T>> source,
        DbError error) =>
        source.Bind(opt => opt.Match(
            Some: v  => RightAsync<DbError, T>(v),
            None: () => LeftAsync<DbError, T>(error)));

    /// <summary>
    /// Maps the <see cref="DbError"/> left side to a <see cref="string"/>
    /// for use in APIs or logging pipelines.
    /// </summary>
    /// <typeparam name="T">The right value type.</typeparam>
    /// <param name="source">Source either.</param>
    public static EitherAsync<string, T> MapErrorToString<T>(
        this EitherAsync<DbError, T> source) =>
        source.MapLeft(e => e.ToString());
}
