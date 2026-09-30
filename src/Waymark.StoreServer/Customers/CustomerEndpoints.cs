using Waymark.Application.Commands;
using Waymark.Application.Customers;
using Waymark.Contracts.Pos;
using Waymark.Domain.Engine;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Organisation;
using Waymark.StoreServer.Security;

namespace Waymark.StoreServer.Customers;

/// <summary>
/// The customers and their tabs, as the till reaches them (B7, D-096). Who acts is the session's
/// person (D-083); a rank the capability needs is theirs, or the person's whose authorisation the
/// request cites, never compared here (CLAUDE.md §3.10). Every answer is a 200 with its outcome.
/// </summary>
public static class CustomerEndpoints
{
    public static void MapCustomers(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Customers with a number: GET, the number in the query string (decoded once, as the lookup's code is).
        app.MapGet("/api/customers", async (
            string? phone, string? terminal, HttpRequest http, TillSessions sessions, CommandExecutor executor, FindCustomersHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (Session(sessions, http) is not { } session)
            {
                return Results.Ok(new CustomerSearchAnswer(CustomerOutcomes.NotSignedIn, null));
            }

            try
            {
                var found = await executor.ExecuteAsync(handler, new FindCustomers(session.StaffId, phone ?? string.Empty, terminal), cancellationToken);
                return Results.Ok(new CustomerSearchAnswer(CustomerOutcomes.Ok, [.. found.Select(Wire)]));
            }
            catch (CustomerRefusedException refusal)
            {
                return Results.Ok(new CustomerSearchAnswer(Outcome(refusal), null, refusal.Message));
            }
        });

        app.MapPost("/api/customers", async (
            CreateCustomerRequest request, HttpRequest http, TillSessions sessions, IRecommendationBoard staff,
            CommandExecutor executor, CreateCustomerHandler handler, CancellationToken cancellationToken) =>
        {
            if (Session(sessions, http) is not { } session)
            {
                return Results.Ok(new CustomerAnswer(CustomerOutcomes.NotSignedIn, null));
            }

            if (await ActorAsync(sessions, http, session, request.Authorisation, Capability.CreateCustomer, staff, cancellationToken) is not { } actor)
            {
                return Results.Ok(new CustomerAnswer(CustomerOutcomes.NotAllowed, null, "Creating a customer is a manager's: an authorisation is asked first."));
            }

            try
            {
                var created = await executor.ExecuteAsync(handler, new CreateCustomer(actor, request.Name, request.Phone, request.TerminalId), cancellationToken);
                return Results.Ok(new CustomerAnswer(CustomerOutcomes.Ok, Wire(created)));
            }
            catch (CustomerRefusedException refusal)
            {
                return Results.Ok(new CustomerAnswer(Outcome(refusal), null, refusal.Message));
            }
        });

        app.MapGet("/api/customers/{customerId}/tab", async (
            string customerId, string? terminal, HttpRequest http, TillSessions sessions, CommandExecutor executor, OpenTabHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (Session(sessions, http) is not { } session)
            {
                return Results.Ok(Refused(CustomerOutcomes.NotSignedIn, null));
            }

            try
            {
                return Results.Ok(Wire(await executor.ExecuteAsync(handler, new OpenTab(session.StaffId, customerId, terminal), cancellationToken)));
            }
            catch (CustomerRefusedException refusal)
            {
                return Results.Ok(Refused(Outcome(refusal), refusal.Message));
            }
        });

        app.MapPost("/api/customers/{customerId}/repayments", async (
            string customerId, RepaymentRequest request, HttpRequest http, TillSessions sessions,
            CommandExecutor executor, RepayTabHandler handler, CancellationToken cancellationToken) =>
        {
            if (Session(sessions, http) is not { } session || !string.Equals(session.TerminalId, request.TerminalId, StringComparison.Ordinal))
            {
                return Results.Ok(Refused(CustomerOutcomes.NotSignedIn, null));
            }

            // What cannot be read is zero, which the tab's rule refuses: never repaid as some other amount.
            var amount = WireText.TryHundredths(request.Amount, out var minor) ? minor : 0;
            try
            {
                return Results.Ok(Wire(await executor.ExecuteAsync(
                    handler, new RepayTab(session.TerminalId, session.StaffId, customerId, amount, request.ReasonCode), cancellationToken)));
            }
            catch (CustomerRefusedException refusal)
            {
                return Results.Ok(Refused(Outcome(refusal), refusal.Message));
            }
        });

        app.MapPost("/api/customers/{customerId}/limit", async (
            string customerId, LimitRequest request, HttpRequest http, TillSessions sessions, IRecommendationBoard staff,
            CommandExecutor executor, ChangeCreditLimitHandler handler, CancellationToken cancellationToken) =>
        {
            if (Session(sessions, http) is not { } session)
            {
                return Results.Ok(Refused(CustomerOutcomes.NotSignedIn, null));
            }

            if (await ActorAsync(sessions, http, session, request.Authorisation, Capability.ManageCredit, staff, cancellationToken) is not { } actor)
            {
                return Results.Ok(Refused(CustomerOutcomes.NotAllowed, "A tab's limit is the owner's: an authorisation is asked first."));
            }

            LimitChange? change = request.Action switch
            {
                LimitActions.Set => LimitChange.Set,
                LimitActions.Freeze => LimitChange.Freeze,
                LimitActions.Unfreeze => LimitChange.Unfreeze,
                _ => null,
            };
            // A limit that cannot be read is refused, never taken as no limit: that would close the tab.
            long? limit = null;
            if (request.Limit is { } typed)
            {
                limit = WireText.TryHundredths(typed, out var read) ? read : null;
            }

            if (change is null || (request.Limit is not null && limit is null))
            {
                return Results.Ok(Refused(CustomerOutcomes.Refused, "Not a limit: set with an amount, or none to close the tab; freeze; unfreeze."));
            }

            try
            {
                return Results.Ok(Wire(await executor.ExecuteAsync(
                    handler, new ChangeCreditLimit(actor, customerId, change.Value, limit, request.TerminalId), cancellationToken)));
            }
            catch (CustomerRefusedException refusal)
            {
                return Results.Ok(Refused(Outcome(refusal), refusal.Message));
            }
        });
    }

    private static SignedInTill? Session(TillSessions sessions, HttpRequest http) =>
        sessions.Resolve(http.Headers[TillSessionHeader.Name].ToString());

    /// <summary>
    /// Who does it: the seller when their own rank allows it, else the person whose authorisation for
    /// <paramref name="capability"/> this session was given (B4's). Null: nobody may.
    /// </summary>
    private static async Task<string?> ActorAsync(
        TillSessions sessions, HttpRequest http, SignedInTill session, string? authorisation, Capability capability,
        IRecommendationBoard staff, CancellationToken cancellationToken) =>
        Actor(
            session.StaffId,
            (await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank,
            capability,
            () => sessions.AuthorisedBy(http.Headers[TillSessionHeader.Name].ToString(), authorisation, capability));

    /// <summary>
    /// The rule <see cref="ActorAsync"/> applies, apart so it is tested without a server: the seller
    /// when their rank reaches the capability (<see cref="StaffPermissions.May"/>, never compared
    /// here), else whoever the cited authorisation names, else nobody.
    /// </summary>
    public static string? Actor(string sellerId, long? sellerRank, Capability capability, Func<string?> authorisedBy)
    {
        ArgumentNullException.ThrowIfNull(authorisedBy);
        return StaffPermissions.May(sellerRank, capability) ? sellerId : authorisedBy();
    }

    private static string Outcome(CustomerRefusedException refusal) => refusal.Refusal switch
    {
        CustomerRefusal.ModuleOff => CustomerOutcomes.ModuleOff,
        CustomerRefusal.NotFound => CustomerOutcomes.NotFound,
        _ => CustomerOutcomes.Refused,
    };

    private static CustomerSummaryWire Wire(CustomerSummary customer) => new(customer.CustomerId, customer.Name, customer.Phone);

    public static TabAnswer Wire(TabView tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return new TabAnswer(
            CustomerOutcomes.Ok,
            Wire(tab.Customer),
            WireText.Figure(tab.Balance),
            tab.Limit is { } limit ? WireText.Figure(limit) : null,
            tab.Available is { } available ? WireText.Figure(available) : null,
            tab.Frozen,
            tab.OldestUnpaid,
            tab.OverdueDays,
            tab.Balance.Currency.Code,
            [.. tab.Movements.Select(movement => new TabMovementWire(Kind(movement), WireText.Figure(movement.Amount), movement.OccurredAt))]);
    }

    private static string Kind(ReceivableMovement movement) => movement.MovementType switch
    {
        Domain.Enums.ReceivableMovementType.Charge => "charge",
        Domain.Enums.ReceivableMovementType.Payment => "payment",
        Domain.Enums.ReceivableMovementType.Adjustment => "adjustment",
        _ => "write_off",
    };

    private static TabAnswer Refused(string outcome, string? reason) =>
        new(outcome, null, null, null, null, false, null, null, null, null, reason);
}
