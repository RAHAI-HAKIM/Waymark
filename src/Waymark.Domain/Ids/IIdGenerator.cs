namespace Waymark.Domain.Ids;

/// <summary>
/// Where a new primary key comes from.
///
/// <para>
/// Declared in Domain and implemented outside it, like every other port here.
/// Domain states that something must mint ids and does not care what does.
/// </para>
/// <para>
/// <b>Never <c>Ulid.NewUlid()</c> in an entity constructor.</b> A static call
/// cannot be substituted, and CLAUDE.md §8 requires the synthetic store
/// generator to be deterministic from a seed — which stops being achievable the
/// moment one entity mints its own id. The same seed would produce different
/// ids on every run, so there would be no golden-file test over generated data,
/// no diff between two generated stores, and no reproducing a reported bug from
/// its seed (decisions.md D-038).
/// </para>
/// <para>
/// Command handlers mint every id for the whole unit of work before anything is
/// written.
/// </para>
/// </summary>
public interface IIdGenerator
{
    /// <summary>A new ULID, as the 26-character Crockford base32 text the schema stores.</summary>
    string NewId();
}
