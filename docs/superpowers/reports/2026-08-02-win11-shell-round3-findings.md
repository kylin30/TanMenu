# Win11 shell Round 3 — final-fix-wave findings

Condensed from `.superpowers/sdd/2026-08-01-win11-shell-round3/final-fix-report.md` and
`progress.md` (and, for the height-floor measurement, `task-4-report.md`), a git-ignored scratch
workspace that was deleted after Round 3 closed. This is the durable record several source
comments cite in place of that workspace; it keeps only the facts those comments rely on, not the
subagent process narrative around them.

Round 3 plan: `docs/superpowers/plans/2026-08-01-win11-shell-round3.md`. Baseline `646b79e` (162
tests) through `725f408` (183 tests) — see "Round 3 commits" below.

## The ~605 DIP practical height floor

`MainWindow.axaml` declares `MinHeight="568"`. On the one 150%-scale (144 DPI) monitor available
during Round 3, that height is not actually reachable: a `SetWindowPos` probe that bypassed all
app code found the window physically cannot render below **908 physical / 605.3 DIP outer, 897
physical / 598 DIP client** — Windows clamps any smaller request and writes the clamped value back
into `Width`/`Height`. Probing the exact threshold found the cliff at exactly 908/910
(`task-4-report.md`).

`WindowHost.FitToContent`'s own arithmetic is correct at every `ColButtonCount` tested — a clean
40 DIP/tile + ~71 DIP constant (raw page height 191.33 DIP at N=3, 271.33 at N=5, 471.33 at N=10,
871.33 at N=20, `task-4-report.md`) — the window simply cannot render shorter than its own floor.

**Consequence for `GeneralConfig.ColButtonCount`**: values of roughly 13 and below all resolve to
that same ~598 DIP client height, so the setting only becomes height-*effective* above that point.
Below it, lowering the value still visibly changes layout, just by trading height for width
instead: fewer rows per column means more columns across. Reproduced live with
`colButtonCount: 3`, which produced a **4648×568 DIP** window (`final-fix-report.md`,
`progress.md`) — on this machine's 5120×1392 DIP work area at 150% scale, per `task-4-report.md`'s
environment note (the two figures were not captured in the same probe, but are the same machine
and monitor throughout Round 3). This is not a defect: nothing in the Round 3 plan required the
setting to be height-effective across its whole [1, 30] range, only that the *default* not need a
horizontal scrollbar, which holds.

**Unverified, stated as an inference, not a fact**: the ~30 DIP gap between the declared
`MinHeight="568"` and the measured 908-physical floor lines up numerically with
`GetSystemMetricsForDpi(SM_CYMINTRACK, 144)` = 56 physical px added to `MinHeight`×scale
(568×1.5 = 852; 852+56 = 908, an exact match) — which would mean Avalonia's `WM_GETMINMAXINFO`
handling *adds* the system minimum-track metric to the declared `MinHeight` rather than taking the
max of the two. `task-4-report.md` is explicit that this is "an inference from three consistent
numeric probes, not a confirmed read of Avalonia's own decompiled source," and was reported as a
finding, not fixed. Also untested: whether the same floor applies at any scale other than the one
150% monitor available.

## `IsSamePlacement`'s comparison basis (finding 4 / residual B)

`WindowHost.FitToContent` used to compare a freshly-computed placement against the window's
**live** `Width`/`Height`. Because the ~605 DIP floor above sits above the fit's own `MinHeight=568`
request, the live (clamped) size and the requested size could never match, so the "free no-op"
skip could never fire — every cold start applied the fit twice.

Fix: capture `_lastFitWidthDip`/`_lastFitHeightDip` — the size the *previous fit itself requested*,
written only after all three property writes (`Width`/`Height`/`Position`) return — and compare
new placements against that instead of the live window size. `null` (nothing has fit yet this
process) always counts as "different," so a process's first fit is never skipped. A separate
`_fitBasisStale` flag (set only for `WindowResizeReason.Unspecified`, established by decompiling
`Win32.WindowImpl` — see below) additionally forces "different" when something outside this class
resized the window since the last fit.

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
latching at a wrong **850×568 DIP** for the rest of the session (568 being `MainWindow.axaml`'s own
`MinHeight`, i.e. a measurement that had collapsed onto the floor). That reproduction is the
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
