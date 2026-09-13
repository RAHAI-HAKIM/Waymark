namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'half_even', 'half_up'.
/// </summary>
public enum RoundingPolicies
{
    /// <summary>Stored as <c>half_even</c>.</summary>
    HalfEven,

    /// <summary>Stored as <c>half_up</c>.</summary>
    HalfUp
}
