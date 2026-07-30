using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class FontUtilTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Segoe UI", "Segoe UI")]
    [InlineData("微软雅黑", "微软雅黑")]
    [InlineData("Alibaba PuHuiTi", "Alibaba PuHuiTi")]
    [InlineData("MS_Sans-Serif 2", "MS_Sans-Serif 2")]
    public void Sanitize_KeepsValidNames(string? input, string expected)
    {
        Assert.Equal(expected, FontUtil.Sanitize(input));
    }

    [Theory]
    // The value goes straight into Avalonia's FontFamily, so a hand-edited config.json must not be
    // able to smuggle punctuation into the family token. The inputs below are deliberately hostile
    // shapes (the CSS-injection payloads the pre-Avalonia build had to defend against, kept because
    // they exercise every character class the whitelist drops).
    [InlineData("Arial\"; } body { background:url(x)")]
    [InlineData("a<script>alert(1)</script>")]
    [InlineData("x'); @import url(evil); /*")]
    public void Sanitize_StripsEverythingOutsideTheWhitelist(string input)
    {
        var result = FontUtil.Sanitize(input);
        foreach (var c in new[] { '"', '\'', ';', '{', '}', '<', '>', '(', ')', '\\', ':', '@', '#', '/', '*' })
            Assert.DoesNotContain(c, result);
        // Letters are preserved (it stays a usable, if mangled, family token).
        Assert.NotEmpty(result);
    }
}
