using System.Text.RegularExpressions;
using TanMenu.Wpf.Island;
using Xunit;

namespace TanMenu.Core.Tests;

public class IslandPreviewDefaultsTests
{
    [Fact]
    public void PreviewExtendedStyle_IsToolNoActivateAndTopmost_WithoutWholeWindowTransparency()
    {
        const uint wholeWindowTransparent = 0x00000020;
        var style = IslandWin32Styles.PreviewExtendedStyle;

        Assert.Equal(
            IslandWin32Styles.WsExNoActivate | IslandWin32Styles.WsExToolWindow | IslandWin32Styles.WsExTopmost,
            style);
        Assert.Equal(0u, style & wholeWindowTransparent);
        Assert.Equal(-1, (int)IslandWin32Styles.HtTransparent);
        Assert.Equal(3, (int)IslandWin32Styles.MaNoActivate);
        Assert.Equal(0x0084u, IslandWin32Styles.WmNcHitTest);
        Assert.Equal(0x0021u, IslandWin32Styles.WmMouseActivate);
    }

    [Fact]
    public void TaskbarQuery_OffWindows_StaysUnknown()
    {
        var (autoHide, thickness) = Win32TaskbarQuery.Query(1.5);
        if (OperatingSystem.IsWindows())
            return;

        Assert.Null(autoHide);
        Assert.Null(thickness);
    }

    [Fact]
    public void PreviewWindowXaml_IsAFixedNonActivatingTransparentHost()
    {
        var axaml = File.ReadAllText(Source("Island", "IslandPreviewWindow.axaml"));
        var code = File.ReadAllText(Source("Island", "IslandPreviewWindow.axaml.cs"));

        Assert.Contains("x:Class=\"TanMenu.Wpf.Island.IslandPreviewWindow\"", axaml);
        Assert.DoesNotContain("FAAppWindow", axaml);
        Assert.Contains("WindowDecorations=\"None\"", axaml);
        Assert.Contains("ShowInTaskbar=\"False\"", axaml);
        Assert.Contains("ShowActivated=\"False\"", axaml);
        Assert.Contains("Topmost=\"True\"", axaml);
        Assert.Contains("CanResize=\"False\"", axaml);
        Assert.Contains("Width=\"320\"", axaml);
        Assert.Contains("Height=\"72\"", axaml);
        Assert.Contains("MinWidth=\"320\"", axaml);
        Assert.Contains("MaxWidth=\"320\"", axaml);
        Assert.Contains("MinHeight=\"72\"", axaml);
        Assert.Contains("MaxHeight=\"72\"", axaml);
        Assert.Contains("Background=\"Transparent\"", axaml);
        Assert.Contains("TransparencyBackgroundFallback=\"Transparent\"", axaml);
        Assert.True(
            axaml.Contains("TransparencyLevelHint=\"Transparent\"", StringComparison.Ordinal) ||
            code.Contains("WindowTransparencyLevel.Transparent", StringComparison.Ordinal));
        Assert.Contains("HorizontalAlignment=\"Center\"", axaml);
        Assert.Contains("VerticalAlignment=\"Bottom\"", axaml);
        Assert.Contains("Background=\"#18181B\"", axaml);
        Assert.Contains("BorderBrush=\"#28282C\"", axaml);
        Assert.Contains("IsHitTestVisible=\"False\"", axaml);
        Assert.DoesNotContain("BoxShadow", axaml);
        Assert.Contains("IslandMetrics.LeaveDelayMs", code);
        Assert.Contains("_leaveTimer.Stop()", code);
        Assert.Contains("new CornerRadiusTransition", code);
        Assert.DoesNotContain("this.Width", code);
        Assert.DoesNotContain("this.Height", code);
    }

    [Fact]
    public void IslandSources_DoNotUseWholeWindowTransparencyOrPerFrameResize()
    {
        var islandDir = Path.Combine(WpfRoot, "Island");
        var sources = Directory.GetFiles(islandDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(islandDir, "*.axaml", SearchOption.TopDirectoryOnly))
            .Select(path => StripComments(File.ReadAllText(path)))
            .ToArray();
        var combined = string.Join("\n", sources);

        Assert.DoesNotContain("0x00000020", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("WS_EX_TRANSPARENT", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowPos", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CompositionTarget", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAndActivate", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("SetForegroundWindow", combined, StringComparison.Ordinal);
        Assert.Contains("HtTransparent", combined, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(combined, "new DispatcherTimer"));
    }

    [Fact]
    public void SettingsExposeThePreviewSwitch_DefaultUnchecked()
    {
        var axaml = File.ReadAllText(Source("Views", "Settings", "BehaviorCard.axaml"));
        var code = File.ReadAllText(Source("Views", "Settings", "BehaviorCard.axaml.cs"));
        var toggle = Regex.Match(axaml, "<ToggleSwitch x:Name=\"IslandPreviewToggle\"[^>]*/>");

        Assert.True(toggle.Success);
        Assert.Contains("hero-switch", toggle.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("IsChecked=\"True\"", toggle.Value, StringComparison.Ordinal);
        Assert.Contains("IslandPreviewToggle.IsChecked == true", code);
        Assert.Contains("ShowIslandPreview = enabled", code);
        Assert.Contains("island.Hide()", code);
        Assert.DoesNotContain("ShowAndActivate", code);
    }

    [Fact]
    public void StartupShowsThePreviewOnlyWhenTheSavedSwitchIsOn_AndExitDisposesItFirst()
    {
        var app = File.ReadAllText(Source("App.axaml.cs"));
        var startup = app[(app.IndexOf("private static void TryShowIslandPreviewFromConfig()", StringComparison.Ordinal))..];
        startup = startup[..startup.IndexOf("private static void TryDisposeIslandPreview()", StringComparison.Ordinal)];
        Assert.Contains("ShowIslandPreview", startup, StringComparison.Ordinal);
        Assert.Contains("return;", startup, StringComparison.Ordinal);
        Assert.True(startup.IndexOf("ShowIslandPreview", StringComparison.Ordinal) <
                    startup.IndexOf(".Show()", StringComparison.Ordinal));

        var exit = app[(app.IndexOf("public void ExitApp()", StringComparison.Ordinal))..];
        Assert.True(exit.IndexOf("TryDisposeIslandPreview()", StringComparison.Ordinal) >= 0);
        Assert.True(exit.IndexOf("TryDisposeIslandPreview()", StringComparison.Ordinal) <
                    exit.IndexOf("FlushPlacementBlocking()", StringComparison.Ordinal));
        Assert.True(exit.IndexOf("TryDisposeIslandPreview()", StringComparison.Ordinal) <
                    exit.IndexOf("desktop.Shutdown()", StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TanMenu.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string WpfRoot => Path.Combine(RepoRoot(), "src", "TanMenu.Wpf");

    private static string Source(params string[] parts) => Path.Combine(new[] { WpfRoot }.Concat(parts).ToArray());

    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }
}
