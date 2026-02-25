namespace DBAccess.Tests.Fakes;

/// <summary>
/// An in-memory <see cref="IDataRecord"/> implementation suitable for unit-testing
/// row mappers without touching a real database. Construct it with a dictionary of
/// column name → value pairs; <c>null</c> values are stored and reported as
/// <c>DBNull</c>.
/// </summary>
/// <example>
/// <code>
/// var record = new FakeDataRecord(new()
/// {
///     ["id"]   = 1,
///     ["name"] = "Alice",
///     ["bio"]  = null,
/// });
/// </code>
/// </example>
public sealed class FakeDataRecord : IDataRecord
{
    private readonly IReadOnlyList<string> _names;
    private readonly IReadOnlyList<object?> _values;

    /// <summary>
    /// Initialises the record from a name→value dictionary.
    /// Insertion order determines ordinal index.
    /// </summary>
    public FakeDataRecord(Dictionary<string, object?> columns)
    {
        _names  = columns.Keys.ToList();
        _values = columns.Values.ToList();
    }

    // ── IDataRecord ──────────────────────────────────────────────────────────

    public int FieldCount => _names.Count;

    public int GetOrdinal(string name)
    {
        var index = _names
            .Select((n, i) => (n, i))
            .FirstOrDefault(t => string.Equals(t.n, name, StringComparison.OrdinalIgnoreCase))
            .i;

        if (!_names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            throw new IndexOutOfRangeException($"Column '{name}' not found in FakeDataRecord.");

        return index;
    }

    public bool IsDBNull(int i) => _values[i] is null;

    public object GetValue(int i) => _values[i] ?? DBNull.Value;

    public string GetName(int i) => _names[i];

    public Type GetFieldType(int i) =>
        _values[i]?.GetType() ?? typeof(object);

    public string GetDataTypeName(int i) => GetFieldType(i).Name;

    // Typed accessors delegate to GetValue for simplicity.
    public bool    GetBoolean(int i)  => (bool)GetValue(i);
    public byte    GetByte(int i)     => (byte)GetValue(i);
    public long    GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => 0;
    public char    GetChar(int i)     => (char)GetValue(i);
    public long    GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => 0;
    public Guid    GetGuid(int i)     => (Guid)GetValue(i);
    public short   GetInt16(int i)    => (short)GetValue(i);
    public int     GetInt32(int i)    => (int)GetValue(i);
    public long    GetInt64(int i)    => (long)GetValue(i);
    public float   GetFloat(int i)    => (float)GetValue(i);
    public double  GetDouble(int i)   => (double)GetValue(i);
    public string  GetString(int i)   => (string)GetValue(i);
    public decimal GetDecimal(int i)  => (decimal)GetValue(i);
    public DateTime GetDateTime(int i)=> (DateTime)GetValue(i);

    public IDataReader GetData(int i) =>
        throw new NotSupportedException("GetData is not supported on FakeDataRecord.");

    public int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public object this[int i]    => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
}
