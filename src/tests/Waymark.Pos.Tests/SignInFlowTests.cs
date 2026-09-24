using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Pos.Checkout;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// The sign-in screen's state (session A5, D-083). The silent failures: a PIN sent to the wrong
/// person because the list moved under the cashier's finger, digits left on the pad for the next
/// person to finish, an outage shown as a wrong PIN (which would teach a cashier their PIN is
/// wrong), and a pad that accepts a digit the server can never match.
/// </summary>
public sealed class SignInFlowTests
{
    private static readonly TillStaffMember Nabil = new("nabil", "Nabil B.", "Caissier", "أمين الصندوق", HasPin: true);
    private static readonly TillStaffMember Samia = new("samia", "Samia K.", "Caissière", "أمينة الصندوق", HasPin: true);
    private static readonly TillStaffMember Karim = new("karim", "Karim M.", "Responsable", "مسؤول", HasPin: false);

    /// <summary>A server that answers sign-ins from a queue and remembers what it was sent.</summary>
    private sealed class Server : ITillServer
    {
        public TillStaff? Staff { get; set; } = new([Karim, Nabil, Samia]);

        public Queue<SignInAnswer?> Answers { get; } = new();

        public List<SignInRequest> Sent { get; } = [];

        public int StaffAsked { get; private set; }

        public Task<TillStaff?> StaffAsync(CancellationToken cancellationToken = default)
        {
            StaffAsked++;
            return Task.FromResult(Staff);
        }

        public Task<SignInAnswer?> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
        }

        public Task SignOutAsync(string sessionToken, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<TillContext?> ContextAsync(string terminalId, string? staffId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TillContext?>(null);

        public Task<BoardAnswer?> BoardAsync(string staffId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BoardAnswer?>(null);

        public Task<DecisionAnswer?> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<DecisionAnswer?>(null);

        public Task<bool> HealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static SignInAnswer Answer(string outcome, string? token = null, DateTimeOffset? until = null, int? left = null) =>
        new(outcome, token, until, left);

    private static async Task<(SignInFlow Flow, Server Server)> Loaded(string? terminal = "till-1")
    {
        var server = new Server();
        var flow = new SignInFlow(server, new TillIdentity(terminal));
        await flow.LoadAsync();
        return (flow, server);
    }

    private static void Type(SignInFlow flow, string digits)
    {
        foreach (var digit in digits)
        {
            flow.Press(digit);
        }
    }

    // ----------------------------------------------------------------- the pad

    [Fact]
    public async Task Nothing_can_be_typed_before_somebody_is_chosen()
    {
        var (flow, _) = await Loaded();

        Type(flow, "1234");

        Assert.Equal(0, flow.DigitCount);
        Assert.False(flow.MaySubmit);
    }

    [Fact]
    public async Task Four_digits_make_a_pin_that_may_be_sent_three_do_not()
    {
        var (flow, _) = await Loaded();
        flow.Select("nabil");

        Type(flow, "123");
        Assert.False(flow.MaySubmit);

        flow.Press('4');
        Assert.True(flow.MaySubmit);
    }

    [Fact]
    public async Task The_pad_holds_eight_digits_and_ignores_a_ninth()
    {
        var (flow, _) = await Loaded();
        flow.Select("nabil");

        Type(flow, "123456789");

        Assert.Equal(8, flow.DigitCount);
    }

    [Theory]
    [InlineData('a')]
    [InlineData(' ')]
    [InlineData('١')] // Arabic-Indic one: char.IsDigit says yes, the server's rule says no (D-083).
    [InlineData('۱')]
    public async Task Only_ascii_digits_reach_the_pad(char typed)
    {
        var (flow, _) = await Loaded();
        flow.Select("nabil");

        flow.Press(typed);

        Assert.Equal(0, flow.DigitCount);
    }

    [Fact]
    public async Task Backspace_takes_one_digit_and_clear_takes_them_all()
    {
        var (flow, _) = await Loaded();
        flow.Select("nabil");
        Type(flow, "1234");

        flow.Backspace();
        Assert.Equal(3, flow.DigitCount);

        flow.Clear();
        Assert.Equal(0, flow.DigitCount);
    }

    [Fact]
    public async Task Choosing_somebody_else_forgets_the_digits_typed_for_the_first()
    {
        // Otherwise Samia would send the start of Nabil's PIN with the end of hers.
        var (flow, _) = await Loaded();
        flow.Select("nabil");
        Type(flow, "48");

        flow.Select("samia");

        Assert.Equal("samia", flow.SelectedId);
        Assert.Equal(0, flow.DigitCount);
    }

    [Fact]
    public async Task Somebody_with_no_pin_cannot_be_chosen_and_is_told_why()
    {
        var (flow, _) = await Loaded();

        flow.Select("karim");

        Assert.Null(flow.SelectedId);
        Assert.Equal(SignInMessageKind.NoPin, flow.Message!.Kind);
    }

    // ----------------------------------------------------------------- sending

    [Fact]
    public async Task A_right_pin_gives_the_person_and_their_token()
    {
        var (flow, server) = await Loaded();
        server.Answers.Enqueue(Answer(SignInOutcomes.SignedIn, token: "tok"));
        flow.Select("nabil");
        Type(flow, "4821");

        var person = await flow.SubmitAsync();

        Assert.Equal(new SignedInStaff("nabil", "tok"), person);
        Assert.Equal(new SignInRequest("till-1", "nabil", "4821"), Assert.Single(server.Sent));
        Assert.Null(flow.SelectedId);
        Assert.Equal(0, flow.DigitCount);
    }

    [Fact]
    public async Task A_wrong_pin_is_cleared_to_be_typed_again_and_says_what_is_left()
    {
        var (flow, server) = await Loaded();
        server.Answers.Enqueue(Answer(SignInOutcomes.WrongPin, left: 3));
        flow.Select("nabil");
        Type(flow, "0000");

        Assert.Null(await flow.SubmitAsync());

        Assert.Equal(0, flow.DigitCount);
        Assert.Equal("nabil", flow.SelectedId);
        Assert.Equal(new SignInMessage(SignInMessageKind.WrongPin, AttemptsLeft: 3), flow.Message);
    }

    [Fact]
    public async Task Locked_says_until_when_the_server_said()
    {
        var until = new DateTimeOffset(2026, 9, 24, 8, 6, 0, TimeSpan.Zero);
        var (flow, server) = await Loaded();
        server.Answers.Enqueue(Answer(SignInOutcomes.Locked, until: until));
        flow.Select("nabil");
        Type(flow, "0000");

        await flow.SubmitAsync();

        Assert.Equal(new SignInMessage(SignInMessageKind.Locked, LockedUntil: until), flow.Message);
    }

    [Fact]
    public async Task No_answer_is_an_outage_never_a_wrong_pin()
    {
        var (flow, server) = await Loaded();
        server.Answers.Enqueue(null);
        flow.Select("nabil");
        Type(flow, "4821");

        Assert.Null(await flow.SubmitAsync());

        Assert.Equal(SignInMessageKind.Offline, flow.Message!.Kind);
    }

    [Theory]
    [InlineData(SignInOutcomes.NoPin, SignInMessageKind.NoPin)]
    [InlineData(SignInOutcomes.UnknownStaff, SignInMessageKind.UnknownStaff)]
    public async Task A_list_out_of_date_is_said_and_asked_for_again(string outcome, SignInMessageKind said)
    {
        var (flow, server) = await Loaded();
        server.Answers.Enqueue(Answer(outcome));
        flow.Select("nabil");
        Type(flow, "4821");
        server.Staff = new([Samia]);

        await flow.SubmitAsync();

        Assert.Equal(said, flow.Message!.Kind);
        Assert.Null(flow.SelectedId);
        Assert.Equal(2, server.StaffAsked);
        Assert.Equal([Samia], flow.Staff);
    }

    [Fact]
    public async Task A_till_with_no_terminal_sends_nothing()
    {
        var (flow, server) = await Loaded(terminal: null);
        flow.Select("nabil");
        Type(flow, "4821");

        Assert.Null(await flow.SubmitAsync());

        Assert.Empty(server.Sent);
        Assert.Equal(SignInMessageKind.NoTerminal, flow.Message!.Kind);
        Assert.Equal(0, flow.DigitCount);
    }

    [Fact]
    public async Task Nothing_is_sent_below_four_digits()
    {
        var (flow, server) = await Loaded();
        flow.Select("nabil");
        Type(flow, "482");

        Assert.Null(await flow.SubmitAsync());
        Assert.Empty(server.Sent);
    }

    // ----------------------------------------------------------------- the list

    [Fact]
    public async Task No_list_is_an_outage_and_the_next_list_clears_it()
    {
        var server = new Server { Staff = null };
        var flow = new SignInFlow(server, new TillIdentity("till-1"));

        await flow.LoadAsync();
        Assert.Equal(SignInMessageKind.Offline, flow.Message!.Kind);
        Assert.Null(flow.Staff);

        server.Staff = new([Nabil]);
        await flow.LoadAsync();
        Assert.Null(flow.Message);
    }

    [Fact]
    public async Task Somebody_who_left_the_list_is_no_longer_chosen()
    {
        var (flow, server) = await Loaded();
        flow.Select("nabil");
        Type(flow, "48");

        server.Staff = new([Samia]);
        await flow.LoadAsync();

        Assert.Null(flow.SelectedId);
        Assert.Equal(0, flow.DigitCount);
    }
}
