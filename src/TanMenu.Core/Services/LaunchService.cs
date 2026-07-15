using System.Diagnostics;
using System.IO;

namespace TanMenu.Core.Services;

public sealed class LaunchService : ILaunchService
{
    public bool Launch(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // Note: no File.Exists guard here: UseShellExecute resolves bare commands and aliases
            // via App Paths + PATH, not just on-disk paths.
            // A truly unlaunchable value throws and is handled below.
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);
            // Shell launch may not return a Process handle (e.g. for documents), that's OK
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Launch failed: {ex.Message}");
            return false;
        }
    }

    public bool OpenFolder(string path, bool selectFile = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            path = Path.GetFullPath(path);

            bool isFile = File.Exists(path);
            bool isDirectory = Directory.Exists(path);
            if (!isFile && !isDirectory)
                return false;

            ProcessStartInfo startInfo;

            if (isFile && selectFile)
            {
                // Open the containing folder with the file selected
                startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = false
                };
            }
            else if (isFile)
            {
                // Open just the containing folder
                var directoryPath = Path.GetDirectoryName(path);
                startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directoryPath}\"",
                    UseShellExecute = false
                };
            }
            else
            {
                // Open the directory itself
                startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                };
            }

            using var process = Process.Start(startInfo);
            // explorer.exe may return null or exit immediately on success — that's fine
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenFolder failed: {ex.Message}");
            return false;
        }
    }
}
