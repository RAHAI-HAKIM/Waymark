namespace Waymark.Domain.Catalogue;

/// <summary>
/// What a unit measures. Stored as TEXT with a CHECK constraint
/// ('count','weight','volume','length').
///
/// <para>
/// The names here are C#'s, not the database's. Domain does not know how it is
/// spelled on disk — that mapping lives in the Persistence configuration, which
/// is the only place allowed to care.
/// </para>
/// </summary>
public enum UnitDimension
{
    Count,
    Weight,
    Volume,
    Length
}
