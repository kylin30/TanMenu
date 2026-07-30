using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the one invariant that keeps light/dark a property of a single file: every colour in the
/// launcher comes from <c>Themes/Fluent2/Tokens.axaml</c>.
/// </summary>
/// <remarks>
/// These are source tests, not runtime tests, and that is deliberate. Both failures they catch are
/// SILENT at run time: a hardcoded <c>#RRGGBB</c> simply renders the wrong colour in one variant, and
/// a misspelled <c>{DynamicResource}</c> key resolves to nothing at all, leaving a control with a
/// null brush — Avalonia logs neither as an error.
/// </remarks>
public class Fluent2TokenTests
{
    private static string ThemesDir =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Themes", "Fluent2");

    private static string LauncherXaml => File.ReadAllText(Path.Combine(ThemesDir, "Launcher.axaml"));
    private static string TokensXaml => File.ReadAllText(Path.Combine(ThemesDir, "Tokens.axaml"));

    private static string MainWindowXaml =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "MainWindow.axaml"));

    /// <summary>Walk up from the test assembly until the solution file appears.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TanMenu.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (TanMenu.slnx) from " +
                                     AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void LauncherStyles_ContainNoColourLiterals()
    {
        // #RGB, #RRGGBB or #AARRGGBB anywhere in the launcher's style sheet.
        var literals = Regex.Matches(LauncherXaml, @"#[0-9A-Fa-f]{3,8}\b")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        Assert.True(literals.Count == 0,
            "Launcher.axaml must take every colour from Tokens.axaml, but it hardcodes: " +
            string.Join(", ", literals));
    }

    [Fact]
    public void EveryTokenTheLauncherReferences_IsDefinedInTokens()
    {
        var defined = DefinedKeys(TokensXaml);
        var missing = ReferencedKeys(LauncherXaml)
            .Where(k => !defined.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Launcher.axaml references tokens that Tokens.axaml does not define: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void EveryTokenTheLauncherWindowReferences_IsDefinedInTokens()
    {
        // MainWindow.axaml is the other consumer, and the one where a typo would leave the acrylic
        // backdrop or the window surface unpainted.
        var defined = DefinedKeys(TokensXaml);
        var missing = ReferencedKeys(MainWindowXaml)
            .Where(k => !defined.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "MainWindow.axaml references tokens that Tokens.axaml does not define: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void EveryThemedTokenIsDefinedInBothVariants()
    {
        // A token present in only one ThemeDictionary resolves to nothing in the other, which shows up
        // as an unpainted control in exactly one appearance — the kind of bug nobody sees until a user
        // switches to dark.
        var light = VariantKeys(TokensXaml, "Light");
        var dark = VariantKeys(TokensXaml, "Dark");

        var onlyLight = light.Except(dark).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var onlyDark = dark.Except(light).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(onlyLight.Count == 0, "Defined only in Light: " + string.Join(", ", onlyLight));
        Assert.True(onlyDark.Count == 0, "Defined only in Dark: " + string.Join(", ", onlyDark));
    }

    private static HashSet<string> DefinedKeys(string xaml) =>
        Regex.Matches(xaml, @"x:Key=""(?<k>[^""]+)""")
            .Select(m => m.Groups["k"].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ReferencedKeys(string xaml) =>
        Regex.Matches(xaml, @"\{(?:Dynamic|Static)Resource\s+(?<k>[A-Za-z0-9_]+)\s*\}")
            .Select(m => m.Groups["k"].Value)
            // ControlThemes live in Launcher.axaml itself, not in the token file.
            .Where(k => !k.StartsWith("Tm", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Keys declared inside one ThemeDictionary block.</summary>
    private static HashSet<string> VariantKeys(string xaml, string variant)
    {
        var openTag = $"<ResourceDictionary x:Key=\"{variant}\">";
        var tagAt = xaml.IndexOf(openTag, StringComparison.Ordinal);
        Assert.True(tagAt >= 0, $"Tokens.axaml has no {variant} ThemeDictionary");

        // Start AFTER the opening tag: its own x:Key names the variant, not a token.
        var start = tagAt + openTag.Length;
        var end = xaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Tokens.axaml's {variant} ThemeDictionary is not closed");

        return DefinedKeys(xaml[start..end]);
    }
}
