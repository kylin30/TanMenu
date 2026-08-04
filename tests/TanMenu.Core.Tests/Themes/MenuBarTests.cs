using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the classic menu band Round 8, Task 3 put where the navigation pane used to be: five items,
/// in one order, every one a direct command, every one with a distinct access key and no ellipsis.
/// </summary>
/// <remarks>
/// <para>
/// SOURCE tests, like <see cref="ShellConstantTests"/> and <see cref="ShellTokenTests"/>, and for the
/// same reason: every failure below renders rather than throws. A sixth item, a reordered pair, a
/// <c>MenuItem</c> that grew a child and quietly became a submenu, a duplicated access key or a label
/// with an ellipsis on it are all things the app runs perfectly well with.
/// </para>
/// <para>
/// <b>Each fact carries the published sentence it enforces.</b> Order is the USER's — 设置 · 主目录 ·
/// 刷新 · 退出 · 关于, with 刷新 kept because <c>LauncherViewModel.RefreshAsync</c> is the only
/// user-reachable caller of <c>ReloadAsync(force: true)</c>. Direct commands in a menu bar are legal
/// and rare (PUBLISHED, <c>menurc/about-menus</c>: "A menu bar typically contains menu names, but it can
/// also contain command items."). No ellipses is PUBLISHED, <c>cmd-menus</c>: About/Options "must
/// display another window when clicked, but don't require additional information from the user.
/// Therefore they don't need ellipses." Access keys on all of them, no exceptions, is PUBLISHED,
/// <c>inter-keyboard</c>.
/// </para>
/// </remarks>
public class MenuBarTests
{
    /// <summary>The five items, in the order the user asked for, by <c>x:Name</c>.</summary>
    private static readonly string[] ExpectedOrder =
        { "MenuOptions", "MenuOpenRoot", "MenuRefresh", "MenuExit", "MenuAbout" };

    /// <summary>Each item's PLAIN <c>AppLanguage</c> key — what <c>MainWindow.ApplyMenuText</c> writes as
    /// <c>AutomationProperties.Name</c>. 关于 is the odd one: its string predates the band (Round 5,
    /// item 2 renamed <c>MenuAbout</c> to <c>About</c> when 关于 became a settings card) and it is
    /// shared with the About group head rather than re-minted.</summary>
    private static readonly Dictionary<string, string> PlainKeys = new(StringComparer.Ordinal)
    {
        ["MenuOptions"] = "MenuOptions",
        ["MenuOpenRoot"] = "MenuOpenRoot",
        ["MenuRefresh"] = "MenuRefresh",
        ["MenuExit"] = "MenuExit",
        ["MenuAbout"] = "About",
    };

    /// <summary>Each item's MARKED key — what <c>ApplyMenuText</c> writes as the <c>Header</c>.</summary>
    private static readonly Dictionary<string, string> MarkedKeys = new(StringComparer.Ordinal)
    {
        ["MenuOptions"] = "MenuOptionsWithAccessKey",
        ["MenuOpenRoot"] = "MenuOpenRootWithAccessKey",
        ["MenuRefresh"] = "MenuRefreshWithAccessKey",
        ["MenuExit"] = "MenuExitWithAccessKey",
        ["MenuAbout"] = "MenuAboutWithAccessKey",
    };

    private static readonly string[] Languages = { AppLanguage.ZhHans, AppLanguage.EnUs };

    private static string SrcDir => Path.Combine(RepoRoot(), "src", "TanMenu.Wpf");

    /// <summary>MainWindow's markup with every XML comment stripped — its own prose names all five
    /// items, quotes their access keys and discusses ellipses, so a raw read would match a sentence as
    /// readily as an element.</summary>
    private static string MainWindowXaml =>
        Regex.Replace(File.ReadAllText(Path.Combine(SrcDir, "MainWindow.axaml")),
            "<!--.*?-->", " ", RegexOptions.Singleline);

    private static string MainWindowCs => File.ReadAllText(Path.Combine(SrcDir, "MainWindow.axaml.cs"));

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

    /// <summary>Every <c>&lt;MenuItem&gt;</c> in <c>MainWindow.axaml</c>, in document order, as
    /// (name, whether the tag is self-closing).</summary>
    private static List<(string Name, bool SelfClosing)> MenuItems()
    {
        var items = new List<(string, bool)>();
        foreach (Match m in Regex.Matches(MainWindowXaml, @"<MenuItem\b(?<attrs>[^>]*)>",
                     RegexOptions.Singleline))
        {
            var attrs = m.Groups["attrs"].Value;
            var name = Regex.Match(attrs, @"x:Name=""(?<n>[^""]+)""").Groups["n"].Value;
            items.Add((name, attrs.TrimEnd().EndsWith('/')));
        }
        return items;
    }

    [Fact]
    public void FiveCommands_InOrder()
    {
        var names = MenuItems().Select(i => i.Name).ToList();

        Assert.True(names.SequenceEqual(ExpectedOrder),
            "The menu band must hold exactly these five items in this order — 设置 · 主目录 · 刷新 · " +
            "退出 · 关于, the order the user asked for — but MainWindow.axaml declares: " +
            string.Join(", ", names.Select(n => n.Length == 0 ? "(unnamed)" : n)));
    }

    /// <summary>
    /// No item has element children, i.e. every one is a COMMAND and none is a submenu.
    /// </summary>
    /// <remarks>
    /// A <c>MenuItem</c> with children is a different control: <c>MenuItem.HasSubMenu</c> is
    /// <c>!Classes.Contains(":empty")</c> (decompiled, Avalonia 12.1.0), so a child flips the item from
    /// "click raises Click" to "click opens a popup", and it changes what the vendored Aero theme paints
    /// — <c>^[IsSubMenuOpen=True]</c> starts firing, which on a childless item never can. That is why
    /// the vendored file also had to gain a <c>^:pressed</c> branch. Self-closing is the whole test: a
    /// <c>MenuItem</c> with a header written from code and no children has nothing to put between tags.
    /// </remarks>
    [Fact]
    public void NoMenuItemHasChildren()
    {
        var withChildren = MenuItems().Where(i => !i.SelfClosing).Select(i => i.Name).ToList();

        Assert.True(withChildren.Count == 0,
            "Every item in the menu band is a DIRECT COMMAND, so every <MenuItem> must be self-closing. " +
            "These are not, which makes them submenus: " + string.Join(", ", withChildren));
    }

    [Fact]
    public void NoLabelEndsInAnEllipsis()
    {
        var offenders = new List<string>();
        foreach (var lang in Languages)
        foreach (var item in ExpectedOrder)
        foreach (var key in new[] { PlainKeys[item], MarkedKeys[item] })
        {
            var text = AppLanguage.Text(key, lang).TrimEnd();
            if (text.EndsWith('…') || text.EndsWith("...", StringComparison.Ordinal))
                offenders.Add($"{lang}/{key} = \"{text}\"");
        }

        Assert.True(offenders.Count == 0,
            "PUBLISHED, cmd-menus: a command that opens another window without asking the user for " +
            "anything first does NOT take an ellipsis. These do: " + string.Join("; ", offenders));
    }

    [Fact]
    public void AccessKeysAreDistinct()
    {
        foreach (var lang in Languages)
        {
            var keys = new List<string>();
            foreach (var item in ExpectedOrder)
            {
                var marked = AppLanguage.Text(MarkedKeys[item], lang);
                var letter = AccessKeyOf(marked);
                Assert.True(letter is not null,
                    $"PUBLISHED, inter-keyboard: every menu item carries an access key, no exceptions. " +
                    $"{lang}/{MarkedKeys[item]} = \"{marked}\" has no \"_\" marker.");
                keys.Add(letter!.ToUpperInvariant());
            }

            var duplicated = keys.GroupBy(k => k, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicated.Count == 0,
                $"Two menu items share an access key in {lang}, so Alt+that letter is ambiguous: " +
                string.Join(", ", duplicated) + " (all five: " + string.Join(", ", keys) + ")");
        }
    }

    /// <summary>
    /// Every <c>AppLanguage</c> key <c>ApplyMenuText</c> names actually exists, in both languages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AppLanguage.Text</c> returns THE KEY ITSELF for a key it does not hold — no throw, no log —
    /// so a typo or a key deleted from under this method renders a menu item labelled
    /// "MenuRefreshWithAccessKey" and nothing anywhere reports it. This fact also pins the pairing the
    /// band depends on: the marked key goes on the <c>Header</c> and the plain one on the accessible
    /// name, because <c>AccessText</c> strips the marker before drawing but a UIA name taken from the
    /// raw header keeps it (MEASURED in Round 8, Task 2 on the Options dialog's 应用 button).
    /// </para>
    /// <para>
    /// <b>The existence half reads the SOURCE, not <c>Text()</c>'s return value.</b> "key == value means
    /// missing" is the obvious test and it is wrong here: <c>About</c>'s English string is literally
    /// "About", so that heuristic reports a key that exists as absent. It was written that way first and
    /// this test caught it.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryMenuLabelKeyResolvesInBothLanguages()
    {
        var referenced = Regex.Matches(MainWindowCs, @"AppLanguage\.Text\(""(?<k>[^""]+)"", lang\)")
            .Select(m => m.Groups["k"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var expected = ExpectedOrder.Select(i => PlainKeys[i])
            .Concat(ExpectedOrder.Select(i => MarkedKeys[i]))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(referenced.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(expected),
            "MainWindow.axaml.cs must read exactly the ten menu keys — five plain for the accessible " +
            "names, five marked for the headers. It reads: " + string.Join(", ", referenced));

        var declared = Regex.Matches(
                File.ReadAllText(Path.Combine(RepoRoot(), "src", "TanMenu.Core", "Services",
                    "AppLanguage.cs")),
                @"^\s*\[""(?<k>[^""]+)""\]\s*=\s*\(", RegexOptions.Multiline)
            .Select(m => m.Groups["k"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(declared.Count > 100,
            $"The AppLanguage key scan found only {declared.Count} entries, which means the scan broke " +
            "rather than that the dictionary shrank. Fix the scan.");

        var missing = expected.Where(k => !declared.Contains(k)).ToList();
        Assert.True(missing.Count == 0,
            "AppLanguage.Text returns the KEY for a key it does not hold, so a missing entry labels a " +
            "menu item with its own key name and nothing reports it. Missing: " +
            string.Join(", ", missing));

        // Both halves of every entry are real: a one-language entry would render as an empty item.
        var blank = new List<string>();
        foreach (var lang in Languages)
        foreach (var key in expected)
            if (string.IsNullOrWhiteSpace(AppLanguage.Text(key, lang)))
                blank.Add($"{lang}/{key}");

        Assert.True(blank.Count == 0, "Blank menu label(s): " + string.Join(", ", blank));
    }

    /// <summary>The rune the first single <c>_</c> marks, or null when there is no marker. Avalonia's
    /// own rule (<c>Avalonia.Controls.Primitives.AccessText</c>, decompiled): the first "_" not followed
    /// by another "_" marks the next character.</summary>
    private static string? AccessKeyOf(string text)
    {
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '_')
                continue;
            if (text[i + 1] == '_')
            {
                i++; // an escaped literal underscore; skip both
                continue;
            }
            return text[i + 1].ToString();
        }
        return null;
    }
}
