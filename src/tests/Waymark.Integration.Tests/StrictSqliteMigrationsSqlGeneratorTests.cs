using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// Proves the STRICT override does its job, and keeps proving it across EF
/// upgrades — the generator extends a type in an EF <c>Internal</c> namespace,
/// so this suite is the thing that turns a silent behaviour change into a red
/// build (decisions.md D-016, costs 3 and 4).
///
/// <para>
/// A deliberately minimal context, unrelated to the real model. It exists to
/// exercise the generator, not to describe Waymark, so it cannot collide with
/// the schema work.
/// </para>
/// </summary>
public sealed class StrictSqliteMigrationsSqlGeneratorTests
{
    private sealed class Row
    {
        public string Id { get; set; } = "";
        public long AmountCentimes { get; set; }
    }

    private sealed class ProbeContext : DbContext
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options
                .UseSqlite("Data Source=:memory:")
                .ReplaceService<IMigrationsSqlGenerator, StrictSqliteMigrationsSqlGenerator>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            var row = builder.Entity<Row>();
            row.ToTable("probe_rows");
            row.HasKey(x => x.Id);
            row.Property(x => x.Id).HasColumnName("id");
            row.Property(x => x.AmountCentimes).HasColumnName("amount_centimes");
        }
    }

    /// <summary>Same model, EF's stock generator, for comparison.</summary>
    private sealed class UnpatchedContext : DbContext
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlite("Data Source=:memory:");

        protected override void OnModelCreating(ModelBuilder builder)
        {
            var row = builder.Entity<Row>();
            row.ToTable("probe_rows");
            row.HasKey(x => x.Id);
            row.Property(x => x.Id).HasColumnName("id");
            row.Property(x => x.AmountCentimes).HasColumnName("amount_centimes");
        }
    }

    [Fact]
    public void Created_tables_are_STRICT()
    {
        using var context = new ProbeContext();

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("STRICT", script, StringComparison.Ordinal);
        Assert.Matches(@"CREATE TABLE ""probe_rows""[\s\S]*?\)\s*STRICT", script);
    }

    [Fact]
    public void Without_the_override_EF_emits_no_STRICT()
    {
        // The reason this class exists. If this test ever starts failing, EF
        // has gained native STRICT support and the override can be deleted.
        using var context = new UnpatchedContext();

        var script = context.Database.GenerateCreateScript();

        Assert.DoesNotContain("STRICT", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_STRICT_table_from_the_generator_refuses_a_REAL()
    {
        // Emitting the keyword is not the same as the database enforcing it.
        var script = new ProbeContext().Database.GenerateCreateScript();

        var directory = Path.Combine(Path.GetTempPath(), "waymark-strict-gen", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={Path.Combine(directory, "probe.db")}"))
            {
                connection.Open();

                using (var create = connection.CreateCommand())
                {
                    create.CommandText = script;
                    create.ExecuteNonQuery();
                }

                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO probe_rows (id, amount_centimes) VALUES ('a', 12.5)";

                var error = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => insert.ExecuteNonQuery());
                Assert.Contains("cannot store REAL value in INTEGER column", error.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_rebuild_path_keeps_STRICT()
    {
        // The case that would otherwise slip through. SQLite cannot ALTER a
        // column, so EF builds a replacement table, copies into it, drops the
        // original and renames. If the replacement came out non-STRICT, a
        // routine column change would quietly disarm the type enforcement on
        // that table and nothing would say so.
        using var context = new ProbeContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var makeNullable = new AlterColumnOperation
        {
            Table = "probe_rows",
            Name = "amount_centimes",
            ClrType = typeof(long),
            ColumnType = "INTEGER",
            IsNullable = true,
            OldColumn = new AddColumnOperation
            {
                Table = "probe_rows",
                Name = "amount_centimes",
                ClrType = typeof(long),
                ColumnType = "INTEGER",
                IsNullable = false
            }
        };

        var sql = string.Join(
            Environment.NewLine,
            generator
                .Generate([makeNullable], context.GetService<IDesignTimeModel>().Model)
                .Select(command => command.CommandText));

        Assert.Contains("ef_temp_probe_rows", sql, StringComparison.Ordinal);
        Assert.Matches(@"CREATE TABLE ""ef_temp_probe_rows""[\s\S]*?\)\s*STRICT", sql);
    }

    [Fact]
    public void EF_own_history_table_is_left_alone()
    {
        // Cost 4: __EFMigrationsHistory belongs to EF. We append nothing to it.
        using var context = new ProbeContext();

        var historyScript = context.GetService<IHistoryRepository>().GetCreateScript();

        Assert.Contains(StrictSqliteMigrationsSqlGenerator.EfMigrationsHistoryTable, historyScript, StringComparison.Ordinal);
        Assert.DoesNotContain("STRICT", historyScript, StringComparison.Ordinal);
    }
}
