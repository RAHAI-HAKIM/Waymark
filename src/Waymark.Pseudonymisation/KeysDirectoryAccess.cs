using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Waymark.Pseudonymisation;

/// <summary>
/// Who may read <c>%ProgramData%\Waymark\keys</c> (O-18, D-057).
///
/// <para>
/// <b>This ACL is the real control on both keys.</b> <c>LocalMachine</c> DPAPI unwraps for any
/// process on the machine, so whoever can read the blob holds the key. <c>ProgramData</c> grants
/// Users read and create by inheritance, so a keys directory left to inherit is readable by every
/// cashier account and the database encryption would be decorative.
/// </para>
/// <para>
/// <b>The rule is an allowlist</b>: inheritance off, and access granted to SYSTEM,
/// Administrators and the account StoreServer runs as (on a till, SYSTEM itself), to nobody
/// else, on the directory and on every file in it. A list of forbidden groups was the first
/// version; it let a cashier's own account, Guests, or an explicit grant on one key file
/// through (Phase 0 final test). Deny rules are ignored: they take access away, never give it.
/// </para>
/// <para>
/// The installer is meant to create the directory this way; until there is one, StoreServer
/// creates it at first start with <see cref="EnsureCreated"/>, and at every start it refuses to
/// run if <see cref="Problems"/> finds anything. D-013's Modify grant to the service on
/// <c>data\</c> does not collide: each directory carries its own explicit ACL.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class KeysDirectoryAccess
{
    /// <summary>
    /// Fixed names for the groups that matter, so the message reads the same on a French or an
    /// Arabic Windows, where the account names are translated.
    /// </summary>
    private static readonly (WellKnownSidType Sid, string Name)[] KnownNames =
    [
        (WellKnownSidType.WorldSid, "Everyone"),
        (WellKnownSidType.AuthenticatedUserSid, "Authenticated Users"),
        (WellKnownSidType.BuiltinUsersSid, "Users"),
        (WellKnownSidType.InteractiveSid, "Interactive"),
        (WellKnownSidType.BuiltinGuestsSid, "Guests"),
    ];

    /// <summary>
    /// Creates the directory with the explicit ACL if it does not exist. An existing directory
    /// is left exactly as it is: repairing an ACL silently would hide whoever loosened it, so
    /// that is <see cref="Problems"/>' job to report.
    /// </summary>
    public static void EnsureCreated(string keysDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);

        if (Directory.Exists(keysDirectory))
        {
            return;
        }

        var security = new DirectorySecurity();

        // Protected, and nothing inherited copied in.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var sid in Allowed())
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(keysDirectory));
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }

        new DirectoryInfo(keysDirectory).Create(security);
    }

    /// <summary>
    /// Everything wrong with the directory's ACL and its files', in words; empty when it is
    /// safe. A missing directory is a problem too: nothing protects a key that has nowhere to
    /// live.
    /// </summary>
    public static IReadOnlyList<string> Problems(string keysDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);

        if (!Directory.Exists(keysDirectory))
        {
            return [$"{keysDirectory} does not exist."];
        }

        var allowed = Allowed();
        var problems = new List<string>();
        var directory = new DirectoryInfo(keysDirectory);
        var security = directory.GetAccessControl(AccessControlSections.Access);

        if (!security.AreAccessRulesProtected)
        {
            problems.Add($"{keysDirectory} inherits permissions from its parent; inheritance must be disabled.");
        }

        problems.AddRange(Strangers(security, allowed, keysDirectory));

        foreach (var file in directory.EnumerateFiles().OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            problems.AddRange(Strangers(file.GetAccessControl(AccessControlSections.Access), allowed, file.FullName));
        }

        return problems;
    }

    /// <summary>SYSTEM, Administrators, and the account this process runs as.</summary>
    private static HashSet<SecurityIdentifier> Allowed()
    {
        using var current = WindowsIdentity.GetCurrent();
        return
        [
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            current.User!,
        ];
    }

    /// <summary>A problem for each principal outside the allowlist that an allow rule grants anything to.</summary>
    private static IEnumerable<string> Strangers(FileSystemSecurity security, HashSet<SecurityIdentifier> allowed, string path) =>
        security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .Where(sid => !allowed.Contains(sid))
            .Distinct()
            .Select(sid => $"{Name(sid)} can access {path}; only SYSTEM, Administrators and the service account may.");

    private static string Name(SecurityIdentifier sid)
    {
        foreach (var (known, name) in KnownNames)
        {
            if (sid.IsWellKnown(known))
            {
                return name;
            }
        }

        try
        {
            var account = sid.Translate(typeof(NTAccount)).Value;
            var slash = account.LastIndexOf('\\');
            return slash >= 0 ? account[(slash + 1)..] : account;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }
}
