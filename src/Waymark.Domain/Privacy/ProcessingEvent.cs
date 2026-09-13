using Waymark.Domain.Enums;

namespace Waymark.Domain.Privacy;

/// <summary>
/// One row of the logbook Loi 25-11 article 41 bis 3 requires, as a call site
/// describes it.
///
/// <para>
/// This is the shape of the promise. D-045 settled the columns from the statute
/// rather than from convenience, so every field here answers a clause of it:
/// the article requires collection, modification, consultation, transmission and
/// deletion to be traced with their reasons, dates, times, and the identity of
/// users and recipients where available.
/// </para>
/// <para>
/// <b>Three of the twelve columns are deliberately absent.</b> A call site does
/// not supply <c>log_id</c> or <c>occurred_at</c>: the writer mints the id from
/// <see cref="Waymark.Domain.Ids.IIdGenerator"/> and stamps the time from the
/// injected <see cref="TimeProvider"/>. A caller that could set either could
/// backdate a row or collide an id, and the log is evidence — evidence whose
/// timestamps the party being audited chose is worth less than evidence whose
/// timestamps it did not.
/// </para>
/// <para>
/// Nor does it supply <c>store_id</c>, which the writer takes from
/// <see cref="ICurrentStore"/>. <c>processing_log</c> is store-scoped behind a
/// global query filter that fails closed (CLAUDE.md §3.3), so a row written
/// under another store's id would be invisible to the process that wrote it —
/// an audit gap that looks like nothing at all. It is also the cheapest route
/// to cross-tenant mislabelling, which is DPIA risk R9.
/// </para>
/// </summary>
/// <param name="Operation">
/// Which of the article's named operations happened. There is a CHECK behind
/// this one, unlike <paramref name="Purpose"/>.
/// </param>
/// <param name="SubjectType">
/// Customer or staff. Required even when <paramref name="Subject"/> is
/// undefined, because the column is <c>NOT NULL</c> while <c>subject_id</c> is
/// not — a retention sweep over customer rows has no subject but is still about
/// customers.
/// </param>
/// <param name="Subject">
/// The person, named the only way this table is allowed to name one.
///
/// <para>
/// <b>A <see cref="Pseudonym"/> and never a string.</b> The type cannot be
/// constructed outside <c>Waymark.Pseudonymisation</c>, so a call site that
/// wants to log has to cross that boundary to obtain the value — which is
/// D-045's "enforced by the type, not by the helper's body", and what CLAUDE.md
/// §4's "structural rather than remembered" means when it reaches code.
/// <c>ProcessingLogContractTests</c> fails if a string-taking overload ever
/// appears.
/// </para>
/// <para>
/// <c>default</c> writes no subject at all, for an operation that has none — a
/// retention purge, a system task. That is the only way to get a null
/// <c>subject_id</c>, and it cannot be confused with a subject whose pseudonym
/// failed to compute, because computing one cannot fail silently.
/// </para>
/// </param>
/// <param name="ActorType">Staff, system or engine. CHECK-constrained.</param>
/// <param name="ActorId">
/// Which staff member, where there was one. The article asks for the identity of
/// users "where available"; a nightly job has none.
/// </param>
/// <param name="Purpose">
/// The article's "reason". A closed enum with <b>no CHECK behind it</b>, so
/// adding a purpose needs no migration: it is vocabulary, not structure. The
/// converter is the validation and it refuses in both directions (D-045).
///
/// <para>
/// The cost of amending a CHECK here is the rebuild's data copy, not the
/// trigger loss D-022 first found — §3.7's procedure closed that, since every
/// index, CHECK and FK is declared in the model and triggers are re-applied by
/// <c>MigrateAndApplyTriggers()</c>. But <c>processing_log</c> takes roughly
/// 1 500 rows a day, so a rebuild on a two-year-old store is an operational
/// pause rather than a routine update.
/// </para>
/// <para>
/// The accepted cost is that the database does not enforce this vocabulary.
/// Anything writing outside EF — raw SQL, a repair script, the engine — can put
/// an unknown purpose into an audit table, and only a read through the converter
/// would notice.
/// </para>
/// </param>
/// <param name="LegalBasis">
/// What made the operation lawful. Its own enum rather than
/// <see cref="Enums.LegalBasis"/>, which cannot express <c>vital_interest</c>
/// without widening a CHECK on <c>customers</c>.
/// </param>
/// <param name="SourceModule">
/// Which part of the system acted. Required: an entry nobody can trace back to
/// a code path is not evidence of anything.
/// </param>
/// <param name="Recipient">
/// Who received the data, for a disclosure or a transmission. The article asks
/// for recipients where available; for a consultation there is none.
/// </param>
/// <param name="TerminalId">
/// Which till, where it happened at one. Caller-supplied, unlike the store:
/// there is no ambient terminal to read it from, and naming the wrong till
/// mislabels a row without crossing a tenancy boundary.
/// </param>
public readonly record struct ProcessingEvent(
    Operation Operation,
    ProcessingLogEntrySubjectType SubjectType,
    Pseudonym Subject,
    ActorType ActorType,
    string? ActorId,
    ProcessingPurpose Purpose,
    ProcessingLegalBasis LegalBasis,
    string SourceModule,
    string? Recipient = null,
    string? TerminalId = null);
