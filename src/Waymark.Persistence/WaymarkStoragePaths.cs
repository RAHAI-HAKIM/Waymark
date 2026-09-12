namespace Waymark.Persistence;

/// <summary>
/// Where the store's files live on disk (decisions.md D-013).
///
/// <para>
/// Path composition only — no configuration, no dependency injection, nothing
/// that would give <c>Waymark.Persistence</c> a reason to know how a host is
/// wired. The host reads the setting and passes a directory in; this decides
/// what sits inside it.
/// </para>
/// <para>
/// The key files are named here but never read here. Only
/// <c>Waymark.Pseudonymisation</c> unwraps the tenant key and only the host
/// unwraps the database key (CLAUDE.md §3.5, decisions.md D-042); a helper in
/// this class returning key <i>material</i> would be the first step to it
/// appearing elsewhere. A directory is not material.
/// </para>
/// </summary>
public static class WaymarkStoragePaths
{
    /// <summary>Configuration key the host reads to override the default.</summary>
    public const string DataDirectorySetting = "Waymark:Storage:DataDirectory";

    /// <summary>File name of the operational database.</summary>
    public const string StoreDatabaseFile = "waymark-store.db";

    /// <summary>
    /// <c>%ProgramData%\Waymark</c> on Windows. Siblings of <c>data</c> under
    /// it are <c>keys</c>, <c>pos-cache</c>, <c>backups</c>, <c>logs</c>
    /// and <c>config</c> (D-013).
    /// </summary>
    public static string DefaultRootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Waymark");

    /// <summary>
    /// <c>%ProgramData%\Waymark\data</c>, the directory holding
    /// <c>waymark-store.db</c>.
    ///
    /// <para>
    /// Machine-scoped rather than user-scoped, because StoreServer runs as a
    /// service and several cashiers share the till. Not beside the executable,
    /// because a self-contained publish is a folder replaced wholesale on
    /// upgrade and data inside it dies with the update. Not under the user
    /// profile, which is often OneDrive-redirected, and OneDrive syncing an
    /// open SQLite file corrupts it. The reasoning is D-013.
    /// </para>
    /// <para>
    /// On Linux this resolves under <c>/usr/share</c>, which is root-owned, so
    /// development on a non-Windows machine must set
    /// <see cref="DataDirectorySetting"/> rather than rely on the default.
    /// </para>
    /// </summary>
    public static string DefaultDataDirectory => Path.Combine(DefaultRootDirectory, "data");

    /// <summary>
    /// Where the wrapped key blobs live — <c>%ProgramData%\Waymark\keys</c>.
    ///
    /// <para>
    /// Its own directory rather than a file beside the database, because the
    /// cloud backup set is an allowlist of directories: a key cannot be swept
    /// into a backup by someone adding a pattern, only by someone adding this
    /// directory (decisions.md D-042). That restores the file-level exclusion
    /// D-039 gave up when <c>waymark-identity.db</c> went away, and narrows the
    /// residual risk to code putting a key into a payload — which is what the
    /// outbox test covers.
    /// </para>
    /// <para>
    /// Both blobs are DPAPI-wrapped at <c>LocalMachine</c> scope, so the test
    /// that asserts a key never reaches a payload has to assert on the wrapped
    /// blob as well as the raw bytes, or it passes while the blob ships.
    /// </para>
    /// </summary>
    public static string DefaultKeysDirectory => Path.Combine(DefaultRootDirectory, "keys");

    /// <summary>Creates the keys directory if it is absent, and returns it.</summary>
    public static string EnsureKeysDirectory(string keysDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);
        Directory.CreateDirectory(keysDirectory);
        return keysDirectory;
    }

    /// <summary>Full path to the operational database inside a data directory.</summary>
    public static string StoreDatabase(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return Path.Combine(dataDirectory, StoreDatabaseFile);
    }

    /// <summary>
    /// Creates the data directory if it is not there, and returns it.
    ///
    /// <para>
    /// SQLite will not create a missing directory, and the error it gives —
    /// "unable to open database file" — does not say that is the problem. WAL
    /// also needs the directory writable, not merely the file: SQLite creates
    /// and deletes the <c>-wal</c> and <c>-shm</c> siblings beside the
    /// database.
    /// </para>
    /// </summary>
    public static string EnsureDataDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        return dataDirectory;
    }
}
