using System.IO;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Saving a small file safely: write a temporary copy, then swap it in.
    /// A crash or power cut mid-save leaves the old file whole instead of a
    /// half-written one (which would load as "defaults" and lose settings).
    /// </summary>
    public static class SafeFile
    {
        public static void WriteAllText(string path, string text)
        {
            string tmp = path + ".saving";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
