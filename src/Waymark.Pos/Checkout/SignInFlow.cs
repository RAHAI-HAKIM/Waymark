using System.Text;
using Waymark.Contracts.Pos;
using Waymark.Pos.Server;

namespace Waymark.Pos.Checkout;

/// <summary>
/// The sign-in screen's state (session A5, D-083): who is listed, who was touched, how many digits
/// are typed, and what the last attempt said. Plain C#, so every rule a cashier would notice is
/// tested without a window.
///
/// <para>
/// <b>The till decides nothing about a PIN.</b> It does not check a length beyond what the pad can
/// hold, keeps no count of wrong attempts and never unlocks anybody: all of that is StoreServer's,
/// where a restarted till cannot reset it. The digits typed never leave this class except in the
/// one request that sends them, and are forgotten as soon as it is sent.
/// </para>
/// </summary>
public sealed class SignInFlow(ITillServer server, TillIdentity till)
{
    /// <summary>What the pad holds at most; the server refuses a longer PIN anyway.</summary>
    public const int MaximumDigits = 8;

    /// <summary>What the pad needs before "Ouvrir la caisse" is available.</summary>
    public const int MinimumDigits = 4;

    private readonly StringBuilder _digits = new(MaximumDigits);

    /// <summary>Who may open this till; null until StoreServer has said.</summary>
    public IReadOnlyList<TillStaffMember>? Staff { get; private set; }

    /// <summary>The person touched in the list, if any. Only someone with a PIN can be.</summary>
    public string? SelectedId { get; private set; }

    /// <summary>How many digits are typed. The digits themselves are never shown, nor exposed.</summary>
    public int DigitCount => _digits.Length;

    /// <summary>What the last attempt, or the list, said; null when there is nothing to say.</summary>
    public SignInMessage? Message { get; private set; }

    /// <summary>Whether a PIN is on its way to the server. The pad ignores keys meanwhile.</summary>
    public bool Busy { get; private set; }

    /// <summary>Whether "Ouvrir la caisse" may be pressed.</summary>
    public bool MaySubmit => !Busy && SelectedId is not null && DigitCount >= MinimumDigits;

    /// <summary>
    /// Whether the list must be asked for again once the server answers: it was never had, or the
    /// last thing the server did was not answer. A till switched on before StoreServer has finished
    /// starting — both start with Windows at the Basic tier — otherwise shows nobody, and says it is
    /// offline, until it is restarted.
    /// </summary>
    public bool NeedsList => Staff is null || Message?.Kind == SignInMessageKind.Offline;

    /// <summary>Raised after every change.</summary>
    public event EventHandler? Changed;

    /// <summary>Asks StoreServer who may open this till.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var staff = await server.StaffAsync(cancellationToken);
        if (staff is null)
        {
            Message = new SignInMessage(SignInMessageKind.Offline);
            Raise();
            return;
        }

        Staff = staff.Staff;
        if (Message?.Kind == SignInMessageKind.Offline)
        {
            Message = null;
        }

        // Somebody who has left the list (their PIN cleared, or their contract ended) is not
        // still selected with digits waiting to be sent.
        if (SelectedId is not null && !Staff.Any(member => member.StaffId == SelectedId && member.HasPin))
        {
            SelectedId = null;
            _digits.Clear();
        }

        Raise();
    }

    /// <summary>Touches a person in the list. Someone without a PIN cannot be chosen; touching them says why.</summary>
    public void Select(string staffId)
    {
        if (Busy || Staff?.FirstOrDefault(member => member.StaffId == staffId) is not { } member)
        {
            return;
        }

        if (!member.HasPin)
        {
            Message = new SignInMessage(SignInMessageKind.NoPin);
            Raise();
            return;
        }

        SelectedId = member.StaffId;
        _digits.Clear();
        Message = null;
        Raise();
    }

    /// <summary>A digit from the pad or the keyboard. Anything but '0' to '9' is ignored, as is a ninth digit.</summary>
    public void Press(char digit)
    {
        if (Busy || SelectedId is null || digit is < '0' or > '9' || _digits.Length >= MaximumDigits)
        {
            return;
        }

        _digits.Append(digit);
        Raise();
    }

    public void Backspace()
    {
        if (!Busy && _digits.Length > 0)
        {
            _digits.Length--;
            Raise();
        }
    }

    /// <summary>"Effacer".</summary>
    public void Clear()
    {
        if (!Busy && _digits.Length > 0)
        {
            _digits.Clear();
            Raise();
        }
    }

    /// <summary>Something the till must say before anybody signs in: the session it held has ended.</summary>
    public void Tell(SignInMessageKind kind)
    {
        Message = new SignInMessage(kind);
        Raise();
    }

    /// <summary>
    /// Sends the PIN ("Ouvrir la caisse"). The digits are cleared whatever the answer: a wrong PIN
    /// is typed again, never corrected.
    /// </summary>
    /// <returns>The person signed in; null when not, with <see cref="Message"/> saying why.</returns>
    public async Task<SignedInStaff?> SubmitAsync(CancellationToken cancellationToken = default)
    {
        if (!MaySubmit || SelectedId is not { } staffId)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(till.TerminalId))
        {
            _digits.Clear();
            Message = new SignInMessage(SignInMessageKind.NoTerminal);
            Raise();
            return null;
        }

        var pin = _digits.ToString();
        _digits.Clear();
        Busy = true;
        Raise();

        SignInAnswer? answer;
        try
        {
            answer = await server.SignInAsync(new SignInRequest(till.TerminalId, staffId, pin), cancellationToken);
        }
        finally
        {
            Busy = false;
        }

        SignedInStaff? signedIn = null;
        switch (answer)
        {
            case null:
                Message = new SignInMessage(SignInMessageKind.Offline);
                break;

            case { Outcome: SignInOutcomes.SignedIn, SessionToken: { } token }:
                signedIn = new SignedInStaff(staffId, token, Staff?.FirstOrDefault(member => member.StaffId == staffId)?.StaffName);
                SelectedId = null;
                Message = null;
                break;

            case { Outcome: SignInOutcomes.WrongPin }:
                Message = new SignInMessage(SignInMessageKind.WrongPin, AttemptsLeft: answer.AttemptsLeft);
                break;

            case { Outcome: SignInOutcomes.Locked }:
                Message = new SignInMessage(SignInMessageKind.Locked, LockedUntil: answer.LockedUntil);
                break;

            case { Outcome: SignInOutcomes.UnknownTerminal }:
                Message = new SignInMessage(SignInMessageKind.UnknownTerminal);
                break;

            default:
                // no_pin or unknown_staff: the list this till holds is out of date. Say so, and
                // ask for it again so the person is shown as they now are.
                Message = new SignInMessage(
                    answer.Outcome == SignInOutcomes.NoPin ? SignInMessageKind.NoPin : SignInMessageKind.UnknownStaff);
                SelectedId = null;
                Raise();
                await LoadAsync(cancellationToken);
                return null;
        }

        Raise();
        return signedIn;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>What the sign-in screen has to say.</summary>
public enum SignInMessageKind
{
    WrongPin,
    Locked,
    NoPin,
    UnknownStaff,
    UnknownTerminal,

    /// <summary>The till was started without <c>--terminal=</c>.</summary>
    NoTerminal,

    Offline,

    /// <summary>The server no longer held this till's session; the ticket was kept.</summary>
    SessionEnded,
}

/// <param name="AttemptsLeft">After a wrong PIN, as the server counted them.</param>
/// <param name="LockedUntil">When locked, as the server's clock set it.</param>
public sealed record SignInMessage(SignInMessageKind Kind, int? AttemptsLeft = null, DateTimeOffset? LockedUntil = null);
