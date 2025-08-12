using System.IO;
using System.IO.Compression;

namespace SccmOfflineUpgrade
{
    internal static class FileUtils
    {
        public static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return Path.GetFullPath(path);
        }

        public static void SafeDeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        public static void ZipDirectory(string sourceDir, string outputZip, bool overwrite = true)
        {
            if (File.Exists(outputZip) && overwrite)
                File.Delete(outputZip);

            ZipFile.CreateFromDirectory(sourceDir, outputZip, CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        public static void Unzip(string zipPath, string destinationDir, bool overwrite = true)
        {
            if (Directory.Exists(destinationDir) && overwrite)
            {
                Directory.Delete(destinationDir, true);
            }
            Directory.CreateDirectory(destinationDir);
            ZipFile.ExtractToDirectory(zipPath, destinationDir);
        }
    }
}
