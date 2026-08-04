using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TanMenu.Core.Tests.Themes;

/// <summary>
/// Guards the two obligations that come attached to the vendored Aero theme files under
/// <c>src/TanMenu.Wpf/Themes/Aero/</c>.
/// </summary>
/// <remarks>
/// <para>
/// The files there are copied, individually and with edits, out of
/// <c>https://github.com/Splitwirez/aero-theme-for-avalonia</c> at <c>main = a705b7c0</c>. That
/// repository is plain MIT — the GitHub API reports <c>license.spdx_id = "MIT"</c> and its
/// <c>LICENSE</c> is the verbatim unmodified MIT text with no additional clause and no CLA — so
/// copying is permitted PROVIDED the copyright notice and the permission notice travel with the
/// copies. Nothing in a build enforces that, and a file added later without the notice would be a
/// licence violation that no reviewer is guaranteed to catch. Hence
/// <see cref="EveryVendoredAeroFile_CarriesTheUpstreamCopyrightHeader"/>.
/// </para>
/// <para>
/// The second obligation is ours rather than the licence's.
/// <see cref="NoVendoredFile_ReferencesTheInterFontFamily"/> closes a CJK regression: the upstream
/// repository bundles seven TTFs of one font family and exactly one line anywhere in it references
/// them, <c>AVMAP/Metrics.axaml</c>'s <c>ContentControlThemeFontFamily</c>. Those TTFs are NOT
/// vendored (they carry no font licence anywhere upstream, which a repository-level MIT notice does
/// not supply), and the family has no CJK coverage. Left in, that one line would point every stock
/// Avalonia control at a font that cannot draw a single character of this app's Chinese UI. The
/// line is deleted on vendoring; this test is what keeps it deleted.
/// </para>
/// <para>
/// Both are SOURCE tests, like <see cref="ShellTokenTests"/> and
/// <see cref="ShellConstantTests"/>, and for the usual reason: neither failure throws at run time.
/// </para>
/// </remarks>
public class AeroAttributionTests
{
    private static string AeroDir =>
        Path.Combine(RepoRoot(), "src", "TanMenu.Wpf", "Themes", "Aero");

    /// <summary>
    /// Files under <c>Themes/Aero/</c> that are NOT copies of upstream and therefore carry no
    /// upstream notice. Each is listed by name rather than by pattern, so adding another file
    /// without a header means editing this test — which is the point of the guard.
    /// </summary>
    /// <remarks>
    /// The list is short on purpose and every entry has to earn its place: attaching Splitwirez's
    /// copyright line and an upstream path to a file he did not write would be a false statement in
    /// exactly the place this project's documentation rule cares most about. Note the contrast with
    /// <c>ControlStyles/ProgressBar.axaml</c>, which is NOT here: that file is the determinate
    /// branch of a real upstream file, so it is a modified copy, keeps the notice, and records what
    /// it dropped in its own "Changes from upstream" list.
    /// </remarks>
    private static readonly HashSet<string> NotVendored = new(StringComparer.OrdinalIgnoreCase)
    {
        // The upstream licence text itself, byte for byte. A notice does not annotate itself.
        "LICENSE-aero-theme-for-avalonia.txt",
        // The wrapper this project wrote to merge the vendored dictionaries and to name the two
        // theme variants "Light" / "Dark" instead of upstream's "Default" / "Dark".
        "AeroResources.axaml",
        // Round 7 Task 6. aero-theme-for-avalonia has NO ComboBox: all nine of its branches were
        // queried through the GitHub trees API for ComboBox*.axaml and every one returned zero
        // hits, and its AVMAP/Controls.axaml names the type only inside a commented-out block
        // pointing at an assembly that does not exist. The composition is therefore authored from
        // Avalonia 12.1's own template contract against the vendored brush vocabulary, and there is
        // no upstream path a "Source:" line could name. The file opens by saying exactly that, and
        // by naming the vocabulary it does reuse.
        //
        // ComboBoxItem.axaml IS NOT HERE, and the distinction is the point of this list. It has no
        // upstream counterpart under its own name either — but what it ships is the vendored
        // ControlStyles/ListBoxItem.axaml re-keyed, ~50 lines of markup that are Splitwirez's, so
        // it carries the notice and the numbered change list like any other copy. A file is on this
        // list when nothing in it was copied, not when nothing UNDER THAT NAME was copied.
        "ComboBox.axaml",
    };

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

    private static IEnumerable<string> VendoredFiles() =>
        Directory.GetFiles(AeroDir, "*", SearchOption.AllDirectories)
            .Where(f => !NotVendored.Contains(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.Ordinal);

    [Fact]
    public void EveryVendoredAeroFile_CarriesTheUpstreamCopyrightHeader()
    {
        // The four parts of the fixed header form: the copyright notice, the pointer to the
        // permission notice, the exact upstream revision, and the record of what was edited.
        var required = new[]
        {
            "Copyright (c) 2026 Splitwirez",
            "Licensed under the MIT License.",
            "LICENSE-aero-theme-for-avalonia.txt",
            "https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0",
            "Changes from upstream:",
        };

        var files = VendoredFiles().ToList();
        Assert.True(files.Count > 0, "No vendored files found under " + AeroDir);

        // The licence file the headers point at must actually be there.
        Assert.True(File.Exists(Path.Combine(AeroDir, "LICENSE-aero-theme-for-avalonia.txt")),
            "Themes/Aero/LICENSE-aero-theme-for-avalonia.txt is missing, so every vendored file's " +
            "header points at nothing and the MIT permission notice does not travel with the copies.");

        var offenders = new List<string>();
        foreach (var file in files)
        {
            // The header is at the TOP of the file, so only the opening of it is inspected: a
            // notice buried three hundred lines down is not a notice.
            var head = string.Join("\n", File.ReadLines(file).Take(30));
            var missing = required.Where(r => !head.Contains(r, StringComparison.Ordinal)).ToList();
            if (missing.Count > 0)
                offenders.Add($"{Path.GetFileName(file)} (missing: {string.Join(" | ", missing)})");
        }

        Assert.True(offenders.Count == 0,
            "MIT requires the copyright notice and the permission notice to travel with every copied " +
            "file. These files under Themes/Aero/ do not carry the header: " +
            string.Join("; ", offenders));
    }

    [Fact]
    public void NoVendoredFile_ReferencesTheInterFontFamily()
    {
        // Comments are stripped first, and that is deliberate rather than lax: the vendored
        // Metrics.axaml's own "Changes from upstream" list has to be able to SAY which line it
        // deleted and why, and the wrapper's prose has to be able to explain the CJK argument.
        // What must not survive is a live reference in markup or code.
        var offenders = new List<string>();
        foreach (var file in VendoredFiles())
        {
            var text = File.ReadAllText(file);
            var body = Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                ? Regex.Replace(text, @"//[^\r\n]*", " ")
                : Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline);

            // \bInter\b, not "Inter": "Internal" (x:ClassModifier="Internal" is on three of these
            // files) must not be mistaken for the font family.
            if (Regex.IsMatch(body, @"\bInter\b"))
                offenders.Add($"{Path.GetFileName(file)}: names the Inter family");

            // The avares:// root the upstream fonts live under. Nothing of ours may resolve into
            // the upstream assembly at all — it is not referenced and does not exist here.
            if (body.Contains("avares://AeroAvalonia", StringComparison.Ordinal))
                offenders.Add($"{Path.GetFileName(file)}: an avares://AeroAvalonia URI survived vendoring");
        }

        Assert.True(offenders.Count == 0,
            "The seven bundled Inter TTFs are deliberately NOT vendored (no font licence upstream) " +
            "and Inter has no CJK coverage, so a live reference to it would render this app's " +
            "Chinese item names as tofu: " + string.Join("; ", offenders));
    }
}
