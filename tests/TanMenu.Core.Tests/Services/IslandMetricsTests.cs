using System.Text.Json;
using TanMenu.Core.Models;
using TanMenu.Core.Serialization;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class IslandMetricsTests
{
    [Fact]
    public void ApprovedPreviewMetrics_MatchTheReviewedDefaults()
    {
        Assert.Equal(120, IslandMetrics.CollapsedWidth);
        Assert.Equal(32, IslandMetrics.CollapsedHeight);
        Assert.Equal(320, IslandMetrics.ExpandedWidth);
        Assert.Equal(72, IslandMetrics.ExpandedHeight);
        Assert.Equal(11, IslandMetrics.CollapsedTopRadius);
        Assert.Equal(20, IslandMetrics.ExpandedTopRadius);
        Assert.Equal(12, IslandMetrics.CollapsedFilletRadius);
        Assert.Equal(22, IslandMetrics.ExpandedFilletRadius);
        Assert.Equal(0, IslandMetrics.GapDip);
        Assert.Equal(300, IslandMetrics.ExpandDurationMs);
        Assert.Equal(250, IslandMetrics.CollapseDurationMs);
        Assert.Equal(80, IslandMetrics.LeaveDelayMs);
        Assert.Equal(0, IslandMetrics.EnterDelayMs);
    }

    [Fact]
    public void ShowIslandPreview_DefaultsOff_OnNewConfig()
    {
        Assert.False(new GeneralConfig().ShowIslandPreview);
        Assert.False(new AppConfig().General.ShowIslandPreview);
    }

    [Fact]
    public void ShowIslandPreview_MissingJsonKey_DeserializesFalse_AndWritesCamelCase()
    {
        var json = JsonSerializer.Serialize(new AppConfig(), ConfigJsonContext.Default.AppConfig);
        Assert.Contains("\"showIslandPreview\": false", json);

        const string legacyWithoutTheKey = """
            {
              "general": {
                "language": "zh-Hans"
              }
            }
            """;
        var loaded = JsonSerializer.Deserialize(legacyWithoutTheKey, ConfigJsonContext.Default.AppConfig);

        Assert.NotNull(loaded);
        Assert.False(loaded.General.ShowIslandPreview);
    }
}
