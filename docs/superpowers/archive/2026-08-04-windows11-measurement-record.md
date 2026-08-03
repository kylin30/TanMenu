# The Windows 11 measurement record

> **Status: ARCHIVE.** This file holds measurements that were taken against a real Windows 11 machine,
> were true of the code at the time, and describe code that no longer exists. Nothing here is a
> statement about the current tree. It exists because the measurements themselves are evidence worth
> keeping, and because this project's rule is that a comment which no longer describes the code is a
> defect — so the prose had to leave the source file, and deleting real evidence outright is worse than
> filing it.
>
> Created by **Round 7, Task 1** (the Aero caption), which is the first task to falsify one of these
> blocks. The Aero conversion design (`../specs/2026-08-04-aero-conversion-design.md`, §4 bucket C)
> lists the other blocks that land here as their own tasks reach them. **Sections are added by the task
> that deletes the code the section describes; nothing is moved ahead of that.**

---

## Round 4, item 5 — the white band across the top of the launcher window

**Deleted from** `src/TanMenu.Wpf/Themes/Fluent2/Tokens.axaml`, where it justified a
`TitleBarBackgroundBrush` = `Transparent` override.
**Stopped being true of the code at** Round 7, Task 1, which replaced Avalonia's
`WindowDrawnDecorations` template wholesale — so the part that read the key stopped existing and the
key lost its only consumer.
**Instrument:** `PrintWindow` (flag 2) into a `System.Drawing.Bitmap`, sampled with pixel reads; plus a
temporary `GetVisualDescendants()` dump through `TopLevelHost` on the live process.
**Machine:** this repository's development machine, Windows 11 Pro 10.0.28000, primary monitor at 150 %.

### What was measured

On a `PrintWindow` capture of the live Debug build, before the override existed, the title strip read
**(255, 255, 255)** and the nav rail one row below it read **(241, 241, 249)** at the same x — sampled
at x = 35 and x = 60. That 14/14/6 difference is what the user's item 5 ("任务栏和左侧导航栏应该是背景色
相同") was about. After the override, all four sample points read (241, 241, 249), i.e. the Mica
backdrop.

### Where the band came from

Avalonia's drawn-decorations template (`WindowDrawnDecorationsContent`, instantiated because MainWindow
set `ExtendClientAreaToDecorationsHint`) built an **underlay** — the layer `TopLevelHost` inserts behind
the window's own content — containing a `Panel` named `PART_TitleBar`, `VerticalAlignment=Top`, `Height`
bound to `TitleBarHeight` (48 in the app at the time), `Background` bound to
`{DynamicResource TitleBarBackgroundBrush}`. Read out of the compiled XAML with
`ilspycmd -t "CompiledAvaloniaXaml.!AvaloniaResources"` on Avalonia.Themes.Fluent 12.1.0, then confirmed
on the **running** app with a temporary `GetVisualDescendants()` dump reached through `TopLevelHost`:

```
Panel name=PART_TitleBar bounds=0,0,1000,48 vis=true bg=White
```

immediately before `MainWindow` in the same tree. That dump is what ruled OUT the alternative
explanation the project had carried until then — that the band was a `PrintWindow` artefact of
`DwmExtendFrameIntoClientArea`. The band was really on screen.

### The finding that outlived its subject

A `Style` setter inside `Window.WindowDecorationsTheme` **cannot** beat a value the template applies as
a **binding on the part itself**. Tried and measured: a `^ /template/ Panel#PART_TitleBar` rule setting
`Background="Transparent"` left the part reporting `bg=White` and the band on screen. The selector was
not the problem — an `Opacity` setter added to the *same* rule as a probe reported back as `op=0.5` on
the same run, so the rule matched. Overriding the **resource key** the template names is the seam that
works.

That precedence fact is theme-independent and still true. It is the reason Round 7, Task 1 replaced the
whole `Template` rather than continuing to patch parts of Fluent's.

### Scope note, as it stood

Only `MainWindow` ever set `ExtendClientAreaToDecorationsHint` (grep: it appeared in no other `.axaml`),
so this was the only window in the app with drawn decorations to tint at all. The override was
deliberately variant-independent, because the requirement — that the strip show the window backdrop
through it exactly as the rail beside it does — held in Light and Dark alike.
