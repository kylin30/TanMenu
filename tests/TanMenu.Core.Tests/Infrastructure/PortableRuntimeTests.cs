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

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "TanMenuPortableTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }
}
