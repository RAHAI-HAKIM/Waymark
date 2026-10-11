namespace Waymark.Domain.Enums;

/// <summary>
/// Who gave a consent (D-113). Stored as TEXT with a CHECK constraint: 'subject',
/// 'legal_representative'.
///
/// <para>
/// A consent given by a legal representative says, by itself, that the person is a minor: that is
/// accepted, and it is the only trace of it the store keeps (Hakim, 10/10). No date of birth, no flag
/// on the customer.
/// </para>
/// </summary>
public enum ConsentGivenBy
{
    /// <summary>Stored as <c>subject</c>: the person the consent is about.</summary>
    Subject,

    /// <summary>Stored as <c>legal_representative</c>: a parent or guardian, for a minor.</summary>
    LegalRepresentative,
}
