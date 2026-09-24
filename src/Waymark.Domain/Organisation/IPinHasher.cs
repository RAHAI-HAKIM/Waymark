namespace Waymark.Domain.Organisation;

/// <summary>
/// Turns a PIN into what <c>staff.pin_hash</c> stores, and checks a PIN against it (D-083).
///
/// <para>
/// A port, because hashing needs <c>System.Security.Cryptography</c>, which the architecture
/// tests keep out of every project but Pseudonymisation and the hosts. StoreServer implements
/// it: the host is the one place a PIN is set or checked.
/// </para>
/// </summary>
public interface IPinHasher
{
    /// <summary>What to store for this PIN. A fresh salt each time.</summary>
    /// <exception cref="ArgumentException">The PIN is not well formed (<see cref="StaffPin.IsWellFormed"/>).</exception>
    string Hash(string pin);

    /// <summary>
    /// Whether <paramref name="pin"/> is the one <paramref name="storedHash"/> was made from.
    /// False — never an exception — for a stored value that is unusable, malformed or made by
    /// another scheme: a broken row refuses the person, it does not crash the sign-in.
    /// </summary>
    bool Verify(string pin, string? storedHash);
}
