using Waymark.Application.Commands;
using Waymark.Domain.Organisation;

namespace Waymark.Application.Organisation;

/// <summary>A new PIN for one staff member (session A5, D-083): StoreServer's <c>--set-pin=</c> switch.</summary>
/// <param name="StaffId">Whose PIN.</param>
/// <param name="Pin">The PIN as typed, twice and matching; checked here again, whoever calls.</param>
public sealed record SetStaffPin(string StaffId, string Pin) : ICommand<NoResult>;

/// <summary>The PIN cannot be set, for a reason the person setting it is told. Nothing is written.</summary>
public sealed class PinRefusedException(string reason) : Exception(reason);

/// <summary>
/// Hashes a PIN and stages it on the staff row; the executor commits it (D-050).
///
/// <para>
/// <b>Only the hash is staged.</b> The PIN itself is never written, logged or put in an
/// exception message: a refusal says what was wrong with it, not what it was.
/// </para>
/// </summary>
public sealed class SetStaffPinHandler(
    IStaffCredentials credentials,
    IPinHasher hasher,
    TimeProvider clock) : ICommandHandler<SetStaffPin, NoResult>
{
    public async Task<NoResult> HandleAsync(
        SetStaffPin command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.StaffId);

        if (!StaffPin.IsWellFormed(command.Pin))
        {
            throw new PinRefusedException(
                $"A PIN is {StaffPin.MinimumLength} to {StaffPin.MaximumLength} digits, 0 to 9 only.");
        }

        // Hashed before anything is looked up only because the hash is what is staged; nothing is
        // written unless the person exists, and a refusal below discards it with the unit of work.
        var hash = hasher.Hash(command.Pin);

        if (!await credentials.StagePinHashAsync(command.StaffId, hash, clock.GetUtcNow(), cancellationToken))
        {
            throw new PinRefusedException(
                $"{command.StaffId} cannot sign in at this store: not active staff here, or their role is retired.");
        }

        return NoResult.Value;
    }
}
