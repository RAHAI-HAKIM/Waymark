namespace Waymark.Generator;

/// <summary>
/// Reads an input file even while another program has it open — a catalogue under review in
/// Excel, a config open in an editor. <see cref="File.ReadAllBytes"/> asks for exclusive
/// read access and fails with "being used by another process" in exactly that case, which
/// is the normal way to review a CSV beside a run.
/// </summary>
internal static class InputFile
{
    public static byte[] ReadAllBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
