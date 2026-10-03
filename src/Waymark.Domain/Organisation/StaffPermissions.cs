namespace Waymark.Domain.Organisation;

/// <summary>
/// Something a person may or may not be allowed to do at the till or in Admin.
///
/// <para>
/// ✍ <b>This list is Hakim's to extend</b>, one entry per gated action as the blocks that
/// need them land. Every member here is one the code already asks for:
/// <see cref="NoSale"/> because <c>ICashDrawer.NoSale</c> is documented as permission-gated,
/// <see cref="ApplyDiscount"/> and <see cref="OverridePrice"/> and
/// <see cref="VoidTransaction"/> because blocks B4, B5 and B8 gate them, and
/// <see cref="DecideRecommendation"/> because D-074 already gates it and must not end up
/// with a second copy of the rule.
/// </para>
/// </summary>
public enum Capability
{
    /// <summary>Take money off a line or a sale (B4). <c>reason_codes.requires_manager</c> may raise it further.</summary>
    ApplyDiscount,

    /// <summary>Sell at a price other than the one in force (B5).</summary>
    OverridePrice,

    /// <summary>Take a line, or a whole sale, back out before it completes (B8).</summary>
    VoidTransaction,

    /// <summary>Open the drawer with no sale behind it. Audited, and the reason it is gated.</summary>
    NoSale,

    /// <summary>Accept, adjust or dismiss a recommendation (D-074).</summary>
    DecideRecommendation,

    /// <summary>
    /// Open a past ticket of another day or another till (B1, D-088). Today's tickets at one's own
    /// till need no rank: the cashier sold them.
    /// </summary>
    ViewOtherTickets,

    /// <summary>Create a customer at the till (B7, D-096): collecting a person's data, the notice handed over.</summary>
    CreateCustomer,

    /// <summary>
    /// Give, change, freeze or take away a customer's tab limit, and let one charge past it (B7,
    /// D-096). Every limit change is a <c>credit_limit_events</c> row with who made it.
    /// </summary>
    ManageCredit,

    /// <summary>
    /// Give money back for something sold (B9, D-098). On the ladder at the floor, so anyone with a
    /// rank refunds; the tenant's <c>refund_min_rank</c> may raise it, through
    /// <see cref="StaffPermissions.May(long?, Capability, long?)"/>.
    /// </summary>
    Refund,

    /// <summary>
    /// Take cash out of the drawer with no sale behind it (B10, D-102): at the floor, raised by the
    /// tenant's <c>paid_out_min_rank</c>, as <see cref="Refund"/> is.
    /// </summary>
    PaidOut,
}

/// <summary>
/// Who may do what, from <c>roles.rank</c> alone.
///
/// <para>
/// Pure, and in Domain for the reason <see cref="Engine.CardAudience"/> gives: a rule about
/// who may do what is the kind that gets quietly duplicated — once in the endpoint, once in
/// the till, once in Admin — and the copies drift. There is one of it.
/// </para>
///
/// <para>
/// <b>There is no permissions table, on purpose.</b> A capability names a minimum rank and
/// the comparison is the one <see cref="Engine.CardAudience.MayDecide"/> already makes:
/// this rank and anything above it. A real <c>role_permissions</c> table can arrive at H2,
/// when Admin needs to edit them; until then a migration would buy nothing.
/// </para>
///
/// <para>
/// <b>Why this one is dangerous.</b> It is the same comparison that was written backwards
/// once already (D-074, session D): a cashier could decide a manager's card and an owner
/// could not. Inverted here it is worse, because nothing refuses — a cashier discounts,
/// overrides and voids all day, every receipt prints, and the first sign is the month's
/// takings. The second trap is <see cref="May"/>'s null: a staff member whose role is not an
/// active row has <b>no</b> rank, and reading that as zero makes them the most junior person
/// in the shop instead of an error (D-037, and <c>RecommendationBoard.StaffAsync</c> already
/// returns null for exactly this).
/// </para>
/// </summary>
public static class StaffPermissions
{
    /// <summary>
    /// The ladder: the lowest <c>roles.rank</c> each capability needs (D-077).
    ///
    /// <para>
    /// <b>Private on purpose.</b> <c>readonly</c> protects the reference, not the contents, so
    /// a public readonly <see cref="Dictionary{TKey, TValue}"/> can be written to from any
    /// project in the solution — one line would hand price overrides to every cashier, with
    /// no error anywhere. <see cref="RequiredRank"/> is the one way to read it.
    /// </para>
    /// </summary>
    private static readonly Dictionary<Capability, long> PermissionMapping = new()
    {
        { Capability.ApplyDiscount, 2 },
        { Capability.DecideRecommendation, 3 },
        { Capability.NoSale, 2 },
        { Capability.OverridePrice, 3 },
        { Capability.VoidTransaction, 2 },
        { Capability.ViewOtherTickets, 2 },
        { Capability.CreateCustomer, 2 },
        { Capability.ManageCredit, 3 },

        // The floor: roles.rank is always above zero. A shop raises it with refund_min_rank (D-098).
        { Capability.Refund, 1 },

        // The floor too: a shop raises it with paid_out_min_rank (D-102).
        { Capability.PaidOut, 1 },
    };

    /// <summary>
    /// The lowest <c>roles.rank</c> that may do this.
    ///
    /// <para>
    /// In the synthetic store <c>cashier</c> is 1, <c>manager</c> 2 and <c>owner</c> 3, but
    /// nothing may assume that shape: a shop defines its own ladder, and <c>roles.rank</c>
    /// only promises to be positive (<c>ck_roles_rank</c>).
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A capability with no rank. It throws rather than defaulting, or a new capability would
    /// silently be available to everyone.
    /// </exception>
    public static long RequiredRank(Capability capability)
    {
        if (!PermissionMapping.TryGetValue(capability, out var requiredRank))
        {
            throw new ArgumentOutOfRangeException(
                nameof(capability), capability, "Unmapped capability, update the permission mapping.");
        }

        return requiredRank;
    }

    /// <summary>
    /// Whether a person of this rank may do this.
    /// </summary>
    /// <param name="staffRank">
    /// From <c>roles.rank</c>, or <b>null</b> when the person is not active staff or their
    /// role is not an active row. Null is never permission: absence is not zero (D-037).
    /// </param>
    /// <param name="capability">What they are trying to do.</param>
    public static bool May(long? staffRank, Capability capability)
    {
        if (staffRank is null)
        {
            return false;
        }

        // Through RequiredRank rather than the dictionary: one path to the ladder, so an
        // unmapped capability fails the same documented way from both methods.
        return staffRank >= RequiredRank(capability);
    }

    /// <summary>
    /// <b>Session B9</b> Whether a person of this rank may do this, when the shop has raised
    /// what it needs (D-098: <c>refund_min_rank</c>, the tenant's).
    ///
    /// <para><b>The rules <c>StaffPermissionsTests</c> hold you to:</b> a null rank is never
    /// permission, whatever the setting. With no setting (null), this is <see cref="May(long?, Capability)"/>
    /// exactly. A setting <b>raises and never lowers</b>: what is needed is the higher of the ladder's
    /// rank and the setting, so a setting of 1 cannot hand price overrides to a cashier. A setting of
    /// zero or less throws: <c>roles.rank</c> is always above zero, and a setting below every rank is
    /// a mistake, not "everyone".</para>
    /// </summary>
    /// <param name="staffRank">As for <see cref="May(long?, Capability)"/>: null when there is no rank.</param>
    /// <param name="capability">What they are trying to do.</param>
    /// <param name="raisedTo">The shop's setting for it; null when the shop set nothing.</param>
    public static bool May(long? staffRank, Capability capability, long? raisedTo)
    {
        if (raisedTo is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(raisedTo), raisedTo, "A setting names a rank, and roles.rank is above zero.");
        }

        if (staffRank is null)
        {
            return false;
        }

        // The higher of the ladder and the setting: a setting raises, never lowers.
        return staffRank >= Math.Max(RequiredRank(capability), raisedTo ?? 0);
    }
}
