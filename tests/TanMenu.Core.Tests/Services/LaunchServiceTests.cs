using System.IO;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class LaunchServiceTests
{
    [Fact]
    public void OpenFolder_MissingPath_ReturnsFalse()
    {
        ILaunchService svc = new LaunchService();
        Assert.False(svc.OpenFolder(@"C:\definitely\not\here\at\all"));
    }

    [Fact]
    public void OpenFolder_EmptyPath_ReturnsFalse()
    {
        ILaunchService svc = new LaunchService();
        Assert.False(svc.OpenFolder("   "));
    }

    [Fact]
    public void Launch_MissingPath_ReturnsFalse()
    {
        ILaunchService svc = new LaunchService();
        Assert.False(svc.Launch(@"C:\definitely\not\here.exe"));
    }

    [Fact]
    public void OpenFolder_ExistingDirectory_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TanMenuLaunchTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            ILaunchService svc = new LaunchService();
            Assert.True(svc.OpenFolder(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
