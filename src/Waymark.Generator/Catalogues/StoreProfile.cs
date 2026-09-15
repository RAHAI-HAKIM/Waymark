using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Generator.Catalogues;

// The shape of a catalogue's store.json: what the store is, as opposed to how it behaves.

internal sealed record StoreProfile
{
    public required string StoreCode { get; init; }

    public required string StoreName { get; init; }

    public required string StoreType { get; init; }

    public string? Address { get; init; }

    public required string Currency { get; init; }

    public required Rounding RoundingPolicy { get; init; }

    /// <summary>Hours ahead of UTC. Fixed: Algeria has no daylight saving.</summary>
    public required int UtcOffsetHours { get; init; }

    public required IReadOnlyList<string> Terminals { get; init; }

    public required IReadOnlyList<RoleDefinition> Roles { get; init; }

    /// <summary>The role whose first staff member becomes <c>stores.manager_id</c>.</summary>
    public required string ManagerRole { get; init; }

    public required IReadOnlyList<StaffDefinition> Staff { get; init; }

    /// <summary>Day type to local opening intervals, "HH:mm-HH:mm".</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> OpeningHours { get; init; }

    public required IReadOnlyList<ReasonCodeDefinition> ReasonCodes { get; init; }

    public required IReadOnlyList<NoticeDefinition> Notices { get; init; }
}

internal sealed record RoleDefinition
{
    public required string Code { get; init; }

    public required int Rank { get; init; }

    public required string LabelFr { get; init; }

    public required string LabelAr { get; init; }
}

internal sealed record StaffDefinition
{
    public required string Name { get; init; }

    public required string Role { get; init; }

    public required DateOnly Joined { get; init; }
}

internal sealed record ReasonCodeDefinition
{
    public required string Code { get; init; }

    public required ReasonCodeAppliesTo AppliesTo { get; init; }

    public required string LabelFr { get; init; }

    public required string LabelAr { get; init; }

    public bool RequiresNote { get; init; }

    public bool RequiresManager { get; init; }
}

internal sealed record NoticeDefinition
{
    public required string Code { get; init; }

    public required NoticeType Type { get; init; }

    public required Language Language { get; init; }

    public required string Body { get; init; }
}
