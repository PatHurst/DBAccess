/*
 * ─────────────────────────────────────────────────────────────────────────────
 * USAGE EXAMPLE — not compiled as part of the library.
 *
 * Pass your ADO.NET connection type as the type parameter; no factory
 * interface required. The connection string is the only constructor argument.
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * 1. Instantiate (or register in DI)
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * using DBAccess;
 * using Npgsql;                          // or Microsoft.Data.SqlClient, etc.
 *
 * // Direct instantiation
 * var db = new Database<NpgsqlConnection>("Host=localhost;Database=shop;...");
 *
 * // DI registration
 * services.AddSingleton(new Database<NpgsqlConnection>(connectionString));
 *
 *
 * 2. Define your domain record and a repository
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * sealed record Product(int Id, string Name, decimal Price);
 *
 * sealed class ProductRepository(Database<NpgsqlConnection> db)
 * {
 *     // Row mapper — a pure function, easy to unit-test in isolation.
 *     static Product Map(IDataRecord r) => new(
 *         r.Get<int>("id"),
 *         r.Get<string>("name"),
 *         r.Get<decimal>("price"));
 *
 *     public EitherAsync<DbError, Seq<Product>> GetAll() =>
 *         db.Query(
 *             conn => CommandBuilder.For(conn)
 *                         .WithSql("SELECT id, name, price FROM products")
 *                         .Build(),
 *             Map);
 *
 *     public EitherAsync<DbError, Option<Product>> FindById(int id) =>
 *         db.QueryOption(
 *             conn => CommandBuilder.For(conn)
 *                         .WithSql("SELECT id, name, price FROM products WHERE id = @id")
 *                         .WithParam("@id", id)
 *                         .Build(),
 *             Map);
 *
 *     // FailOnNone converts Option<Product>.None into a DbError.
 *     public EitherAsync<DbError, Product> GetById(int id) =>
 *         FindById(id).FailOnNone(DbError.FromMessage($"Product {id} not found"));
 *
 *     public EitherAsync<DbError, int> Create(string name, decimal price) =>
 *         db.Scalar<int>(
 *             conn => CommandBuilder.For(conn)
 *                         .WithSql("INSERT INTO products (name, price) VALUES (@name, @price) RETURNING id")
 *                         .WithParam("@name", name)
 *                         .WithParam("@price", price)
 *                         .Build());
 *
 *     // FailOnZeroRows treats 0 affected rows as a DbError.
 *     public EitherAsync<DbError, Unit> Delete(int id) =>
 *         db.Execute(
 *             conn => CommandBuilder.For(conn)
 *                         .WithSql("DELETE FROM products WHERE id = @id")
 *                         .WithParam("@id", id)
 *                         .Build())
 *           .FailOnZeroRows(DbError.FromMessage($"Product {id} not found"));
 * }
 *
 *
 * 3. Compose operations — no try/catch, no nulls
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * var repo = new ProductRepository(db);
 *
 * // Sequential: create then fetch the persisted record
 * var result = await repo
 *     .Create("Widget", 9.99m)
 *     .Bind(repo.GetById);
 *
 * result.Match(
 *     Right: p   => Console.WriteLine($"Created: {p.Id} — {p.Name}"),
 *     Left:  err => Console.WriteLine($"Failed:  {err}"));
 *
 *
 * 4. Transactional batch
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * var txResult = await db.Transact(async (conn, tx) =>
 * {
 *     using var debit = CommandBuilder.For(conn, tx)
 *         .WithSql("UPDATE accounts SET balance = balance - @amount WHERE id = @from")
 *         .WithParam("@amount", 100m)
 *         .WithParam("@from", 1)
 *         .Build();
 *     debit.ExecuteNonQuery();
 *
 *     using var credit = CommandBuilder.For(conn, tx)
 *         .WithSql("UPDATE accounts SET balance = balance + @amount WHERE id = @to")
 *         .WithParam("@amount", 100m)
 *         .WithParam("@to", 2)
 *         .Build();
 *     credit.ExecuteNonQuery();
 * });
 *
 * txResult.Match(
 *     Right: _ => Console.WriteLine("Transfer complete."),
 *     Left: err => Console.WriteLine($"Transfer failed: {err}"));
 */
