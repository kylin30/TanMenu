using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the three things the About box has to be, and the one thing the Options dialog has to have
/// stopped being. Round 8, Task 4.
/// </summary>
/// <remarks>
/// <para>
/// SOURCE tests, like <see cref="OptionsWindowTests"/> and <see cref="ShellConstantTests"/>, and for
/// the same reason those are: every failure below renders. A resizable About box renders. A second
/// <c>AboutCard</c> left behind in the Options dialog renders — as a sixth tab that is a duplicate of a
/// whole window. A hard-coded 关于 title renders, in one language.
/// </para>
/// <para>
/// PUBLISHED, <c>https://learn.microsoft.com/en-us/windows/win32/uxguide/win-property-win</c>: "Is the
/// content solely an application's attributes? If so, use an About box." That sentence is why this
/// window exists, and the rest of what it asserts is <c>vis-layout</c>'s: "Command buttons — 75x23" and
/// "Dialog box margins — 7 DLU / 11 relative pixels on all sides".
/// </para>
/// </remarks>
public class AboutWindowTests
{
    private static string AboutPath =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Dialogs", "AboutWindow.axaml");

    private static string OptionsPath =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Dialogs", "OptionsWindow.axaml");

    /// <summary>The markup with comments already gone — <see cref="XDocument"/> drops them — which
    /// matters here because this window's own prose quotes every sentence and every number these tests
    /// encode.</summary>
    private static XDocument About() => XDocument.Load(AboutPath);

    private static readonly XName XamlName =
        XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

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

    /// <summary>
    /// A fixed, ownerless-caption modal sized to its own content — the shape of a classic Windows
    /// About box.
    /// </summary>
    /// <remarks>
    /// <c>CanResize="False"</c> is the one with a published consequence attached: PUBLISHED,
    /// <c>win-dialog-box</c>, "Resizable dialogs can have a Maximize button", i.e. a fixed one has none —
    /// which Windows does not honour from the markup alone, hence <c>Dialogs/DialogCaption.cs</c>.
    /// <c>Icon</c> is asserted ABSENT for the same family of reasons ("Dialog boxes don't have title bar
    /// icons"), and <c>Title</c> is asserted absent because the title is written from
    /// <c>AppLanguage</c> in <c>ApplyText</c> — a literal here would be a label in one language.
    /// </remarks>
    [Fact]
    public void AboutBox_IsAFixedContentSizedModal()
    {
        var window = About().Root!;

        Assert.Equal("WidthAndHeight", (string?)window.Attribute("SizeToContent"));
        Assert.Equal("False", (string?)window.Attribute("CanResize"));
        Assert.Equal("False", (string?)window.Attribute("ShowInTaskbar"));
        Assert.Equal("CenterOwner", (string?)window.Attribute("WindowStartupLocation"));

        Assert.Null((string?)window.Attribute("Icon"));
        Assert.Null((string?)window.Attribute("Width"));
        Assert.Null((string?)window.Attribute("Height"));
        Assert.Null((string?)window.Attribute("Title"));

        var code = File.ReadAllText(AboutPath + ".cs");
        Assert.Matches(@"Title\s*=\s*AppLanguage\.Text\(\s*""About""", code);
        Assert.Matches(@"DialogCaption\.ApplyTo\(this\)", code);
    }

    /// <summary>
    /// The whole content is one <c>AboutCard</c> and one 确定 button, inside the published dialog margin.
    /// </summary>
    /// <remarks>
    /// ONE button, and it carries BOTH <c>IsDefault</c> and <c>IsCancel</c>: there is nothing here to
    /// commit, so Enter and Esc mean the same thing and a one-button dialog is what says so. The row
    /// deliberately has NO top margin — the hosted card already carries the published 11 as its own
    /// bottom margin, so stating a second would render 22. That 11 came from
    /// <c>Themes/Shell/Launcher.axaml</c>'s <c>HeaderedContentControl.tm-group-box</c> until Round 8,
    /// Task 6 deleted both the rule and the group box it styled; <c>AboutCard.axaml</c>'s own root
    /// Grid states it now, which is why this assertion did not have to move with it.
    /// </remarks>
    [Fact]
    public void AboutBox_HostsTheAboutCardAndOneOkButton()
    {
        var root = About().Root!.Elements().Single();
        Assert.Equal("StackPanel", root.Name.LocalName);
        Assert.Equal("11", (string?)root.Attribute("Margin"));

        var children = root.Elements().ToList();
        Assert.Equal(2, children.Count);
        Assert.Equal("AboutCard", children[0].Name.LocalName);

        var row = children[1];
        Assert.Equal("Right", (string?)row.Attribute("HorizontalAlignment"));
        Assert.Null((string?)row.Attribute("Margin"));

        var button = row.Elements().Single();
        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal("OkBtn", (string?)button.Attribute(XamlName));
        Assert.Equal("75", (string?)button.Attribute("Width"));
        Assert.Equal("23", (string?)button.Attribute("Height"));
        Assert.Equal("True", (string?)button.Attribute("IsDefault"));
        Assert.Equal("True", (string?)button.Attribute("IsCancel"));

        // No Theme=, so it rides the vendored Aero {x:Type Button} like the Options dialog's three.
        Assert.Null((string?)button.Attribute("Theme"));
    }

    /// <summary>
    /// 关于 left the Options dialog completely: no <c>AboutCard</c> in its markup, and no
    /// <c>ShowAboutTab</c> in its code-behind.
    /// </summary>
    /// <remarks>
    /// The tab COUNT is <see cref="OptionsWindowTests.EveryTabHeaderComesFromAppLanguage"/>'s; this is
    /// the other half — a count of five with the card still parked somewhere off-strip would pass that
    /// test and ship two About boxes. <c>ShowAboutTab</c> was Round 8, Task 3's provisional bridge from
    /// the menu band's 关于 item into the Options dialog, and it is the method whose deletion this
    /// asserts: left behind, it would be dead code naming a field that no longer exists.
    /// </remarks>
    [Fact]
    public void TheOptionsDialogNoLongerCarriesTheAboutCard()
    {
        var options = File.ReadAllText(OptionsPath);
        var optionsCode = File.ReadAllText(OptionsPath + ".cs");

        Assert.DoesNotMatch(@"<cards:AboutCard\b", options);
        Assert.DoesNotMatch(@"\bAboutTab\b", Regex.Replace(options, "<!--.*?-->", " ", RegexOptions.Singleline));
        Assert.DoesNotMatch(@"\bShowAboutTab\b", optionsCode);

        // And nothing else in the shell reaches for it either — the menu band's 关于 item opens the
        // About box directly (MainWindow.OnAboutClick).
        var mainWindowCode = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "MainWindow.axaml.cs"));
        Assert.DoesNotMatch(@"\bShowAboutTab\b", mainWindowCode);
        Assert.Matches(@"AboutWindow\.ShowAsync\(", mainWindowCode);
    }
}
