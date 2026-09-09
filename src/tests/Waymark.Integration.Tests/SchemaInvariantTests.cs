using Microsoft.Data.Sqlite;

namespace Waymark.Integration.Tests;

/// <summary>
/// The rules in CLAUDE.md §3, asserted against the database the schema
/// actually produces rather than against the file that is supposed to produce
/// it.
///
/// <para>
/// These are invariants, not a snapshot. None of them counts tables or pins a
/// version, so adding a table never breaks them — only breaking a rule does.
/// This is the half of the D-016 fidelity check that needs no DbContext; the
/// other half, comparing this database against one built by migrating from
/// empty, arrives with the model.
/// </para>
/// </summary>
public sealed class SchemaInvariantTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _schema;

    public SchemaInvariantTests(MigratedDatabaseFixture schema) => _schema = schema;

    // EF Core owns __EFMigrationsHistory and __EFMigrationsLock. Their shape
    // is EF's business, not ours, and the lock table has exactly the
    // rowid-alias key these rules forbid everywhere else.
    private const string EfTablePrefix = "__EF";

    // -----------------------------------------------------------------------
    // §3.1 — money is never a float
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_table_is_STRICT()
    {
        var lax = _schema.Query($"""
            SELECT name FROM pragma_table_list
            WHERE schema = 'main'
              AND type = 'table'
              AND strict = 0
              AND name NOT LIKE 'sqlite_%'
              AND name NOT LIKE '{EfTablePrefix}%'
            ORDER BY name
            """);

        Assert.True(lax.Count == 0,
            "STRICT is what makes the decimal rule mechanical: without it SQLite "
            + "accepts a REAL into an INTEGER money column. These tables are not "
            + "STRICT:\n  " + string.Join("\n  ", lax));
    }

    [Fact]
    public void No_column_anywhere_is_declared_REAL()
    {
        var floats = _schema.Query("""
            SELECT m.name || '.' || i.name
            FROM sqlite_schema m
            JOIN pragma_table_info(m.name) i
            WHERE m.type = 'table' AND m.name NOT LIKE '__EF%' AND upper(i.type) = 'REAL'
            ORDER BY 1
            """);

        Assert.True(floats.Count == 0,
            "CLAUDE.md §3.1: monetary columns are TEXT or INTEGER, never REAL, "
            + "and nothing that is summed may be a float. Found:\n  "
            + string.Join("\n  ", floats));
    }

    [Fact]
    public void Money_and_quantity_columns_are_INTEGER()
    {
        // Name-based, so it is a guard rather than a proof. The exclusions are
        // columns whose names contain a money word but which are not amounts:
        // "price_type" is an enum, "last_price_at" a timestamp.
        var wrong = _schema.Query("""
            SELECT m.name || '.' || i.name || ' is ' || i.type
            FROM sqlite_schema m
            JOIN pragma_table_info(m.name) i
            WHERE m.type = 'table'
              AND (i.name LIKE '%price%' OR i.name LIKE '%amount%' OR i.name LIKE '%cost%'
                   OR i.name LIKE '%total%' OR i.name LIKE '%quantity%' OR i.name LIKE '%qty%')
              AND i.name NOT LIKE '%\_at' ESCAPE '\'
              AND i.name NOT LIKE '%\_type' ESCAPE '\'
              AND i.name NOT LIKE '%\_id' ESCAPE '\'
              AND i.type <> 'INTEGER'
            ORDER BY 1
            """);

        Assert.True(wrong.Count == 0,
            "Money is minor units as INTEGER and quantity is thousandths as "
            + "INTEGER. These columns are neither:\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void STRICT_actually_refuses_a_REAL_in_a_money_column()
    {
        // The rule is only worth anything if the engine enforces it, so assert
        // the behaviour rather than the declaration.
        using var connection = _schema.Connect(enforceForeignKeys: false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cash_movements
                (movement_id, session_id, movement_type, amount, reason_code, staff_id, occurred_at)
            VALUES ('01TEST', 's1', 'paid_in', 12.50, 'r1', 'st1', '2026-01-01T00:00:00Z')
            """;

        var error = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Contains("cannot store REAL value in INTEGER column", error.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // §3.2 — every primary key is a ULID generated in application code
    // -----------------------------------------------------------------------

    [Fact]
    public void No_table_relies_on_a_rowid_alias_for_its_identity()
    {
        // A single-column INTEGER PRIMARY KEY *is* the rowid in SQLite, and
        // SQLite assigns it. That is database-generated identity by another
        // name, and it collides across offline terminals. Composite keys with
        // an INTEGER component are fine — parameter_registry versions one.
        var rowidAliases = _schema.Query("""
            SELECT m.name
            FROM sqlite_schema m
            WHERE m.type = 'table'
              AND m.name NOT LIKE '__EF%'
              AND (SELECT count(*) FROM pragma_table_info(m.name) WHERE pk > 0) = 1
              AND (SELECT upper(type) FROM pragma_table_info(m.name) WHERE pk > 0) = 'INTEGER'
            ORDER BY 1
            """);

        Assert.True(rowidAliases.Count == 0,
            "CLAUDE.md §3.2: no database-assigned identity, so offline terminals "
            + "cannot collide. These tables have a rowid-alias primary key:\n  "
            + string.Join("\n  ", rowidAliases));
    }

    [Fact]
    public void No_table_uses_AUTOINCREMENT()
    {
        var offenders = _schema.Query("""
            SELECT name FROM sqlite_schema
            WHERE type = 'table' AND name NOT LIKE '__EF%' AND upper(sql) LIKE '%AUTOINCREMENT%'
            ORDER BY 1
            """);

        Assert.True(offenders.Count == 0,
            "CLAUDE.md §3.2: no database autoincrement, anywhere, ever. Found in:\n  "
            + string.Join("\n  ", offenders));
    }

    // -----------------------------------------------------------------------
    // The database is coherent
    // -----------------------------------------------------------------------

    [Fact]
    public void The_schema_produces_a_sound_database()
    {
        Assert.Equal("ok", _schema.Scalar("PRAGMA integrity_check"));
        Assert.Equal("0", _schema.Scalar("SELECT count(*) FROM pragma_foreign_key_check"));
    }

    [Theory]
    // Compliance tables — CLAUDE.md §4, and what Waymark_DPIA_v1 promises.
    [InlineData("consent_events")]
    [InlineData("processing_log")]
    [InlineData("data_subject_requests")]
    [InlineData("retention_policies")]
    // The outbox pattern the whole sync design rests on — CLAUDE.md §3.6.
    [InlineData("outbox")]
    [InlineData("inbox")]
    // Engine parameters arrive with a version and a computed_at — §5.
    [InlineData("parameter_registry")]
    public void Required_table_exists(string table)
    {
        var found = _schema.Scalar(
            $"SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = '{table}'");

        Assert.Equal("1", found);
    }
}
