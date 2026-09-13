namespace Waymark.Domain.Hardware;

/// <summary>
/// The cash drawer.
///
/// <para>
/// On a real till the drawer is not a device the software talks to. It is wired
/// to the printer's kick port, and opening it means sending the printer a pulse
/// command — so a store with no printer has no drawer either. The
/// implementation models that rather than hiding it, because "the receipt
/// printed but the drawer did not open" is a support call whose cause is
/// upstream of both.
/// </para>
/// </summary>
public interface ICashDrawer
{
    /// <summary>
    /// Opens the drawer, for a stated reason.
    ///
    /// <para>
    /// <b>There is no parameterless overload, and there must never be one.</b>
    /// A drawer that opens without a recorded reason is the shrinkage-audit hole
    /// <c>System_Architecture</c> §2 names: a no-sale open is exactly how cash
    /// leaves a till unaccounted for, so the reason is the audit record's
    /// subject and not a diagnostic nicety.
    /// </para>
    /// </summary>
    /// <param name="reason">Why the drawer is opening.</param>
    Task OpenAsync(DrawerOpenReason reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Why the drawer opened. Every member is an audit answer, not a log level.
///
/// <para>
/// <see cref="NoSale"/> is the one that matters. It is a permission-gated action
/// for a retailer or manager, and the common shrinkage-audit question is how
/// many of them a shift produced. The others are ordinary cash handling and are
/// each already a row somewhere — a transaction, a cash movement, a session.
/// </para>
/// </summary>
public enum DrawerOpenReason
{
    /// <summary>A completed sale taking cash.</summary>
    Sale,

    /// <summary>A cash refund.</summary>
    Refund,

    /// <summary>Opened with no transaction. Permission-gated, and audited.</summary>
    NoSale,

    /// <summary>Petty cash in.</summary>
    PaidIn,

    /// <summary>Petty cash out.</summary>
    PaidOut,

    /// <summary>Counting the float at the start of a shift.</summary>
    ShiftStart,

    /// <summary>Counting down at the end of a shift.</summary>
    ShiftEnd,
}
