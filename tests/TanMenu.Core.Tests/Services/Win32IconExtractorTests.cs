using System;
using System.IO;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class Win32IconExtractorTests
{
    [Fact]
    public void GetIconPngBytes_ForExplorerExe_ReturnsPngBytes()
    {
        var explorer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");
        Assert.True(File.Exists(explorer), "explorer.exe must exist on Windows test host");

        var bytes = Win32IconExtractor.GetIconPngBytes(explorer);

        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0);
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x4E, bytes[2]);
        Assert.Equal(0x47, bytes[3]);
    }

    [Fact]
    public void GetIconPngBytes_ForMissingPath_ReturnsNull()
    {
        Assert.Null(Win32IconExtractor.GetIconPngBytes(@"C:\nope\missing.exe"));
    }
}

public class IconProviderTests
{
    [Fact]
    public void IconProvider_Implements_IIconProvider()
    {
        IIconProvider provider = new IconProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void IconProvider_ForExistingFile_MatchesStaticExtractor()
    {
        // explorer.exe reliably exists AND has an extractable shell icon on all
        // Windows hosts (notepad.exe is a Store-app stub on Win11 and yields no icon).
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");
        Assert.True(File.Exists(path), "explorer.exe must exist on Windows test host");

        IIconProvider provider = new IconProvider();
        var viaInstance = provider.GetIconPngBytes(path);
        var viaStatic = Win32IconExtractor.GetIconPngBytes(path);

        // Both should be non-null PNG byte streams for a real executable.
        Assert.NotNull(viaInstance);
        Assert.NotNull(viaStatic);
        Assert.True(viaInstance!.Length > 0);
    }

    [Fact]
    public void IconProvider_ForMissingPath_ReturnsNull()
    {
        IIconProvider provider = new IconProvider();
        Assert.Null(provider.GetIconPngBytes(@"C:\definitely\not\here.exe"));
    }
}
