using System.Buffers.Binary;

namespace Waymark.Pseudonymisation;

/// <summary>
/// Crockford base32, for the 128-bit values this project produces.
///
/// <para>
/// The alphabet omits <c>I</c>, <c>L</c>, <c>O</c> and <c>U</c> — the first
/// three because they are misread as <c>1</c>, <c>1</c> and <c>0</c>, the last
/// so the encoding cannot spell an obscenity. That matters here because a
/// pseudonym is read aloud and typed by hand during a data-subject request, and
/// a transcription error at that moment looks exactly like "no such subject".
/// </para>
/// <para>
/// 26 characters for 128 bits, so a pseudonym is the same shape as a ULID and
/// the cloud's columns stay uniform (D-039). 26 × 5 = 130 bits of room, so the
/// top two bits are always zero and the first character is always in
/// <c>0</c>–<c>3</c> — the same convention ULID uses.
/// </para>
/// <para>
/// Encoding only. There is nothing here that decodes: a pseudonym is never
/// turned back into anything, and a decoder would be a function whose only
/// purpose is to look like one.
/// </para>
/// </summary>
internal static class CrockfordBase32
{
    /// <summary>Crockford's alphabet. Position is the value.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>How many characters 128 bits occupies.</summary>
    internal const int Length = 26;

    /// <summary>
    /// Encodes exactly 16 bytes, big-endian, as 26 characters.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is not 16 bytes. Not a defensive nicety: a
    /// shorter span would silently encode a smaller number with leading zeros,
    /// and every pseudonym would still look perfectly well-formed.
    /// </exception>
    internal static string Encode(ReadOnlySpan<byte> value)
    {
        if (value.Length != 16)
        {
            throw new ArgumentException(
                $"128 bits is 16 bytes; got {value.Length}.", nameof(value));
        }

        var bits = ((UInt128)BinaryPrimitives.ReadUInt64BigEndian(value[..8]) << 64)
                   | BinaryPrimitives.ReadUInt64BigEndian(value[8..]);

        return string.Create(Length, bits, static (destination, source) =>
        {
            // Least significant character first, so the shift is the loop.
            for (var index = destination.Length - 1; index >= 0; index--)
            {
                destination[index] = Alphabet[(int)(source & 0x1F)];
                source >>= 5;
            }
        });
    }
}
