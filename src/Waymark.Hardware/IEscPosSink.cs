using System.Text;
using Waymark.Domain.Hardware;

namespace Waymark.Hardware;

/// <summary>
/// Where encoded bytes go. A file in development, a serial or USB port on a
/// till.
///
/// <para>
/// This is the only seam between the fake and the real printer. Everything
/// above it — the document, the encoder, the drawer kick — is the same code in
/// both, which is the point of having a fake at all.
/// </para>
/// </summary>
public interface IEscPosSink
{
    Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
}

/// <summary>
/// The development sink: appends the raw byte stream to a <c>.escpos</c> file,
/// and a readable rendering beside it.
///
/// <para>
/// <b>Both files, and the raw one is the important half.</b> A fake that wrote
/// only a readable transcript would let the encoder be wrong in every way that
/// matters — a missed initialise, a bad barcode length prefix, an alignment
/// never reset — and the first real print would find all of it at once. The
/// <c>.escpos</c> file is byte-for-byte what the printer would have received, so
/// it can be diffed, hex-dumped, or piped to a real device.
/// </para>
/// <para>
/// The <c>.txt</c> beside it exists because a hex dump does not tell you the
/// total was misaligned. It renders printable characters and names the control
/// sequences, so a human can see the paper.
/// </para>
/// </summary>
public sealed class FileEscPosSink : IEscPosSink
{
    private readonly string _rawPath;
    private readonly string _readablePath;

    /// <param name="path">
    /// The <c>.escpos</c> file. The readable rendering goes beside it with a
    /// <c>.txt</c> extension.
    /// </param>
    public FileEscPosSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _rawPath = path;
        _readablePath = Path.ChangeExtension(path, ".txt");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Where the raw stream is being written.</summary>
    public string RawPath => _rawPath;

    /// <summary>Where the readable rendering is being written.</summary>
    public string ReadablePath => _readablePath;

    public async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            // Append: a till prints all day, and a sink that truncated would
            // leave only the last receipt — which is never the one being
            // investigated.
            await using (var raw = new FileStream(
                _rawPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                await raw.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            await File.AppendAllTextAsync(
                _readablePath, Render(bytes.Span), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException error)
        {
            throw new ReceiptPrinterException($"Could not write to {_rawPath}.", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new ReceiptPrinterException($"Not allowed to write to {_rawPath}.", error);
        }
    }

    /// <summary>
    /// The byte stream as something a person can read: printable characters as
    /// themselves, control sequences named in angle brackets.
    /// </summary>
    internal static string Render(ReadOnlySpan<byte> bytes)
    {
        var output = new StringBuilder(bytes.Length);

        for (var index = 0; index < bytes.Length; index++)
        {
            var current = bytes[index];

            if (current == EscPos.LineFeed)
            {
                output.Append('\n');
                continue;
            }

            if (current >= 0x20 && current < 0x7F)
            {
                output.Append((char)current);
                continue;
            }

            // Name the command and skip its parameters, so the rendering shows
            // the paper rather than a field of escape codes.
            var (name, length) = Describe(bytes[index..]);
            output.Append('<').Append(name).Append('>');
            index += length - 1;
        }

        return output.ToString();
    }

    private static (string Name, int Length) Describe(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x1B, 0x40, ..] => ("init", 2),
        [0x1B, 0x61, 0, ..] => ("left", 3),
        [0x1B, 0x61, 1, ..] => ("centre", 3),
        [0x1B, 0x61, 2, ..] => ("right", 3),
        [0x1B, 0x45, 1, ..] => ("bold on", 3),
        [0x1B, 0x45, 0, ..] => ("bold off", 3),
        [0x1B, 0x74, ..] => ("codepage", 3),
        [0x1D, 0x21, 0, ..] => ("normal height", 3),
        [0x1D, 0x21, ..] => ("double height", 3),
        [0x1D, 0x56, ..] => ("cut", 4),
        [0x1D, 0x68, ..] => ("barcode height", 3),
        [0x1D, 0x77, ..] => ("barcode width", 3),
        [0x1D, 0x48, ..] => ("barcode text", 3),
        [0x1D, 0x6B, ..] => ("barcode", 4),
        [0x1B, 0x70, ..] => ("DRAWER KICK", 5),
        _ => ("?", 1),
    };
}
