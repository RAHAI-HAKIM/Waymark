namespace Waymark.Domain.Organisation;

/// <summary>
/// Whether a <c>staff.pin_hash</c> is one anybody could
/// ever log in with — asked <b>before</b> any PIN is checked against it.
///
/// <para>
/// This is deliberately not the verifier and knows nothing about how a PIN is hashed. The
/// algorithm is still Hakim's to choose, and it will live in infrastructure with whatever it
/// needs; <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1) and a KDF is not a
/// domain rule. What <i>is</i> a domain rule is which stored values can never authenticate at
/// all, and that has to be decided in one place rather than inside each caller.
/// </para>
///
/// <para>
/// <b>Why it exists.</b> The generator writes <see cref="NeverUsable"/> into every synthetic
/// staff row: synthetic staff are records, not accounts, and a real hash of a guessable PIN
/// in demo data is a credential waiting to be copied. Once a real verifier exists, that
/// string must fail closed. The danger is the shape of the mistake — a verifier that hashes
/// the offered PIN and compares it to <c>"synthetic:no-login"</c> happens to refuse
/// everything today by luck, and would start accepting the moment the stored format changes
/// to something a hash could collide with. Refusing on purpose, first, is not the same as
/// refusing by accident.
/// </para>
/// </summary>
public static class StaffPin
{
    /// <summary>
    /// The hash the synthetic store writes, which no PIN may ever match.
    ///
    /// <para>
    /// The generator declares the same constant as <c>ReferenceData.SyntheticPinHash</c>, and
    /// a test holds the two equal: nothing that ships may reference
    /// <c>Waymark.Generator</c> (D-054), so the string exists twice on purpose and drift
    /// between the copies would silently make every generated staff row loginable.
    /// </para>
    /// </summary>
    public const string NeverUsable = "synthetic:no-login";

    /// <summary>
    /// Whether this stored hash could authenticate somebody. False means refuse without
    /// checking the PIN at all.
    /// </summary>
    /// <param name="storedHash">The value in <c>staff.pin_hash</c>.</param>
    public static bool IsUsable(string? storedHash)
    {
        if (storedHash == NeverUsable || String.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }
        return true;
    }
}
