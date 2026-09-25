using System.Security.Cryptography;
using Waymark.Domain.Organisation;

namespace Waymark.StoreServer.Security;

/// <summary>
/// <b>Session A5.</b> Staff PINs hashed with Argon2id (D-083), using the
/// <c>Konscious.Security.Cryptography.Argon2</c> package.
///
/// <para><b>What to store</b> — the PHC string format, so the parameters travel with the hash
/// and can be raised later without breaking a PIN set today:</para>
/// <code>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt, base64&gt;$&lt;hash, base64&gt;</code>
/// <para>
/// <see cref="MemoryKib"/>, <see cref="Iterations"/> and <see cref="Parallelism"/> are OWASP's
/// minimum for Argon2id; about a tenth of a second on a slow till, paid once per sign-in. The salt
/// is <see cref="SaltBytes"/> random bytes from <c>RandomNumberGenerator</c>, the hash
/// <see cref="HashBytes"/> bytes. Use unpadded standard base64, as PHC strings do.
/// </para>
/// <para><b>The four things the tests hold you to:</b></para>
/// <list type="number">
///   <item><description><b>Refuse first, on purpose.</b> <see cref="Verify"/> asks
///   <see cref="StaffPin.IsUsable"/> about the stored value and <see cref="StaffPin.IsWellFormed"/>
///   about the PIN before any hashing, so the synthetic sentinel is refused because it is the
///   sentinel, not because it happens not to parse (D-077).</description></item>
///   <item><description><b>Read the parameters from the stored string</b>, never from the
///   constants: a PIN hashed with other parameters must still verify.</description></item>
///   <item><description><b>Compare in constant time</b>, with
///   <c>CryptographicOperations.FixedTimeEquals</c>, never <c>SequenceEqual</c> or <c>==</c>:
///   those stop at the first differing byte, and the time they take says how many were
///   right.</description></item>
///   <item><description><b>Anything malformed is false, never an exception</b>: a row somebody
///   edited by hand refuses that person and nobody else.</description></item>
/// </list>
/// </summary>
public sealed class Argon2PinHasher : IPinHasher
{
    public const int MemoryKib = 19_456;
    public const int Iterations = 2;
    public const int Parallelism = 1;
    public const int SaltBytes = 16;
    public const int HashBytes = 32;

    /// <summary>
    /// The most a stored row may ask for (F-23): 1 GiB, 10 passes, 4 lanes. The parameters are read
    /// from the row, so a row edited to <c>m=4000000</c> would otherwise make one check take
    /// gigabytes and minutes, and sign-ins are one at a time (D-083): every till would wait. Far
    /// above what <see cref="Hash"/> writes, so raising the constants later still verifies.
    /// </summary>
    public const int MaximumMemoryKib = 1_048_576;
    public const int MaximumIterations = 10;
    public const int MaximumParallelism = 4;

    /// <summary>The PHC prefix this scheme writes, and the only one <see cref="Verify"/> accepts.</summary>
    private const string Algorithm = "argon2id";
    private const string Version = "v=19";

    public string Hash(string pin)
    {
        if (!StaffPin.IsWellFormed(pin))
        {
            throw new ArgumentException(
                $"A PIN is {StaffPin.MinimumLength} to {StaffPin.MaximumLength} digits, 0 to 9 only.", nameof(pin));
        }

        // A fresh salt every time: two people who chose 1234 get different rows.
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(pin, salt, MemoryKib, Iterations, Parallelism, HashBytes);

        // $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>: everything Verify needs to redo the work.
        return $"${Algorithm}${Version}$m={MemoryKib},t={Iterations},p={Parallelism}${Unpadded(salt)}${Unpadded(hash)}";
    }

    public bool Verify(string pin, string? storedHash)
    {
        // 1. Refuse first, on purpose: the sentinel because it is the sentinel (D-077), and a PIN
        //    that could never have been set without spending an Argon2 run on it.
        if (!StaffPin.IsUsable(storedHash) || !StaffPin.IsWellFormed(pin))
        {
            return false;
        }

        // 2. Read salt, parameters and hash back out of the stored string. Anything that does
        //    not parse is a broken row: false, never an exception.
        if (!TryParse(storedHash!, out var memoryKib, out var iterations, out var parallelism, out var salt, out var expected))
        {
            return false;
        }

        // 3. Hash the offered PIN again with the stored salt and parameters, not the constants.
        var actual = Compute(pin, salt, memoryKib, iterations, parallelism, expected.Length);

        // 4. Constant time: the comparison takes as long whichever byte differs.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Compute(string pin, byte[] salt, int memoryKib, int iterations, int parallelism, int length)
    {
        using var argon = new Konscious.Security.Cryptography.Argon2id(System.Text.Encoding.UTF8.GetBytes(pin))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }

    /// <summary>
    /// Splits <c>$argon2id$v=19$m=…,t=…,p=…$salt$hash</c>. False for anything else: another
    /// algorithm, a missing part, a parameter that is not a positive number, or bad base64.
    /// </summary>
    private static bool TryParse(
        string stored, out int memoryKib, out int iterations, out int parallelism, out byte[] salt, out byte[] hash)
    {
        memoryKib = iterations = parallelism = 0;
        salt = hash = [];

        // "$a$b$c$d$e" splits into an empty first part and five more.
        var parts = stored.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != Algorithm || parts[2] != Version)
        {
            return false;
        }

        foreach (var setting in parts[3].Split(','))
        {
            var pair = setting.Split('=');
            if (pair.Length != 2
                || !int.TryParse(pair[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
                || value <= 0)
            {
                return false;
            }

            switch (pair[0])
            {
                case "m": memoryKib = value; break;
                case "t": iterations = value; break;
                case "p": parallelism = value; break;
                default: return false;
            }
        }

        // A salt shorter than 8 bytes or a hash shorter than 16 is not one this scheme makes.
        // Above a ceiling is malformed like anything else: it refuses this person, never every till.
        return memoryKib > 0 && iterations > 0 && parallelism > 0
            && memoryKib <= MaximumMemoryKib && iterations <= MaximumIterations && parallelism <= MaximumParallelism
            && TryUnpadded(parts[4], out salt) && salt.Length >= 8
            && TryUnpadded(parts[5], out hash) && hash.Length >= 16;
    }

    /// <summary>Standard base64 without its '=' padding, as PHC strings write it.</summary>
    private static string Unpadded(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryUnpadded(string text, out byte[] bytes)
    {
        var padded = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        bytes = new byte[padded.Length];
        if (Convert.TryFromBase64String(padded, bytes, out var written))
        {
            bytes = bytes[..written];
            return true;
        }

        bytes = [];
        return false;
    }
}
