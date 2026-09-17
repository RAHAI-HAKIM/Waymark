using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// The keys directory's ACL (O-18): the real control on both keys, since machine-scope DPAPI
/// unwraps for any local process. Windows only; elsewhere these assert nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class KeysDirectoryAccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "waymark-acl", Guid.NewGuid().ToString("N"));

    private string Keys => Path.Combine(_root, "keys");

    [Fact]
    public void A_directory_it_creates_is_locked_down_and_passes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        KeysDirectoryAccess.EnsureCreated(Keys);

        var security = new DirectoryInfo(Keys).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected, "The created directory still inherits from its parent.");
        Assert.All(Sids(security), sid => Assert.Contains(sid, Allowed()));
        Assert.Empty(KeysDirectoryAccess.Problems(Keys));
    }

    [Fact]
    public void A_directory_that_inherits_its_parents_permissions_is_refused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // What a plain CreateDirectory under ProgramData produces.
        Directory.CreateDirectory(Keys);

        Assert.Contains(KeysDirectoryAccess.Problems(Keys), p => p.Contains("inherits permissions", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WellKnownSidType.BuiltinUsersSid, "Users can access")]
    [InlineData(WellKnownSidType.AuthenticatedUserSid, "Authenticated Users can access")]
    [InlineData(WellKnownSidType.WorldSid, "Everyone can access")]
    [InlineData(WellKnownSidType.InteractiveSid, "Interactive can access")]
    public void A_locked_down_directory_that_a_broad_group_can_read_is_refused(WellKnownSidType group, string expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        KeysDirectoryAccess.EnsureCreated(Keys);
        Grant(Keys, group, FileSystemRights.ReadData);

        var problems = KeysDirectoryAccess.Problems(Keys);

        Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("inherits", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WellKnownSidType.BuiltinGuestsSid)]
    [InlineData(WellKnownSidType.BuiltinPowerUsersSid)]
    [InlineData(WellKnownSidType.LocalServiceSid)]
    [InlineData(WellKnownSidType.NetworkServiceSid)]
    public void Any_principal_beyond_system_administrators_and_the_service_is_refused(WellKnownSidType principal)
    {
        // The rule is an allowlist, not a list of the usual suspects: a cashier's own account,
        // or any group nobody thought to name, reads a key just as well as Users does.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        KeysDirectoryAccess.EnsureCreated(Keys);
        Grant(Keys, principal, FileSystemRights.ReadData);

        Assert.Contains(KeysDirectoryAccess.Problems(Keys), p => p.Contains("can access", StringComparison.Ordinal));
    }

    [Fact]
    public void A_key_file_with_its_own_looser_permissions_is_refused()
    {
        // The directory can be locked down while one file inside it carries an explicit grant.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        KeysDirectoryAccess.EnsureCreated(Keys);
        var key = Path.Combine(Keys, "store.key");
        File.WriteAllBytes(key, [1, 2, 3]);
        Assert.Empty(KeysDirectoryAccess.Problems(Keys));

        var file = new FileInfo(key);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(security);

        Assert.Contains(KeysDirectoryAccess.Problems(Keys), p => p.Contains("store.key", StringComparison.Ordinal));
    }

    [Fact]
    public void A_deny_rule_is_not_a_grant()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        KeysDirectoryAccess.EnsureCreated(Keys);
        var directory = new DirectoryInfo(Keys);
        var security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null), FileSystemRights.Read, AccessControlType.Deny));
        directory.SetAccessControl(security);

        Assert.Empty(KeysDirectoryAccess.Problems(Keys));
    }

    [Fact]
    public void An_existing_directory_is_never_repaired_only_reported()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Fixing it quietly would hide whoever loosened it.
        Directory.CreateDirectory(Keys);
        KeysDirectoryAccess.EnsureCreated(Keys);

        Assert.False(new DirectoryInfo(Keys).GetAccessControl().AreAccessRulesProtected);
        Assert.NotEmpty(KeysDirectoryAccess.Problems(Keys));
    }

    [Fact]
    public void A_missing_directory_is_a_problem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Contains(KeysDirectoryAccess.Problems(Keys), p => p.Contains("does not exist", StringComparison.Ordinal));
    }

    [SupportedOSPlatform("windows")]
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

    [SupportedOSPlatform("windows")]
    private static IEnumerable<SecurityIdentifier> Sids(DirectorySecurity security) =>
        security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => (SecurityIdentifier)rule.IdentityReference);

    [SupportedOSPlatform("windows")]
    private static void Grant(string path, WellKnownSidType group, FileSystemRights rights)
    {
        var directory = new DirectoryInfo(path);
        var security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(group, null), rights, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
