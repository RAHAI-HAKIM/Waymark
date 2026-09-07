using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Sqlite.Migrations.Internal;

namespace Waymark.Persistence;

/// <summary>
/// Appends <c>STRICT</c> to every table a migration creates.
///
/// <para>
/// EF Core does not emit <c>STRICT</c> for SQLite, and without it SQLite
/// accepts a REAL into an INTEGER column. The hand-written schema declares all
/// of its tables STRICT; the moment a migration creates a table, that
/// enforcement would silently stop applying to it — the build green, the tests
/// green, and a float able to reach a money column again. See decisions.md
/// D-016, cost 3.
/// </para>
/// <para>
/// This matters on the rebuild path as much as on plain creation. SQLite cannot
/// <c>ALTER</c> most things, so EF creates a replacement table, copies, drops
/// and renames. That replacement goes through <see cref="CreateTableOperation"/>
/// too, so overriding this one method covers both.
/// </para>
/// <para>
/// <b>Recorded risk.</b> <c>SqliteMigrationsSqlGenerator</c> lives in an
/// <c>Internal</c> namespace, which EF Core does not guarantee across versions.
/// There is no public base for SQLite migration SQL generation, so this is the
/// available approach. The guard is
/// <c>StrictSqliteMigrationsSqlGeneratorTests</c>: if an EF upgrade changes the
/// shape, the tests fail at upgrade time rather than in a shop.
/// </para>
/// </summary>
public sealed class StrictSqliteMigrationsSqlGenerator : SqliteMigrationsSqlGenerator
{
    /// <summary>
    /// EF Core's own bookkeeping table. Its shape belongs to EF, not to us, so
    /// we leave it exactly as EF wants it (decisions.md D-016, cost 4).
    /// </summary>
    public const string EfMigrationsHistoryTable = "__EFMigrationsHistory";

    public StrictSqliteMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IRelationalAnnotationProvider annotationProvider)
        : base(dependencies, annotationProvider)
    {
    }

    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (string.Equals(operation.Name, EfMigrationsHistoryTable, StringComparison.Ordinal))
        {
            base.Generate(operation, model, builder, terminate);
            return;
        }

        // Let the base emit the whole statement up to and including the closing
        // parenthesis, then append the table option before the terminator.
        base.Generate(operation, model, builder, terminate: false);
        builder.Append(" STRICT");

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }
}
