using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// Who may open this till: the store's active staff whose role is an active row (session A5,
/// D-083). The list the sign-in screen shows; a PIN is never part of it.
/// </summary>
public sealed record TillStaff(
    [property: JsonPropertyName("staff")] IReadOnlyList<TillStaffMember> Staff);

/// <summary>One person on the sign-in screen.</summary>
/// <param name="HasPin">
/// Whether a PIN has been set for them. Without one they are listed, so they know they have not
/// been forgotten, but cannot be chosen (<c>StoreServer --set-pin=</c>).
/// </param>
public sealed record TillStaffMember(
    [property: JsonPropertyName("staff_id")] string StaffId,
    [property: JsonPropertyName("staff_name")] string StaffName,
    [property: JsonPropertyName("role_label_fr")] string RoleLabelFr,
    [property: JsonPropertyName("role_label_ar")] string RoleLabelAr,
    [property: JsonPropertyName("has_pin")] bool HasPin);

/// <summary>A person typing their PIN at a till.</summary>
public sealed record SignInRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("staff_id")] string StaffId,
    [property: JsonPropertyName("pin")] string Pin);

/// <summary>
/// StoreServer's answer to a sign-in. Every outcome is a 200, as everywhere between the till and
/// its server; an error status means only that the server failed.
/// </summary>
/// <param name="Outcome">One of <see cref="SignInOutcomes"/>.</param>
/// <param name="SessionToken">
/// When signed in: what the till sends in <see cref="TillSessionHeader.Name"/> with every sale.
/// The server knows who holds it, so a sale no longer says who is selling.
/// </param>
/// <param name="LockedUntil">When locked: the moment the person may try again.</param>
/// <param name="AttemptsLeft">After a wrong PIN: how many more before the lock.</param>
public sealed record SignInAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("session_token")] string? SessionToken,
    [property: JsonPropertyName("locked_until")] DateTimeOffset? LockedUntil,
    [property: JsonPropertyName("attempts_left")] int? AttemptsLeft);

/// <summary>The values of <see cref="SignInAnswer.Outcome"/>.</summary>
public static class SignInOutcomes
{
    public const string SignedIn = "signed_in";

    /// <summary>Not their PIN. <see cref="SignInAnswer.AttemptsLeft"/> says how many remain.</summary>
    public const string WrongPin = "wrong_pin";

    /// <summary>Too many wrong PINs in a row; nothing is checked until <see cref="SignInAnswer.LockedUntil"/>.</summary>
    public const string Locked = "locked";

    /// <summary>No PIN has been set for this person.</summary>
    public const string NoPin = "no_pin";

    /// <summary>Not active staff of this store with an active role.</summary>
    public const string UnknownStaff = "unknown_staff";

    /// <summary>This store has no such terminal.</summary>
    public const string UnknownTerminal = "unknown_terminal";
}

/// <summary>The header a signed-in till sends its session token in.</summary>
public static class TillSessionHeader
{
    public const string Name = "X-Waymark-Session";
}
