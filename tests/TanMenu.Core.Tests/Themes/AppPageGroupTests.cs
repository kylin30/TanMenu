using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the one thing Round 6, item 4 (应用页面每个分类增加一个面板样式) can get silently wrong.
/// </summary>
/// <remarks>
/// <para>
/// Item 4 wrapped each category on the 应用 page in a <c>Border Classes="tm-group-panel"</c> and moved
/// <c>IsVisible="{Binding HasVisibleItems}"</c> from the inner <c>StackPanel Classes="tm-group"</c>
/// onto that Border. The move is not tidying. The reconnaissance MEASURED what happens when a filter
/// hides a category: the <c>ItemsControl</c>'s generated container stays visible at zero size and only
/// the bound child collapses — with the search box set to "Steam", four of the five group containers
/// reported an empty UIA rect while the items panel still measured 187.33 DIP, the surviving group's
/// 91.33 plus four inter-category gaps. So a Border added OUTSIDE a binding that stayed on the
/// StackPanel would paint its fill, its hairline and its 12/8 padding around nothing at all: every
/// filtered-out category would render as an empty bordered box 22 DIP wide.
/// </para>
/// <para>
/// A source test, for the same reason <see cref="ShellTokenTests"/> and <see cref="SettingsRowTests"/>
/// are: the failure is invisible until somebody types in the search box, no capture taken on a resting
/// page would show it, and nothing under <c>tests/</c> can drive Avalonia layout (every test here runs
/// off the UI thread). Item 4 is the only one of Round 6's four items that adds a test, and this is why.
/// </para>
/// </remarks>
public class AppPageGroupTests
{
    private const string PanelClass = "tm-group-panel";
    private const string GroupClass = "tm-group";
    private const string VisibilityBinding = "{Binding HasVisibleItems}";

    private static XDocument AppPage()
    {
        var path = Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Views", "AppPage.axaml");
        Assert.True(File.Exists(path), "Views/AppPage.axaml is missing: " + path);
        return XDocument.Load(path);
    }

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

    private static bool HasClass(XElement e, string cls) =>
        ((string?)e.Attribute("Classes") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(cls, StringComparer.Ordinal);

    [Fact]
    public void GroupPanel_CarriesTheVisibilityBinding()
    {
        var doc = AppPage();

        var panels = doc.Descendants()
            .Where(e => e.Name.LocalName == "Border" && HasClass(e, PanelClass))
            .ToList();

        Assert.True(panels.Count == 1,
            $"AppPage.axaml must declare exactly one <Border Classes=\"{PanelClass}\"> — the category " +
            $"panel inside the MenuGroupVm DataTemplate — but it declares {panels.Count}.");

        Assert.Equal(VisibilityBinding, (string?)panels[0].Attribute("IsVisible"));

        var groupsWithVisibility = doc.Descendants()
            .Where(e => e.Name.LocalName == "StackPanel"
                        && HasClass(e, GroupClass)
                        && e.Attribute("IsVisible") is not null)
            .ToList();

        Assert.True(groupsWithVisibility.Count == 0,
            $"IsVisible must live on the <Border Classes=\"{PanelClass}\">, never on the " +
            $"<StackPanel Classes=\"{GroupClass}\"> inside it. A filtered-out category whose Border stays " +
            "visible renders as an empty bordered box, because the ItemsControl's generated container " +
            "stays visible at zero size — measured, not hypothetical.");
    }
}
