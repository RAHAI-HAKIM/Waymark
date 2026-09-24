namespace Waymark.Domain.Organisation;

/// <summary>
/// Who and where the till is, in the words its top bar shows: the store, the till, and the
/// person selling (session A4).
///
/// <para>
/// A read, like the product lookup: it stages nothing. Store scoping is the global filter, so a
/// terminal or a staff member of another store is simply not found (CLAUDE.md §3.3), and the
/// till says so rather than showing another shop's name.
/// </para>
/// <para>
/// Until sign-in (A5) the person is whoever the till was started with (<c>--staff=</c>); A5 will
/// ask this same directory for the person who typed their PIN.
/// </para>
/// </summary>
public interface ITillDirectory
{
    /// <summary>The till and, when they are active staff with an active role, the person at it.</summary>
    /// <returns>Null when this store has no such terminal.</returns>
    Task<TillDescription?> DescribeAsync(string terminalId, string? staffId, CancellationToken cancellationToken = default);
}

/// <summary>The till as its top bar names it.</summary>
/// <param name="Staff">
/// Null when nobody is named, or the person named is not active staff of this store, or their
/// role is not an active row. Shown as nobody, never as a guess: the same rule that gives such a
/// person no rank (D-037, D-077).
/// </param>
/// <param name="Currency">The store's currency code, <c>stores.currency</c>: what an empty ticket totals in.</param>
public sealed record TillDescription(string StoreName, string TerminalName, string Currency, StaffDescription? Staff);

/// <summary>The person at the till, with their role's labels in both languages.</summary>
public sealed record StaffDescription(string StaffName, string RoleLabelFr, string RoleLabelAr);
