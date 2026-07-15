using System.IO;
using TanMenu.Core.Infrastructure;
using Xunit;

namespace TanMenu.Core.Tests.Infrastructure;

public class PortableRuntimeTests
{
    [Fact]
    public void IsEnabledAt_RequiresMarkerFile()
    {
        var root = NewTempRoot();
        try
        {
            Assert.False(PortableRuntime.IsEnabledAt(root));

            File.WriteAllText(Path.Combine(root, PortableRuntime.MarkerFileName), "portable");

            Assert.True(PortableRuntime.IsEnabledAt(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PortablePaths_AreInsideApplicationDirectory()
    {
        var root = NewTempRoot();
        try
        {
            var data = PortableRuntime.GetDataRoot(root);

            Assert.Equal(Path.Combine(root, "Data"), data);
            Assert.Equal(Path.Combine(root, "Data", "WebView2"),
                PortableRuntime.GetWebView2DataFolder(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigrateLegacyDataIfNeeded_MovesDataToStableRoot()
    {
        var root = NewTempRoot();
        try
        {
            var current = Path.Combine(root, "current");
            Directory.CreateDirectory(Path.Combine(current, "Data"));
            File.WriteAllText(Path.Combine(current, "Data", "config.json"), "legacy");

            Assert.True(PortableRuntime.MigrateLegacyDataIfNeeded(current, root));
            Assert.False(Directory.Exists(Path.Combine(current, "Data")));
            Assert.Equal("legacy", File.ReadAllText(Path.Combine(root, "Data", "config.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigrateLegacyDataIfNeeded_DoesNotOverwriteExistingStableData()
    {
        var root = NewTempRoot();
        try
        {
            var current = Path.Combine(root, "current");
            Directory.CreateDirectory(Path.Combine(current, "Data"));
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            File.WriteAllText(Path.Combine(current, "Data", "config.json"), "legacy");
            File.WriteAllText(Path.Combine(root, "Data", "config.json"), "stable");

            Assert.False(PortableRuntime.MigrateLegacyDataIfNeeded(current, root));
            Assert.Equal("stable", File.ReadAllText(Path.Combine(root, "Data", "config.json")));
            Assert.Equal("legacy", File.ReadAllText(Path.Combine(current, "Data", "config.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "TanMenuPortableTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }
}
