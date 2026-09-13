namespace Waymark.Domain.Privacy;

/// <summary>
/// The tier 1 → tier 2 boundary, as a port. Turns a direct identifier into the
/// only name the cloud is ever allowed to know it by.
///
/// <para>
/// Declared in Domain and implemented in <c>Waymark.Pseudonymisation</c>, which
/// is the only project that holds the tenant key or computes a pseudonym
/// (CLAUDE.md §3.5). Everyone else names this interface and receives the
/// implementation through dependency injection, so nobody else acquires a
/// reference to the project that holds the key.
/// </para>
/// <para>
/// <b>Holding this port is a capability, not a convenience.</b> Anything that
/// can call <see cref="PseudonymFor"/> can turn a customer id into the pseudonym
/// the cloud stores, which is half of re-identification. Inject it into as
/// little as possible. <c>Waymark.Sync</c> must never hold it — pseudonymisation happens
/// <i>before</i> the outbox and what lands there is already tier-2 shaped
/// (CLAUDE.md §4), so sync has no honest use for it, and an architecture test
/// fails if sync acquires one.
/// </para>
/// <para>
/// The key-check value is deliberately <b>not</b> here. Restore integrity needs
/// it, and needing it is no reason to hand out the ability to pseudonymise —
/// see <see cref="ITenantKeyCheck"/>.
/// </para>
/// </summary>
public interface IPseudonymiser
{
    /// <summary>
    /// The pseudonym for one subject, under its own domain-separation prefix.
    ///
    /// <para>
    /// Deterministic: the same subject under the same tenant key always gives
    /// the same value, which is what lets the cloud accumulate a history and
    /// what makes reverse lookup possible without a mapping table — a store has
    /// a few thousand customers, so a data-subject request computes each
    /// pseudonym and matches, in milliseconds (D-039).
    /// </para>
    /// </summary>
    /// <param name="domain">Which population <paramref name="subjectId"/> is from.</param>
    /// <param name="subjectId">The direct identifier, as stored locally.</param>
    /// <returns>26 characters of Crockford base32, the same shape as a ULID.</returns>
    Pseudonym PseudonymFor(SubjectDomain domain, string subjectId);
}

/// <summary>
/// Proof that the key in use is the key the cloud has seen before.
///
/// <para>
/// Its own port rather than a member of <see cref="IPseudonymiser"/>, on
/// least-privilege grounds. Restore integrity is a sync-time concern, and the
/// component that checks it would otherwise be handed the ability to compute
/// every customer's pseudonym in order to compare one constant.
/// </para>
/// <para>
/// The check exists because a restore performed with the wrong key fails
/// silently and expensively: every customer quietly acquires a second
/// pseudonymous identity, and the cloud's history splits in two with nothing to
/// indicate it. StoreServer recomputes this after a restore and refuses to sync
/// on a mismatch, forcing an explicit acknowledgement that a new pseudonym epoch
/// has begun (D-042, DPIA §5.4).
/// </para>
/// </summary>
public interface ITenantKeyCheck
{
    /// <summary>
    /// A truncated keyed hash of a fixed string under the tenant key, held in
    /// the cloud tenant record.
    ///
    /// <para>
    /// <b>Safe to publish, and only because the key has full entropy.</b> This
    /// is a known-plaintext/ciphertext pair: an attacker holding it can test
    /// candidate keys offline at no cost. Against 32 random bytes that is
    /// worthless, which is the second independent reason D-042 forbids deriving
    /// the tenant key from a passphrase — the first being that Waymark already
    /// holds backups and cloud pseudonyms.
    /// </para>
    /// </summary>
    string CheckValue { get; }
}
