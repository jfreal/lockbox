namespace Lockbox.Core.Vault;

internal static class FileUtil
{
    /// <summary>Writes to a temp file first so a crash mid-save never leaves a half-written file.</summary>
    public static void WriteAtomically(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }
}
