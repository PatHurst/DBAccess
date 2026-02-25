namespace DBAccess;

/// <summary>
/// Extension methods for <see cref="IDataRecord"/> that make column reading
/// safer and more expression-friendly inside row-mapping lambdas.
/// </summary>
public static class DataRecordExtensions
{
    /// <summary>
    /// Reads a non-nullable column value cast to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="record">The current data record.</param>
    /// <param name="column">Column name.</param>
    /// <exception cref="InvalidCastException">
    /// Thrown when the column value is <c>DBNull</c>.
    /// </exception>
    public static T Get<T>(this IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        if (record.IsDBNull(ordinal))
            throw new InvalidCastException(
                $"Column '{column}' is NULL but was read as non-nullable {typeof(T).Name}.");
        return (T)record.GetValue(ordinal);
    }

    /// <summary>
    /// Reads a nullable column, returning <see cref="None"/> when the column
    /// is <c>DBNull</c> and <see cref="Some{T}"/> otherwise.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="record">The current data record.</param>
    /// <param name="column">Column name.</param>
    public static Option<T> GetOption<T>(this IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        return record.IsDBNull(ordinal) ? None : Some((T)record.GetValue(ordinal));
    }

    /// <summary>
    /// Reads a <see cref="string"/> column, returning <see cref="None"/> for
    /// <c>DBNull</c> or empty strings.
    /// </summary>
    /// <param name="record">The current data record.</param>
    /// <param name="column">Column name.</param>
    public static Option<string> GetOptionString(this IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        if (record.IsDBNull(ordinal)) return None;
        var value = record.GetString(ordinal);
        return string.IsNullOrEmpty(value) ? None : Some(value);
    }
}
