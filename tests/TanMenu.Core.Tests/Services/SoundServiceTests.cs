using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class SoundServiceTests
{
    [Fact]
    public void Initialize_DirWithoutWavFiles_IsReadyFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TanMenuSoundTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var svc = new SoundService(NullLogger<SoundService>.Instance);
            svc.Initialize(tempDir);
            Assert.False(svc.IsReady);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PlayClickSoundAsync_WhenNotReady_NoException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TanMenuSoundTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var svc = new SoundService(NullLogger<SoundService>.Instance);
            svc.Initialize(tempDir); // no wav files — IsReady == false
            // Must not throw
            await svc.PlayClickSoundAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PlayHoverSoundAsync_WhenNotReady_NoException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TanMenuSoundTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var svc = new SoundService(NullLogger<SoundService>.Instance);
            svc.Initialize(tempDir); // no wav files — IsReady == false
            // Must not throw
            await svc.PlayHoverSoundAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PlayClickSoundAsync_BeforeInitialize_NoException()
    {
        var svc = new SoundService(NullLogger<SoundService>.Instance);
        Assert.False(svc.IsReady);
        // Must not throw even without Initialize being called
        await svc.PlayClickSoundAsync();
    }

    [Fact]
    public void Dispose_CanBeCalledTwice_NoException()
    {
        var svc = new SoundService(NullLogger<SoundService>.Instance);
        svc.Dispose();
        svc.Dispose(); // should not throw
    }
}
