# Win11 shell Round 3 — final-fix-wave findings

> **DEPRECATED — Round 7, Task 10.** Describes the Windows 11 shell, shipped in 0.11.0-0.12.0 and
> replaced by the Aero conversion (`../specs/2026-08-04-aero-conversion-design.md`). Kept as the
> record of that work; its measurements were true of the code at the time and are not true of the code
> now. Nothing below was edited to add this header. `src/TanMenu.Wpf/Themes/Fluent2/` is named
> `Themes/Shell/` from Task 10 on, and the Windows 11 measurements several of these sections rest on
> are collected in `../archive/2026-08-04-windows11-measurement-record.md`.

Condensed from `.superpowers/sdd/2026-08-01-win11-shell-round3/final-fix-report.md` and
`progress.md` (and, for the height-floor measurement, `task-4-report.md`), a git-ignored scratch
workspace that was deleted after Round 3 closed. This is the durable record several source
comments cite in place of that workspace; it keeps only the facts those comments rely on, not the
subagent process narrative around them.

Round 3 plan: `docs/superpowers/plans/2026-08-01-win11-shell-round3.md`. Baseline `646b79e` (162
tests) through `725f408` (183 tests) — see "Round 3 commits" below.

## The ~605 DIP practical height floor

> **Corrected and superseded by Round 4, item 4** (commit landing
> `docs/superpowers/specs/2026-08-02-win11-shell-round4-design.md` §4.1(d), §7.3). The measurements in
> this section are accurate for the build that produced them. Two things below are not, and the
> corrections are inline: the floor was **not** a platform constant but an arithmetic consequence of
> `MainWindow.axaml`'s own `MinHeight="568"`, and the `SM_CYMINTRACK` explanation at the end of the
> section is a **numeric coincidence**. Round 4 removed `MinHeight` from `MainWindow.axaml`, which
> removes the floor: the app's 568 DIP minimum now lives in `WindowHost.MinWindowHeightDip` and is
> applied to the placement REQUEST rather than to the window, so requested height == real height.

At the time this was written, `MainWindow.axaml` declared `MinHeight="568"`. On the one 150%-scale
(144 DPI) monitor available
during Round 3, that height was not actually reachable: a `SetWindowPos` probe that bypassed all
app code found the window physically could not render below **908 physical / 605.3 DIP outer, 897
physical / 598 DIP client** — Windows clamped any smaller request and wrote the clamped value back
into `Width`/`Height`. Probing the exact threshold found the cliff at exactly 908/910
(`task-4-report.md`).

`WindowHost.FitToContent`'s own arithmetic is correct at every `ColButtonCount` tested — a clean
40 DIP/tile + ~71 DIP constant (raw page height 191.33 DIP at N=3, 271.33 at N=5, 471.33 at N=10,
871.33 at N=20, `task-4-report.md`) — the window simply cannot render shorter than its own floor.

**Consequence for `GeneralConfig.ColButtonCount`**: values of roughly 13 and below all resolved to
that same ~598 DIP client height, so the setting only became height-*effective* above that point.
Below it, lowering the value still visibly changes layout, just by trading height for width
instead: fewer rows per column means more columns across. Reproduced live with
`colButtonCount: 3`, which produced a **4648×568 DIP** window (`final-fix-report.md`,
`progress.md`) — on this machine's 5120×1392 DIP work area at 150% scale, per `task-4-report.md`'s
environment note (the two figures were not captured in the same probe, but are the same machine
and monitor throughout Round 3). This is not a defect: nothing in the Round 3 plan required the
setting to be height-effective across its whole [1, 30] range, only that the *default* not need a
horizontal scrollbar, which holds.

> Round 4 correction: the ~598 DIP part of that is gone. The app's own 568 DIP minimum still applies
> (`WindowHost.MinWindowHeightDip`), so low values still all land on one height — 568 rather than 598
> — and the cut-over is consequently a little lower than 13. It was **not** re-measured in Round 4, so
> no new exact value is claimed. The width behaviour is unchanged.

**Corrected by Round 4, item 4 — this inference was wrong.** The paragraph below is kept verbatim as
the record of what Round 3 believed, followed by the decompiled mechanism that replaces it:

> **Unverified, stated as an inference, not a fact**: the ~30 DIP gap between the declared
> `MinHeight="568"` and the measured 908-physical floor lines up numerically with
> `GetSystemMetricsForDpi(SM_CYMINTRACK, 144)` = 56 physical px added to `MinHeight`×scale
> (568×1.5 = 852; 852+56 = 908, an exact match) — which would mean Avalonia's `WM_GETMINMAXINFO`
> handling *adds* the system minimum-track metric to the declared `MinHeight` rather than taking the
> max of the two. `task-4-report.md` is explicit that this is "an inference from three consistent
> numeric probes, not a confirmed read of Avalonia's own decompiled source," and was reported as a
> finding, not fixed. Also untested: whether the same floor applies at any scale other than the one
> 150% monitor available.

The 56 is a **numeric coincidence**: on this machine it is both `SM_CYMINTRACK(144)` and this
window's `BorderThickness.Top + Bottom`, and it is the latter that Avalonia actually adds.
Decompiling `Avalonia.Win32.dll` 12.1.0 with `ilspycmd` (re-run and re-read while writing this
correction, not carried over from a report), `WindowImpl.WndProc`'s `WM_GETMINMAXINFO` case reads:

```csharp
if (_minSize.Height > 0.0)
{
    ref POINT p = ref structure2.ptMinTrackSize;
    double num15 = _minSize.Height * RenderScaling;
    borderThickness = BorderThickness;
    double num16 = num15 + borderThickness.Top;
    borderThickness = BorderThickness;
    p.Y = (int)(num16 + borderThickness.Bottom);
}
```

`WindowImpl.BorderThickness` is not a system metric — it calls `AdjustWindowRectEx` on the window's
own live style and returns `new Thickness(-left, -top, right, bottom)`. For this window's style that
is L11 T45 R11 B11, so `Top + Bottom` = 56 and `ptMinTrackSize.Y` = 568 × 1.5 + 56 = **908**
physical, exactly the measured outer floor. The real vertical non-client inset is only the 11 px
bottom, because `ExtendClientAreaToDecorationsHint` pulls the 45 px caption *into* the client area —
so the client floor came out at 897 px = 598 DIP against a request of 568, i.e. the caption was
counted twice.

Three consequences, all acted on in Round 4:

1. The floor is a **function of `MinHeight`**, so raising `MinHeight` cannot escape it and no
   scale-dependent constant needs to be hardcoded anywhere. Deleting the attribute makes the guard
   above fail (`_minSize.Height == 0`), Windows falls back to its own default minimum track size, and
   requested height == real height.
2. The width case has no such error and `MinWidth="760"` therefore **stays** declared: horizontally
   the inset Avalonia adds (`Left + Right` = 22) is the whole real inset, so the client floor already
   equals `MinWidth`.
3. "Whether the same floor applies at any scale other than 150%" is answered by the formula rather
   than needing another monitor: the surplus is `BorderThickness.Top + Bottom` at whatever DPI the
   window is on, and it is zero once `MinHeight` is unset.

## `IsSamePlacement`'s comparison basis (finding 4 / residual B)

`WindowHost.FitToContent` used to compare a freshly-computed placement against the window's
**live** `Width`/`Height`. Because the ~605 DIP floor above sat above the fit's own `MinHeight=568`
request, the live (clamped) size and the requested size could never match, so the "free no-op"
skip could never fire — every cold start applied the fit twice.

Fix: capture `_lastFitWidthDip`/`_lastFitHeightDip` — the size the *previous fit itself requested*,
written only after all three property writes (`Width`/`Height`/`Position`) return — and compare
new placements against that instead of the live window size. `null` (nothing has fit yet this
process) always counts as "different," so a process's first fit is never skipped. A separate
`_fitBasisStale` flag (set only for `WindowResizeReason.Unspecified`, established by decompiling
`Win32.WindowImpl` — see below) additionally forces "different" when something outside this class
resized the window since the last fit.

> **Round 4, item 4 updates this in three ways; the request-based rule itself stands.**
>
> 1. **The clamp that motivated it is gone.** With `MinHeight` removed from `MainWindow.axaml` (see
>    the correction above) the platform no longer clamps a fit's request at all, so live size and
>    requested size would now agree. The rule is kept regardless — "compare against what you asked
>    for" is what *the same placement* has to mean, and it no longer depends on a platform quirk to
>    be worth having.
> 2. **The basis is now the whole rectangle.** The two `double?` fields became one
>    `PlacementRect? _lastFitPlacement`. The old skip test built its comparison out of the window's
>    *live* `Position` plus the *previous request's* size — only half request-based, which was
>    tolerable while a fit preserved position. It is not now that position is a function of the size.
>    One behaviour changes with it: a pure MOVE (a title-bar drag, an external `SetWindowPos`) reaches
>    `PositionChanged`, never `Resized` (decompiled `Win32.WindowImpl`: `WM_MOVE` raises
>    `PositionChanged`, only `WM_SIZE` raises `Resized`), so it sets neither `_userResized` nor
>    `_fitBasisStale`; a content-unchanged re-fit therefore now *skips* instead of snapping the moved
>    window back to the anchor. The next summon re-anchors it, because `RestorePlacement` always
>    applies the anchor before `Show`.
> 3. **The anchor applies on every re-fit, not only at restore.** All five surviving re-fit triggers
>    (startup build finished, docked notice opened/closed, launcher rebuild, 设置→应用, Maximized→Normal
>    — the rail toggle was deleted by Round 4, item 1) now **move** the window as well as resizing it:
>    `FitToContent` no longer reads the window's current Left/Top, it re-derives them from the resolved
>    size. A restore-only anchor was not an option — the cold-start placeholder is 1000×688 and the
>    fitted window is neither, so the very first fit would have undone it.
>
> Unchanged: `null` still counts as "different"; the basis is still written only *after* all three
> property writes return; `RestorePlacement` still does not update it.

**Cold-start log evidence.** Pre-fix, the same log already contained the reviewer's own
reproduction, two `[INF]` lines at the identical geometry 3 ms apart (only possible if the second
fit re-applied rather than skipped):

```
2026-08-02 02:58:41.324 ... [INF] Window fit to content: 1724x568 DIP (...)
2026-08-02 02:58:41.327 ... [INF] Window fit to content: 1724x568 DIP (...)
```

Post-fix, a fresh cold start on the same machine produced exactly one `[INF]` fit and one `[DBG]`
skip, 4 ms apart (a skip only ever logs at Debug):

```
2026-08-02 03:45:54.521 ... [INF] Window fit to content: 1724x568 DIP (...)
2026-08-02 03:45:54.525 ... [DBG] Window fit to content: already 1724x568 DIP; nothing to apply
```

## Pin-button theming on a detached settings page (finding 1 / residual A)

`SettingsPage`'s constructor calls each card's `ReloadFromConfig()` — which calls
`BehaviorCard.ApplyTaskbarPinState` — *before* `MainWindow.ShowPageCore` ever assigns the freshly
built page to `PageHost.Content`, i.e. while the whole card tree is still detached from the
logical tree. Decompiled Avalonia 12.1.0: `StyledElement.StylingParent` walks
`InheritanceParent`, which a detached control terminates before ever reaching `Application.Styles`
(where `TmButton`/`TmAccentButton` live), so `this.FindResource(...)` returns
`AvaloniaProperty.UnsetValue` and `as ControlTheme` used to yield a hard `null` — clobbering
whatever theme the button already had on any call after the very first.

Two-part fix:
1. `ApplyTaskbarPinState` now only assigns `PinTaskbarBtn.Theme` when the `FindResource` lookup
   actually succeeds, so a failed lookup leaves the existing theme alone instead of nulling it.
2. `BehaviorCard.axaml` gives `PinTaskbarBtn` a static default `Theme="{StaticResource TmButton}"`
   (the neutral theme, deliberately, not the accent one) so the *very first* call — before any
   lookup has ever succeeded — also renders correctly rather than falling back to stock Fluent.
   `RefreshTaskbarPinStateAsync` sets `IsEnabled = false` synchronously on attach, so the pre-query
   frame reads as a neutral disabled control, not a dimmed accent-blue swatch.

An A/B capture (fixed build vs. the default `Theme` attribute removed, both cold-started fresh,
~33 ms/frame `PrintWindow` sampling) produced **identical** results in both builds: the WinRT
taskbar-pin query's continuation lands before the newly attached page's first render on this
machine, so the pre-query frame never actually reached the screen to be captured either way. The
fix is therefore justified by the code mechanism (a failed lookup can no longer clobber a good
theme, and the first-ever call now has a defensible default instead of none), not by a captured
frame difference — recorded as "could not verify a differing frame," not claimed as demonstrated.

## Decompiled Avalonia mechanisms

**`InvalidateStyles` does not drop a locally-applied `ControlTheme` (confirmed by decompile).**
Decompiling `Avalonia.Base.dll` 12.1.0 via `ilspycmd`: `StyledElement.InvalidateStyles` does
exactly two things — `ValueStore.RemoveFrames(FrameType.Style)` and `_stylesApplied = false`.
`FrameType` is a three-value enum (`Style`, `TemplatedParentTheme`, `Theme`), and `RemoveFrames`
filters via `FramePriority.IsType`, i.e. `(int)priority % 3 == (int)type` — so it removes **Style**
frames only. A `ControlTheme` applied through an element-local `Theme` value is installed as a
`FrameType.Theme` frame (`ApplyControlTheme(theme, FrameType.Theme)`, reached because
`GetEffectiveTheme()` returns the local `Theme` before it ever consults the implicit one); theme
frames are dropped only by `OnControlThemeChanged` (the `Theme` property itself changing) or by
`ReevaluateImplicitTheme` (which runs on attach and short-circuits for an element with a local
`Theme`) — neither runs on detach, and `_themeApplied` is not cleared either.

This disproves an earlier claim that `MainWindow`'s `RefitToContent` guard rested on: that
detaching the page from `PageHost.Content` (which calls `InvalidateStyles` on every descendant)
strips app-page tiles of their 168 DIP width. It does not — that width is a `Width` Setter on the
`TmTileButton` `ControlTheme`, applied element-locally (`Theme="{StaticResource TmTileButton}"` in
`Views/AppPage.axaml`), i.e. a `FrameType.Theme` frame that survives the detach. What *is* lost on
detach is every app-level, **class-scoped** style frame the page depends on — `Image.tm-tile-icon`'s
20×20 icon box, `TextBlock.tm-section`'s size/line-height/weight, `Border.tm-section-head`'s
padding, `StackPanel.tm-group`'s alignment, the search field's geometry, and so on — which of those
specifically produced the corrupted measurement below was not isolated.

> **Superseded in part by Round 4, item 3** (`docs/superpowers/specs/2026-08-02-win11-shell-round4-design.md`
> §3.4). The decompiled `FrameType` mechanism above stands unchanged. The 168 DIP example does not:
> that `Width` Setter is deleted, so a tile is sized by its content, and both terms of that content —
> the class-scoped `Image.tm-tile-icon` box listed here as *lost* on detach, and the label's face,
> which `MainWindow.ApplyWindowFont` sets on the window rather than on the tile — are things the
> detach breaks. A detached measurement now corrupts **width as well as height**, which makes
> `RefitToContent`'s guard more load-bearing than this section left it, not less.

**`DispatcherPriority.Loaded` ordering — confirmed by decompile.** The comment on
`MainWindow.InitializeAndFitAsync` (and the identical claim in `2af2401`) asserts that Avalonia
processes pending layout invalidations before running Loaded-priority work, which is why deferring
the cold-start fit to `DispatcherPriority.Loaded` (matching every other re-fit trigger) measures
the already-current, re-templated visual tree instead of racing it.

The claim holds by construction, and the chain is re-verifiable in the pinned Avalonia 12.1.0:
`Avalonia.Layout.LayoutManager.QueueLayoutPass()` calls
`MediaContext.Instance.BeginInvokeOnRender(...)`, which calls `MediaContext.ScheduleRender(now: true)`;
that sets `priority = DispatcherPriority.Render`, and when an operation is already queued it *raises*
the queued one to `Render`. The branch that would demote to `DispatcherPriority.Input` is guarded by
`!now`, so it is unreachable from `BeginInvokeOnRender`. `DispatcherPriority.Loaded` compares strictly
below `DispatcherPriority.Render`, so any layout invalidation queued by the synchronous binding
updates in `ReplaceGroups` is dequeued and executed *before* the Loaded-priority job. The deferral
therefore closes the race rather than usually winning it.

Provenance, so a future maintainer can weigh it: this chain was traced by the Round 3 whole-round
review, not re-derived when this record was written. It is corroborated empirically by three fresh
cold starts, each reporting the correct capped page height on the very first fit. The same decompile
also confirms the pre-existing claim at `WindowHost.cs` that `Default` priority is lower than `Loaded`.

**Non-`User` resize reasons.** Decompiling `Win32.WindowImpl`: it keeps one `_resizeReason` that
`WM_SIZE` hands straight to `Resized`, which is `User` only between `WM_ENTERSIZEMOVE`/
`WM_EXITSIZEMOVE`, `DpiChange` only inside `WM_DPICHANGED`'s scope, and `Application`/`Layout` only
inside the scope `WindowImpl.Resize` wraps `SetWindowPlacement` in — the only route this app's own
`Width`/`Height` writes take. So gating `_fitBasisStale` on `Unspecified` catches foreign resizes
(Win+arrow snap, an external `SetWindowPos`, un-maximizing by caption) without being tripped by the
app's own writes.

## The 850×568 DIP `RefitToContent`-guard reproduction

`MainWindow.RefitToContent`'s guard against measuring `_appPage` while 设置 (not 应用) is the hosted
page is load-bearing, not defensive tidiness: temporarily removing it reproduced the window
latching at a wrong **850×568 DIP** for the rest of the session (568 being the app's own minimum
height, declared as `MainWindow.axaml`'s `MinHeight` at the time and moved to
`WindowHost.MinWindowHeightDip` by Round 4, item 4 — i.e. a measurement that had collapsed onto the
floor). That reproduction is the
observed fact the guard rests on; per the decompile finding above, which class-scoped style frame
specifically produced 850×568 was not isolated and is not claimed.

## Round 3 commits

Baseline `646b79e` (162 tests) through:

| Commit | Content |
|---|---|
| `8b4c1a2` | Task 1: caption buttons flush to the title band's top edge, search-box focus ring |
| `d2ef421` | Task 1 review fix: centre the title-bar search box, match Task Manager's width |
| `d107349` | Task 2: rail parity with Task Manager, overflow menu moves into the rail |
| `dcc8359` | Task 2 review fix: stop `ShowPage` re-entering itself, re-measure the search-box centre |
| `ba005df` | Task 3: stretch settings cards to width, fix heading hierarchy, disable pin button when pinned |
| `2af2401` | Task 4: items per column is a setting again; remove button size (162 → 178 tests) |
| `413b5a3` | Final fix wave: pin theme, binary NUL bytes, stale docs, double re-fit |
| `725f408` | Residual fix wave: pin-button first frame, fit-basis ordering, decompile-checked docs (178 → 183 tests) |

Final state: **183 tests passing**, Release NativeAOT publish at 0 warnings.
