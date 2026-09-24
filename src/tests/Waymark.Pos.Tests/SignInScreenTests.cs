using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// The sign-in screen, decided (session A5, G1 "Connexion"). The silent failures: a dot per digit
/// that shows how long a PIN is before it is finished, a lock time in UTC on a till in Algiers, a
/// person with no PIN drawn as choosable, and an outage worded as a wrong PIN.
/// </summary>
public sealed class SignInScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 7, 1, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    private static readonly TillStaffMember Nabil = new("nabil", "Nabil B.", "Caissier", "أمين الصندوق", HasPin: true);
    private static readonly TillStaffMember Karim = new("karim", "Karim M.", "Responsable", "مسؤول", HasPin: false);

    private static readonly TillContext Context =
        new(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", null, null, null);

    private static SignInScreen Build(
        string? selected = null,
        int digits = 0,
        SignInMessage? message = null,
        bool busy = false,
        bool maySubmit = false,
        ServerState? server = null,
        IReadOnlyList<TillStaffMember>? staff = null,
        TillText? text = null) =>
        SignInScreen.Build(new SignInState(
            text ?? TillText.French, Algiers, Now, server ?? ServerState.Reachable, Context,
            staff ?? [Karim, Nabil], selected, digits, message, busy, maySubmit));

    [Fact]
    public void The_screen_asks_who_opens_the_till_and_names_the_till()
    {
        var screen = Build();

        Assert.Equal("Qui ouvre la caisse ?", screen.Title);
        Assert.Equal("Caisse 1", screen.Subtitle);
        Assert.Equal("El Bahdja · Caisse 1", screen.Top.Place);
        Assert.Null(screen.Top.Tab);
        Assert.Null(screen.Top.Staff);
    }

    [Fact]
    public void Each_person_has_initials_a_name_and_a_role()
    {
        var row = Build().People[1];

        Assert.Equal(("NB", "Nabil B.", "Caissier"), (row.Initials, row.Name, row.Role));
        Assert.True(row.Available);
        Assert.Null(row.Chip);
    }

    [Fact]
    public void Somebody_with_no_pin_is_listed_with_a_label_saying_so()
    {
        // Label before colour: the row says why, rather than only looking greyed out.
        var row = Build().People[0];

        Assert.False(row.Available);
        Assert.Equal(new Chip(Tone.Neutral, "SANS CODE PIN"), row.Chip);
    }

    [Fact]
    public void Nobody_chosen_the_pad_asks_for_a_name_and_is_unavailable()
    {
        var pad = Build().Pad;

        Assert.Equal("Choisissez votre nom", pad.Title);
        Assert.False(pad.Available);
    }

    [Fact]
    public void Chosen_the_pad_names_them_and_the_row_is_selected()
    {
        var screen = Build(selected: "nabil");

        Assert.Equal("Code PIN de Nabil B.", screen.Pad.Title);
        Assert.True(screen.Pad.Available);
        Assert.True(screen.People[1].Selected);
    }

    [Theory]
    [InlineData(0, 0, 4)]
    [InlineData(2, 2, 4)]
    [InlineData(4, 4, 4)]
    [InlineData(6, 6, 6)]
    [InlineData(8, 8, 8)]
    public void Four_dots_until_a_fifth_digit_then_one_per_digit(int typed, int filled, int slots)
    {
        // Four empty dots say nothing about a PIN's length; a field sized to the stored PIN would.
        var pad = Build(selected: "nabil", digits: typed).Pad;

        Assert.Equal((filled, slots), (pad.Filled, pad.Slots));
    }

    [Fact]
    public void Open_the_till_is_available_only_when_the_flow_says_so()
    {
        Assert.False(Build(selected: "nabil", digits: 4, maySubmit: false).Open.Enabled);
        Assert.True(Build(selected: "nabil", digits: 4, maySubmit: true).Open.Enabled);
        Assert.Equal("Ouvrir la caisse", Build().Open.Title);
    }

    [Fact]
    public void While_a_pin_is_checked_nothing_can_be_touched()
    {
        var screen = Build(selected: "nabil", digits: 4, busy: true);

        Assert.False(screen.Pad.Available);
        Assert.All(screen.People, row => Assert.False(row.Available));
    }

    // ---------------------------------------------------------------- messages

    [Fact]
    public void A_wrong_pin_is_a_warning_with_the_attempts_left()
    {
        var message = Build(message: new SignInMessage(SignInMessageKind.WrongPin, AttemptsLeft: 3)).Message!;

        Assert.Equal((Tone.Warning, "CODE INCORRECT", "Encore 3 essais avant le blocage"), (message.Tone, message.Label, message.Text));
        Assert.Equal("Encore 1 essai avant le blocage", Build(message: new SignInMessage(SignInMessageKind.WrongPin, AttemptsLeft: 1)).Message!.Text);
    }

    [Fact]
    public void Locked_is_critical_and_says_the_time_on_the_tills_clock()
    {
        var until = new DateTimeOffset(2026, 9, 24, 7, 6, 0, TimeSpan.Zero);

        var message = Build(message: new SignInMessage(SignInMessageKind.Locked, LockedUntil: until)).Message!;

        Assert.Equal((Tone.Critical, "BLOQUÉ"), (message.Tone, message.Label));
        Assert.Contains("08:06", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outage_is_said_as_an_outage_whatever_the_last_attempt_said()
    {
        var since = new ServerState(Now.AddMinutes(-2));

        var message = Build(message: new SignInMessage(SignInMessageKind.WrongPin, AttemptsLeft: 3), server: since).Message!;

        Assert.Equal((Tone.Critical, "HORS LIGNE"), (message.Tone, message.Label));
        Assert.Contains("07:59", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_message_has_a_label()
    {
        foreach (var kind in Enum.GetValues<SignInMessageKind>())
        {
            var message = Build(message: new SignInMessage(kind, AttemptsLeft: 2, LockedUntil: Now)).Message;

            Assert.NotNull(message);
            Assert.False(string.IsNullOrWhiteSpace(message.Label), $"{kind} has no label.");
        }
    }

    [Fact]
    public void Nobody_on_the_list_is_said_rather_than_left_blank()
    {
        var screen = Build(staff: []);

        Assert.Empty(screen.People);
        Assert.Equal("Personne ne peut ouvrir cette caisse", screen.Empty!.Title);
    }

    // ------------------------------------------------------------------ Arabic

    [Fact]
    public void In_arabic_the_role_is_the_arabic_label_and_the_screen_reads_right_to_left()
    {
        var screen = Build(selected: "nabil", text: TillText.Arabic);

        Assert.True(screen.RightToLeft);
        Assert.Equal("أمين الصندوق", screen.People[1].Role);
    }

    [Theory]
    [InlineData("Nabil B.", "NB")]
    [InlineData("samia", "S")]
    [InlineData("Émilie de la Tour", "ÉD")]
    [InlineData("  Karim   M.  ", "KM")]
    [InlineData("Gérant (synthétique)", "GS")]
    [InlineData("", "")]
    public void Initials_are_the_first_letters_of_the_first_two_words(string name, string initials) =>
        Assert.Equal(initials, SignInScreen.Initials(name));
}
