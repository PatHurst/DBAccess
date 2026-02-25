# DBAccess

A lightweight, functional ADO.NET database access layer for .NET 10, built on [LanguageExt](https://github.com/louthy/language-ext).

Every operation returns `EitherAsync<DbError, T>`. There are no exceptions to catch at the call-site, no nulls to guard against, and no hidden control flow — just composable, railway-oriented pipelines from query to response.

---

## Contents

- [Philosophy](#philosophy)
- [Requirements](#requirements)
- [Getting Started](#getting-started)
- [API Reference](#api-reference)
  - [Database\<TConnection\>](#databasetconnection)
  - [CommandBuilder](#commandbuilder)
  - [DataRecordExtensions](#datarecordextensions)
  - [DbError](#dberror)
  - [DbPipelineExtensions](#dbpipelineextensions)
- [Patterns](#patterns)
  - [Defining a Repository](#defining-a-repository)
  - [Sequential Operations with Bind](#sequential-operations-with-bind)
  - [Not Found vs Error](#not-found-vs-error)
  - [Transactions](#transactions)
  - [Consuming Results](#consuming-results)
  - [Traversing Collections](#traversing-collections)
- [Provider Notes](#provider-notes)
- [Testing](#testing)
- [Design Decisions](#design-decisions)

---

## Philosophy

Most database layers surface failures as thrown exceptions and represent missing rows as `null`. Both force the caller to handle the failure path through out-of-band mechanisms — `try/catch` blocks and null checks — that don't compose and are easy to forget.

DBAccess makes the failure path explicit in the return type. Every operation returns `EitherAsync<DbError, T>`: a `Right<T>` on success or a `Left<DbError>` on failure. All exceptions are caught at the lowest level and converted to `DbError` — nothing propagates up the stack uninvited. Missing rows are represented as `Option<T>`, keeping "not found" clearly distinct from "something went wrong".

The practical result is that repository and service code is a chain of `Map` and `Bind` calls with a single `.Match` at the outermost boundary — a controller action, a background job, a log line — where both outcomes are handled explicitly.

---

## Requirements

- .NET 10
- [LanguageExt.Core](https://www.nuget.org/packages/LanguageExt.Core) 4.4.9
- Any ADO.NET provider — referenced by the **consuming project**, not this library:
  - PostgreSQL: `Npgsql`
  - SQL Server: `Microsoft.Data.SqlClient`
  - SQLite: `Microsoft.Data.Sqlite`
  - MySQL: `MySqlConnector`

---

## Getting Started

### 1. Add your ADO.NET provider

```xml
<PackageReference Include="Npgsql" Version="8.*" />
```

### 2. Instantiate `Database<TConnection>`

Pass the concrete connection type as the type parameter and the connection string as the constructor argument. No factory class or interface is needed.

```csharp
using DBAccess;
using Npgsql;

var db = new Database<NpgsqlConnection>("Host=localhost;Database=mydb;Username=app;Password=secret");
```

### 3. Register in DI (optional)

```csharp
services.AddSingleton(new Database<NpgsqlConnection>(connectionString));
```

### 4. Write a row mapper

A mapper is a pure function from `IDataRecord` to your domain type. The `IDataRecord` extension methods handle null-safety cleanly.

```csharp
static Product Map(IDataRecord r) => new(
    r.Get<int>("id"),
    r.Get<string>("name"),
    r.Get<decimal>("price"),
    r.GetOptionString("notes"));
```

### 5. Query

```csharp
var result = await db.Query(
    conn => CommandBuilder.For(conn)
                .WithSql("SELECT id, name, price, notes FROM products ORDER BY name")
                .Build(),
    Map);

result.Match(
    Right: products => Console.WriteLine($"Found {products.Count} products"),
    Left:  err      => Console.WriteLine($"Error: {err}"));
```

---

## API Reference

### `Database<TConnection>`

```csharp
public sealed class Database<TConnection>
    where TConnection : IDbConnection, new()
```

The central class. Holds no mutable state — safe to register as a singleton. Two constructors are available.

#### Constructors

**`Database(string connectionString)`** — standard path. A fresh `TConnection` is created, opened, and disposed for every operation. This is the right choice for all production database servers, which manage connection pooling internally.

```csharp
var db = new Database<NpgsqlConnection>("Host=localhost;...");
```

**`Database(Func<TConnection> factory)`** — advanced path. Connection creation is delegated entirely to the caller. The caller retains ownership — `Database` will **never dispose** a connection obtained from this constructor. Useful when the provider requires custom construction, or when a test fixture needs to supply a single shared connection (see [Testing](#testing)).

```csharp
var db = new Database<SqliteConnection>(() => myPersistentConnection);
```

---

#### `Query<T>`

```csharp
EitherAsync<DbError, Seq<T>> Query<T>(
    Func<TConnection, IDbCommand> build,
    Func<IDataRecord, T> map)
```

Executes a `SELECT` and projects every matched row through `map`. Returns an empty `Seq<T>` — not an error — when the query matches zero rows.

```csharp
var products = await db.Query(
    conn => CommandBuilder.For(conn)
                .WithSql("SELECT * FROM products WHERE active = @active")
                .WithParam("@active", true)
                .Build(),
    Map);
```

---

#### `QueryOne<T>`

```csharp
EitherAsync<DbError, T> QueryOne<T>(
    Func<TConnection, IDbCommand> build,
    Func<IDataRecord, T> map)
```

Executes a `SELECT` and returns the first row. Returns `Left<DbError>` if zero rows are returned. Use this when absence is genuinely a program error rather than a normal outcome.

```csharp
var config = await db.QueryOne(
    conn => CommandBuilder.For(conn)
                .WithSql("SELECT value FROM config WHERE key = @key")
                .WithParam("@key", "smtp_host")
                .Build(),
    r => r.Get<string>("value"));
```

---

#### `QueryOption<T>`

```csharp
EitherAsync<DbError, Option<T>> QueryOption<T>(
    Func<TConnection, IDbCommand> build,
    Func<IDataRecord, T> map)
```

Executes a `SELECT` and wraps the first row in `Option<T>`. Returns `None` — not an error — when zero rows match. Prefer this over `QueryOne` for user-facing lookups where "not found" is a normal outcome. Chain `.FailOnNone(...)` if you need to convert absence into an error downstream.

```csharp
var user = await db.QueryOption(
    conn => CommandBuilder.For(conn)
                .WithSql("SELECT * FROM users WHERE id = @id")
                .WithParam("@id", userId)
                .Build(),
    MapUser);
// EitherAsync<DbError, Option<User>>
```

---

#### `Scalar<T>`

```csharp
EitherAsync<DbError, T> Scalar<T>(
    Func<TConnection, IDbCommand> build)
```

Executes the command and returns the first column of the first row, converted to `T` via `Convert.ChangeType`. Returns `Left<DbError>` if the result is `null`. Useful for `COUNT(*)`, `MAX(...)`, and provider-specific row-id queries.

```csharp
var count = await db.Scalar<long>(
    conn => CommandBuilder.For(conn)
                .WithSql("SELECT COUNT(*) FROM orders WHERE status = @s")
                .WithParam("@s", "pending")
                .Build());
```

---

#### `Execute`

```csharp
EitherAsync<DbError, int> Execute(
    Func<TConnection, IDbCommand> build)
```

Executes a non-query command and returns the number of rows affected. Chain `.FailOnZeroRows(...)` when zero affected rows should be treated as a failure.

```csharp
var affected = await db.Execute(
    conn => CommandBuilder.For(conn)
                .WithSql("DELETE FROM sessions WHERE expires_at < @now")
                .WithParam("@now", DateTimeOffset.UtcNow)
                .Build());
```

---

#### `Transact<T>` / `Transact`

```csharp
// Returns a value
EitherAsync<DbError, T> Transact<T>(
    Func<TConnection, IDbTransaction, Task<T>> work,
    IsolationLevel isolation = IsolationLevel.ReadCommitted)

// Side-effect only, returns Unit
EitherAsync<DbError, Unit> Transact(
    Func<TConnection, IDbTransaction, Task> work,
    IsolationLevel isolation = IsolationLevel.ReadCommitted)
```

Runs the callback inside a single transaction. Commits on success; rolls back automatically on any exception or faulted `Task`. Pass both `conn` and `tx` into `CommandBuilder.For(conn, tx)` for every command inside the block.

```csharp
await db.Transact(async (conn, tx) =>
{
    using var debit = CommandBuilder.For(conn, tx)
        .WithSql("UPDATE accounts SET balance = balance - @amount WHERE id = @from")
        .WithParam("@amount", 100m)
        .WithParam("@from",   fromId)
        .Build();
    debit.ExecuteNonQuery();

    using var credit = CommandBuilder.For(conn, tx)
        .WithSql("UPDATE accounts SET balance = balance + @amount WHERE id = @to")
        .WithParam("@amount", 100m)
        .WithParam("@to",     toId)
        .Build();
    credit.ExecuteNonQuery();
});
```

---

### `CommandBuilder`

Fluent builder over `IDbCommand` that eliminates repetitive ADO.NET parameter ceremony. Calls `conn.CreateCommand()` and `cmd.CreateParameter()` on the standard interfaces, so it works with any ADO.NET provider without modification. `null` parameter values are automatically mapped to `DBNull.Value`.

```csharp
using var cmd = CommandBuilder
    .For(conn)                    // .For(conn, tx) inside a Transact block
    .WithSql("SELECT * FROM orders WHERE customer_id = @cid AND status = @status")
    .WithParam("@cid",    customerId)
    .WithParam("@status", "pending")
    .Build();
```

The caller owns disposal of the returned `IDbCommand`.

---

### `DataRecordExtensions`

Null-safe helpers for reading columns inside row mappers, as extension methods on `IDataRecord`.

#### `Get<T>(string column)`

Reads a non-nullable column and casts to `T`. Throws `InvalidCastException` if the value is `DBNull`. Treat this as a schema contract violation.

```csharp
var id    = r.Get<long>("id");
var name  = r.Get<string>("name");
var price = r.Get<decimal>("price");
```

> **SQLite type note:** `Microsoft.Data.Sqlite` returns `long` for all `INTEGER` columns and `double` for all `REAL` columns, regardless of your type argument. `r.Get<int>("id")` will throw a runtime `InvalidCastException` against SQLite results. Use `Get<long>` and `Get<double>` in SQLite mappers. See [Provider Notes](#provider-notes).

#### `GetOption<T>(string column)`

Returns `Some(value)` when the column has a value and `None` when it is `DBNull`.

```csharp
var managerId = r.GetOption<long>("manager_id"); // Option<long>
```

#### `GetOptionString(string column)`

Like `GetOption<string>`, but also returns `None` for empty strings.

```csharp
var website = r.GetOptionString("website"); // None if NULL or ""
```

---

### `DbError`

```csharp
public sealed record DbError(string Message, Option<Exception> Cause)
```

The typed error on the left side of every `EitherAsync`. Carries a human-readable message and optionally the underlying exception.

| Factory method | When to use |
|---|---|
| `DbError.FromMessage(string)` | Logical failures with no underlying exception |
| `DbError.FromException(Exception)` | Wrapping a caught exception directly |
| `DbError.FromException(string, Exception)` | Adding context to a caught exception |

`ToString()` produces a consistently formatted string suitable for logging:

```
[DbError] User 42 not found
[DbError] QueryOne failed — SqlException: Invalid object name 'users'
```

---

### `DbPipelineExtensions`

Extension methods on `EitherAsync<DbError, T>` for common patterns.

#### `FailOnZeroRows(DbError error)`

Converts an `EitherAsync<DbError, int>` (from `Execute`) to `EitherAsync<DbError, Unit>`, treating zero affected rows as the supplied error.

```csharp
await db.Execute(conn => CommandBuilder.For(conn)
            .WithSql("DELETE FROM products WHERE id = @id")
            .WithParam("@id", id)
            .Build())
        .FailOnZeroRows(DbError.FromMessage($"Product {id} not found"));
```

#### `FailOnNone<T>(DbError error)`

Unwraps `Option<T>` inside an `EitherAsync`. `Some(v)` becomes `Right(v)`; `None` becomes `Left(error)`. Bridges `QueryOption` (where absence is neutral) with callers where absence is an error.

```csharp
await db.QueryOption(build, Map)
        .FailOnNone(DbError.FromMessage($"User {id} not found"));
// EitherAsync<DbError, User>
```

#### `MapErrorToString<T>()`

Maps `Left<DbError>` to `Left<string>` for use in API responses or logging sinks.

```csharp
var result = await repo.GetById(id).MapErrorToString();
// EitherAsync<string, User>
```

---

## Patterns

### Defining a Repository

Keep the mapper `static`. Inject the typed `Database<TConnection>` directly into the constructor.

```csharp
sealed class OrderRepository(Database<NpgsqlConnection> db)
{
    static Order Map(IDataRecord r) => new(
        r.Get<long>("id"),
        r.Get<long>("customer_id"),
        r.Get<decimal>("total"),
        r.Get<string>("status"),
        r.GetOption<DateTimeOffset>("shipped_at"));

    public EitherAsync<DbError, Seq<Order>> GetByCustomer(long customerId) =>
        db.Query(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM orders WHERE customer_id = @cid ORDER BY created_at DESC")
                        .WithParam("@cid", customerId)
                        .Build(),
            Map);

    public EitherAsync<DbError, Option<Order>> FindById(long id) =>
        db.QueryOption(
            conn => CommandBuilder.For(conn)
                        .WithSql("SELECT * FROM orders WHERE id = @id")
                        .WithParam("@id", id)
                        .Build(),
            Map);

    public EitherAsync<DbError, Order> GetById(long id) =>
        FindById(id).FailOnNone(DbError.FromMessage($"Order {id} not found"));

    public EitherAsync<DbError, Unit> Ship(long id) =>
        db.Execute(
            conn => CommandBuilder.For(conn)
                        .WithSql("UPDATE orders SET status = 'shipped', shipped_at = NOW() WHERE id = @id")
                        .WithParam("@id", id)
                        .Build())
          .FailOnZeroRows(DbError.FromMessage($"Order {id} not found"));
}
```

### Sequential Operations with Bind

Use `Bind` when each step depends on the result of the previous one. The chain short-circuits to `Left` on the first failure — no nested `if` checks or early returns.

```csharp
// Create then immediately fetch the persisted record
var order = await repo
    .Create(customerId: 7, total: 149.99m)
    .Bind(repo.GetById);
```

### Not Found vs Error

`QueryOption` separates "no row" from "something went wrong". Use the result shape to drive different HTTP responses:

```csharp
var result = await repo.FindById(id);

return result.Match(
    Right: opt => opt.Match(
        Some: order => Results.Ok(order),
        None: ()    => Results.NotFound()),
    Left:  err  => Results.Problem(err.ToString()));
```

Or collapse "not found" into an error immediately with `FailOnNone` for a single-level match:

```csharp
return await repo
    .FindById(id)
    .FailOnNone(DbError.FromMessage($"Order {id} not found"))
    .Match(
        Right: order => Results.Ok(order),
        Left:  err   => Results.Problem(err.ToString()));
```

### Transactions

Wrap multiple commands in `Transact`. If the callback throws for any reason the transaction is rolled back and `Left<DbError>` is returned — nothing is committed.

```csharp
var result = await db.Transact(async (conn, tx) =>
{
    using var insert = CommandBuilder.For(conn, tx)
        .WithSql("INSERT INTO products (name, price) VALUES (@name, @price)")
        .WithParam("@name",  name)
        .WithParam("@price", price)
        .Build();
    insert.ExecuteNonQuery();

    using var audit = CommandBuilder.For(conn, tx)
        .WithSql("INSERT INTO audit_log (action, occurred_at) VALUES (@action, @ts)")
        .WithParam("@action", "product_created")
        .WithParam("@ts",     DateTimeOffset.UtcNow)
        .Build();
    audit.ExecuteNonQuery();
});
```

### Consuming Results

At the boundary, use `.Match` to handle both outcomes:

```csharp
app.MapGet("/orders/{id:long}", async (long id, OrderRepository repo) =>
    await repo
        .GetById(id)
        .MapErrorToString()
        .Match(
            Right: order => Results.Ok(order),
            Left:  err   => Results.Problem(err)));
```

For side-effects with no return value, use `.IfRight` / `.IfLeft`:

```csharp
(await repo.Ship(orderId)).Match(
    Right: _ => logger.LogInformation("Order {Id} shipped", orderId),
    Left: err => logger.LogError("Ship failed for {Id}: {Err}", orderId, err));
```

### Traversing Collections

To run the same operation for a collection of inputs and collect all results, use LanguageExt's `TraverseSerial`. The chain short-circuits to the first `Left`:

```csharp
var ids = new long[] { 1, 2, 3, 4 };

var orders = await ids
    .Select(id => repo.GetById(id))
    .TraverseSerial(x => x);

orders.Match(
    Right: seq => Console.WriteLine($"Fetched {seq.Count} orders"),
    Left:  err => Console.WriteLine($"Lookup failed: {err}"));
```

---

## Provider Notes

### SQLite (`Microsoft.Data.Sqlite`)

**Column types.** The SQLite driver always returns specific CLR types from `IDataRecord`, regardless of your type argument to `Get<T>`:

| SQL type | CLR type returned |
|---|---|
| `INTEGER` | `long` |
| `REAL` | `double` |
| `TEXT` | `string` |
| `BLOB` | `byte[]` |

`Get<int>("id")` will throw an `InvalidCastException` at runtime because the driver returns `long`. Always use `Get<long>` and `Get<double>` in SQLite mappers:

```csharp
// PostgreSQL / SQL Server mapper
static Product Map(IDataRecord r) => new(r.Get<int>("id"), r.Get<string>("name"), r.Get<decimal>("price"));

// SQLite mapper
static Product Map(IDataRecord r) => new((int)r.Get<long>("id"), r.Get<string>("name"), (decimal)r.Get<double>("price"));
```

**Multi-statement SQL.** SQLite's `ExecuteScalar` on a multi-statement string only processes the first statement. The pattern `INSERT ...; SELECT last_insert_rowid()` in a single command does not work — `ExecuteScalar` returns `null` because it never advances past the `INSERT`. Use two sequential commands instead:

```csharp
// Broken
.WithSql("INSERT INTO t (name) VALUES (@name); SELECT last_insert_rowid();")

// Correct — two separate commands on the same connection
using var insert = CommandBuilder.For(conn).WithSql("INSERT INTO t (name) VALUES (@name)").WithParam("@name", name).Build();
insert.ExecuteNonQuery();

using var rowid = CommandBuilder.For(conn).WithSql("SELECT last_insert_rowid()").Build();
var id = (long)rowid.ExecuteScalar()!;
```

PostgreSQL's `RETURNING` clause avoids this issue entirely:

```sql
INSERT INTO products (name, price) VALUES (@name, @price) RETURNING id
```

**In-memory databases in tests.** A SQLite `:memory:` database exists only for the lifetime of the single connection that created it. Using `new Database<SqliteConnection>("Data Source=:memory:")` means every operation opens a new connection to an independent (empty) database — the table you just created is gone by the next call. Use the factory constructor to share one persistent connection:

```csharp
var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var db = new Database<SqliteConnection>(() => connection);
// connection must stay open for the lifetime of db
```

`Database` never disposes a connection obtained via the factory constructor. The caller owns its lifetime.

---

## Testing

The solution includes `DBAccess.Tests` with both static and live test suites. Run all tests with:

```bash
dotnet test
```

No external database server is required — live tests use SQLite in-process.

### Static tests

Static tests verify types and logic without touching a database at all.

`FakeDataRecord` is a test-only `IDataRecord` backed by a `Dictionary<string, object?>`. It lets you test row mappers as pure functions:

```csharp
var record = new FakeDataRecord(new()
{
    ["id"]    = 1L,       // long — matches SQLite behaviour
    ["name"]  = "Widget",
    ["notes"] = null,
});

var product = Map(record);
product.Name.Should().Be("Widget");
product.Notes.IsNone.Should().BeTrue();
```

The static test classes cover `DbError` (factory methods, equality, `ToString`), `CommandBuilder` (parameter wiring, null mapping, transaction binding), `DataRecordExtensions` (`Get<T>`, `GetOption<T>`, `GetOptionString`, full mapper composition), and `DbPipelineExtensions` (`FailOnZeroRows`, `FailOnNone`, `MapErrorToString`, short-circuit behaviour of `Bind`).

### Live tests

Live tests run against SQLite in-memory databases. Two xUnit class fixtures manage connection lifetime:

**`SqliteFixture`** — shared by `QueryTests`, `MutationTests`, and `PipelineIntegrationTests`. Owns one persistent `SqliteConnection`; each test class clears rows in its `IAsyncLifetime.InitializeAsync`.

**`TransactionFixture`** — used exclusively by `TransactionTests`. An isolated connection per test class so `BeginTransaction` / `Commit` / `Rollback` calls don't conflict with other tests sharing the same connection.

To run against a real server, replace `SqliteConnection` with your provider and supply a real connection string. All test logic is otherwise identical.

---

## Design Decisions

**No Dapper, no ORM.** The only dependency beyond the BCL is `LanguageExt.Core`. Mapping is explicit and co-located with the query — there is no reflection, no convention-based mapping, and no configuration to debug. `CommandBuilder` and `DataRecordExtensions` remove enough boilerplate that raw ADO.NET is comfortable at this abstraction level.

**Provider via type parameter.** `Database<TConnection>` takes the connection type as `where TConnection : IDbConnection, new()`. No factory interface to implement, no extra registration step. Swapping providers is a one-token change.

**Two constructors, explicit ownership.** The string constructor creates and disposes one connection per operation (right for pooled servers). The factory constructor borrows a connection and never disposes it (right for SQLite in tests, or providers with custom construction requirements). A private `_ownsConnections` flag tracks which mode is active — the caller never has to think about it.

**`Either`, not exceptions.** All exceptions are captured inside `TryAsync` at the `Run` / `RunInTransaction` boundary and converted to `Left<DbError>`. Application code above the repository layer is exception-free for database calls. The failure path is part of the type signature, not a side channel.

**`QueryOption` vs `QueryOne`.** A missing row and a failed query are semantically different. `QueryOption` returns `Option<T>` and lets the caller decide whether absence is an error in their context. `QueryOne` treats absence as an error immediately. Both exist so repositories express precise intent rather than forcing callers to unwrap and re-wrap `Option`.

**`Seq<T>`, not `IEnumerable<T>`.** LanguageExt's `Seq<T>` is immutable and strictly evaluated. Rows are fully materialised before leaving `Query`, so callers cannot accidentally re-execute a deferred enumeration by iterating twice or passing it across an `await`.
