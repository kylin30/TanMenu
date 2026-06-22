namespace TanMenu.Core.Services;

public interface ILaunchService
{
    /// <summary>Launches a file/program via the shell (UseShellExecute=true). Returns false on error.</summary>
    bool Launch(string path);

    /// <summary>Opens a folder in Explorer (optionally selecting a file). Returns false on error.</summary>
    bool OpenFolder(string path, bool selectFile = false);
}
