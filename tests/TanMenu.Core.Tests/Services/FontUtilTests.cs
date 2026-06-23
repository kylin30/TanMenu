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
    // The value is inlined into a window-wide <style> font-family; CSS-significant characters that
    // could break out of or hijack the rule must be stripped (defence against a hand-edited config).
    [InlineData("Arial\"; } body { background:url(x)")]
    [InlineData("a<script>alert(1)</script>")]
    [InlineData("x'); @import url(evil); /*")]
    public void Sanitize_StripsCssBreakingCharacters(string input)
    {
        var result = FontUtil.Sanitize(input);
        foreach (var c in new[] { '"', '\'', ';', '{', '}', '<', '>', '(', ')', '\\', ':', '@', '#', '/', '*' })
            Assert.DoesNotContain(c, result);
        // Letters are preserved (it stays a usable, if mangled, family token).
        Assert.NotEmpty(result);
    }
}
