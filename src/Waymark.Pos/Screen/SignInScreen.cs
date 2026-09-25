using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>What the till knows before anybody signs in, gathered for one frame.</summary>
/// <param name="Context">The store and the till, for the top bar; null until StoreServer has said.</param>
public sealed record SignInState(
    TillText Text,
    TimeZoneInfo Zone,
    DateTimeOffset Now,
    ServerState Server,
    TillContext? Context,
    IReadOnlyList<TillStaffMember>? Staff,
    string? SelectedId,
    int DigitCount,
    SignInMessage? Message,
    bool Busy,
    bool MaySubmit);

/// <summary>
/// The sign-in screen, decided (session A5, G1 "Connexion"): who may open the till on one side,
/// the PIN pad on the other, and "Ouvrir la caisse". <b>No Almanac here</b>: nothing is anybody's
/// until somebody is signed in (D-074). The G1 board's clock-in box and "Pointer sans ouvrir la
/// caisse" are B10's.
/// </summary>
public sealed record SignInScreen(
    bool RightToLeft,
    TopBar Top,
    string Title,
    string? Subtitle,
    IReadOnlyList<StaffRow> People,
    EmptyState? Empty,
    PinPad Pad,
    NoticeLine? Message,
    PrimaryKey Open)
{
    /// <summary>The fewest dots the field shows: a PIN of four is the common case, and the field does not grow until a fifth.</summary>
    public const int MinimumSlots = 4;

    public static SignInScreen Build(SignInState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = state.Text;
        var context = state.Context is { Outcome: TillContextOutcome.Found } found ? found : null;

        var people = (state.Staff ?? []).Select(member => new StaffRow(
            member.StaffId,
            Initials(member.StaffName),
            member.StaffName,
            text.Language == TillLanguage.Arabic ? member.RoleLabelAr : member.RoleLabelFr,
            Selected: member.StaffId == state.SelectedId,
            Available: member.HasPin && !state.Busy,
            Chip: member.HasPin ? null : new Chip(Tone.Neutral, text.NoPinLabel))).ToList();

        var empty = state.Staff is { Count: 0 } ? new EmptyState(text.NobodyMaySignIn, text.NobodyMaySignInHint) : null;

        var selected = state.Staff?.FirstOrDefault(member => member.StaffId == state.SelectedId);
        var pad = new PinPad(
            selected is null ? text.ChooseYourName : text.PinOf(selected.StaffName),
            state.DigitCount,
            Math.Max(MinimumSlots, state.DigitCount),
            text.ClearKey,
            // The pad is live only for somebody chosen, and not while their PIN is being checked.
            Available: selected is not null && !state.Busy);

        return new SignInScreen(
            text.RightToLeft,
            new TopBar(
                context is null ? null : $"{context.StoreName} · {context.TerminalName}",
                Tab: null,
                state.Server.IsReachable ? new Connection(text.Online, Tone.Neutral) : new Connection(text.Offline, Tone.Critical),
                Staff: null,
                DisplayFigures.Clock(Local(state, state.Now))),
            text.WhoOpensTheTill,
            context?.TerminalName,
            people,
            empty,
            pad,
            MessageOf(state),
            new PrimaryKey(text.OpenTheTill, null, text.EnterKey, state.MaySubmit));
    }

    /// <summary>
    /// Whether <paramref name="next"/> is the screen the window drew last, so it need not be drawn
    /// again (<see cref="TillScreen.Compare"/> says why that matters). The list is compared by its
    /// people, the rest by the record.
    /// </summary>
    public static bool Same(SignInScreen? drawn, SignInScreen next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return drawn is not null && drawn.People.SequenceEqual(next.People) && drawn with { People = next.People } == next;
    }

    /// <summary>
    /// "NB" for "Nabil B.", "GS" for "Gérant (synthétique)": the first letter of the first two words
    /// that have one, upper-cased. Arabic has no capitals, and ToUpperInvariant leaves its letters
    /// as they are.
    /// </summary>
    public static string Initials(string name)
    {
        var letters = (name ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(FirstLetter)
            .OfType<string>()
            .Take(2);
        return string.Concat(letters).ToUpperInvariant();
    }

    /// <summary>The word's first letter, with any accent that follows it; null when it has none.</summary>
    private static string? FirstLetter(string word)
    {
        for (var i = 0; i < word.Length; i++)
        {
            if (char.IsLetter(word[i]))
            {
                return StringInfo.GetNextTextElement(word, i);
            }
        }

        return null;
    }

    private static NoticeLine? MessageOf(SignInState state)
    {
        var text = state.Text;

        // The server gone says so before anything a PIN attempt said: nothing else is true until it is back.
        if (!state.Server.IsReachable || state.Message?.Kind == SignInMessageKind.Offline)
        {
            return new NoticeLine(
                Tone.Critical,
                text.Offline,
                text.OfflineSince(DisplayFigures.Clock(Local(state, state.Server.UnreachableSince ?? state.Now))));
        }

        return state.Message switch
        {
            null => null,
            { Kind: SignInMessageKind.WrongPin, AttemptsLeft: var left } =>
                new NoticeLine(Tone.Warning, text.WrongPin, left is { } n ? text.AttemptsLeft(n) : string.Empty),
            { Kind: SignInMessageKind.Locked, LockedUntil: var until } =>
                new NoticeLine(Tone.Critical, text.Locked, text.LockedUntil(DisplayFigures.Clock(Local(state, until ?? state.Now)))),
            { Kind: SignInMessageKind.NoPin } => new NoticeLine(Tone.Warning, text.NoPinLabel, text.NoPinDetail),
            { Kind: SignInMessageKind.UnknownStaff } => new NoticeLine(Tone.Warning, text.UnknownStaff, text.UnknownStaffDetail),
            { Kind: SignInMessageKind.UnknownTerminal } => new NoticeLine(Tone.Critical, text.UnknownTerminal, text.UnknownTerminalDetail),
            { Kind: SignInMessageKind.NoTerminal } => new NoticeLine(Tone.Critical, text.UnknownTerminal, text.NoTerminalDetail),
            { Kind: SignInMessageKind.SessionEnded } => new NoticeLine(Tone.Warning, text.SessionEnded, text.SessionEndedDetail),
            _ => null,
        };
    }

    private static DateTimeOffset Local(SignInState state, DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, state.Zone);
}

/// <summary>One person in the list: initials, name, role, and whether they can be chosen.</summary>
/// <param name="Chip">"SANS CODE PIN" for somebody who cannot sign in yet: label before colour.</param>
public sealed record StaffRow(string StaffId, string Initials, string Name, string Role, bool Selected, bool Available, Chip? Chip);

/// <summary>The PIN field and its pad.</summary>
/// <param name="Filled">Dots filled: one per digit typed. Never the digits.</param>
/// <param name="Slots">Dots drawn: at least <see cref="SignInScreen.MinimumSlots"/>, one more per digit beyond.</param>
public sealed record PinPad(string Title, int Filled, int Slots, string ClearKey, bool Available);
