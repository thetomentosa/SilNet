using System.IO;
namespace SilNet;
public static class AppLog
{
    public static string DirectoryPath => Path.Combine(Settings.DirectoryPath, "logs");
    private static readonly object Sync = new();
    public static void Write(string source, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, $"silnet-{DateTime.Now:yyyy-MM-dd}-{Environment.ProcessId}.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.WriteAllText(path, "Log rotated\n");
                File.AppendAllText(path, $"{DateTime.Now:O} [{source}] {message}\n");
            }
        }
        catch { }
    }
}
