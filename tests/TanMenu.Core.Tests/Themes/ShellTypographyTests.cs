using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the one typographic fact Round 8, Task 8 established over this app's own markup:
/// <b>nothing states a font weight.</b>
/// </summary>
/// <remarks>
/// <para>
/// <c>https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-fonts</c> gives Windows 7 a table of
/// text roles and one font for each, and not one row this app uses carries a weight: a main
/// instruction is "12 pt. blue (#003399) Segoe UI", a group header is "11 pt. blue (#003399) Segoe
/// UI", normal text is "9 pt. black (#000000) Segoe UI". The same page's Attributes section opens
/// "Most UI text should be plain without any attributes", and allows bold only "in control labels to
/// make the text easier to parse" and "sparingly to draw attention to text users must read".
/// </para>
/// <para>
/// Three <c>SemiBold</c> setters survived Round 7's Aero conversion in
/// <c>Themes/Shell/Launcher.axaml</c> — <c>TextBlock.tm-section</c>, <c>TextBlock.tm-group-head</c>
/// and <c>StackPanel.tm-empty &gt; TextBlock.title</c> — every one inherited from the Windows 11 type
/// ramp those classes were first written against (Body Strong, Subtitle), and every one a weight no
/// row of that table has. Round 8, Task 8 deleted all three. Hierarchy on these surfaces is SIZE and
/// COLOUR now, which is what Aero itself used.
/// </para>
/// <para>
/// <b>Why a source test and not a rendered one.</b> A weight is silent at run time: a heading that
/// gained a <c>SemiBold</c> again would still lay out, still fit, and still pass every geometry
/// assertion in this suite — it would just be off the published table. The design for this round said
/// a <c>dotnet test</c> for the weight change "is not possible"; that is true of a RENDERED
/// assertion on this machine and not of a source one, which is what this is.
/// </para>
/// <para>
/// <b>Scope, stated rather than assumed.</b> Every <c>.axaml</c> under <c>src/TanMenu.Wpf</c> is
/// scanned except <see cref="Vendored"/> and the two files in <see cref="OutOfScope"/>. Neither
/// exclusion is an oversight:
/// </para>
/// <list type="bullet">
///   <item><c>Themes/Aero/</c> is the vendored third-party theme. Changing a weight in there would
///   falsify the "nothing else was changed" claim its headers make, which
///   <see cref="AeroAttributionTests"/> owns.</item>
///   <item><c>Controls/SplashPanel.axaml</c> carries two <c>FontWeight="Light"</c> lines on a branded
///   splash whose accent gradient, drop-shadowed plate and animated spinner Round 8 left standing.
///   Task 8 changed that file's corner radius and nothing else; its typography is recorded as
///   undeclared residue, not as swept.</item>
///   <item><c>Dialogs/MessageBoxWindow.axaml</c> carries <c>FontWeight="Bold"</c> on the single
///   character drawn inside the alert's icon plate. That is a glyph standing in for a system icon
///   bitmap this app ships no asset for, and it is the "draw attention to text users must read" case
///   the guide sanctions.</item>
/// </list>
/// <para>
/// There is no pending list here on purpose. This suite's other ratchets carry one only to record a
/// state the very next commit removes; a permanent entry would be an exemption wearing a ratchet's
/// clothes. The two files are named as SCOPE, with their reason, so that widening the scan stays a
/// decision someone makes rather than a list someone notices.
/// </para>
/// </remarks>
public class ShellTypographyTests
{
    /// <summary>The vendored Aero theme, excluded whole.</summary>
    private const string Vendored = "Themes/Aero/";

    /// <summary>Files outside this rule, each for the reason in the class remarks.</summary>
    private static readonly string[] OutOfScope =
    {
        "Controls/SplashPanel.axaml",
        "Dialogs/MessageBoxWindow.axaml",
    };

    [Fact]
    public void NoAppMarkup_StatesAFontWeight()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var (file, doc) in ScannedFiles())
        {
            scanned++;

            // Two shapes carry a weight: a Setter inside a Style or ControlTheme, and a local
            // attribute on an element. Either may be attached (TextElement.FontWeight,
            // TemplatedControl.FontWeight), so the LOCAL NAME is what is matched, not the whole one.
            foreach (var element in doc.Descendants())
            {
                if (element.Name.LocalName == "Setter")
                {
                    var property = (string?)element.Attribute("Property");
                    if (property is not null && property.EndsWith("FontWeight", StringComparison.Ordinal))
                        offenders.Add($"{file}: Setter Property=\"{property}\"");
                }

                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name.LocalName.EndsWith("FontWeight", StringComparison.Ordinal))
                        offenders.Add(
                            $"{file}: <{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\">");
                }
            }
        }

        // A scan that found no files would pass by checking nothing. There were 14 when Round 8,
        // Task 8 wrote this: App.axaml, MainWindow.axaml, Controls/InfoBar.axaml, the two Dialogs
        // windows left in scope, Themes/Shell's two, and the seven views.
        Assert.True(
            scanned >= 12,
            $"Only {scanned} markup files were scanned. This test's scope has stopped matching the " +
            "tree; the tree has not stopped having markup.");

        Assert.True(
            offenders.Count == 0,
            "No markup in this app may state a font weight. " +
            "https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-fonts gives every text role " +
            "a size and a colour and no weight, and its Attributes section opens \"Most UI text " +
            "should be plain without any attributes\". Round 8, Task 8 deleted the last three " +
            "SemiBold setters, all of them left over from the Windows 11 type ramp. Separate the " +
            "line with FontSizeGroupHead / FontSizeSubtitle and HeadingBrush instead. Offending: " +
            string.Join(", ", offenders));
    }

    private static IEnumerable<(string File, XDocument Doc)> ScannedFiles()
    {
        var appDir = Path.Combine(RepoRoot(), "src", "TanMenu.Wpf");
        Assert.True(Directory.Exists(appDir), "src/TanMenu.Wpf does not exist.");

        foreach (var path in Directory.EnumerateFiles(appDir, "*.axaml", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(appDir, path).Replace('\\', '/');

            // bin/ and obj/ hold no .axaml today — the compiler embeds them rather than copying —
            // but a build-layout change must not silently double every file in the scan.
            if (relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith("obj/", StringComparison.Ordinal))
                continue;
            if (relative.StartsWith(Vendored, StringComparison.Ordinal))
                continue;
            if (OutOfScope.Contains(relative, StringComparer.Ordinal))
                continue;

            yield return (relative, XDocument.Load(path));
        }
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
}
