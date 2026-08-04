# Round 8 — the classic main-menu shell, the Options dialog, and the strip

> **Status: IMPLEMENTED.** All nine tasks shipped, `bf770d7`..`7b655c9` plus this document's own commit.
> **222 tests pass, version 0.14.0.** §10 is the record: what shipped, every number this design got
> wrong, the two environment facts the round established, and what is knowingly left open. Where §10
> and §§0-9 disagree, §10 is the later reading — and the **code is the authority over both**. Nothing
> in §§0-9 was rewritten to match the build; a design is a record of what was decided, and correcting
> it in place would erase the decisions the tasks actually departed from.

Design for the user's five items. Written against the tree at `38589ed` (main, clean, 194 tests passing,
version 0.13.0).

Every number below carries one of five labels, used exactly as Round 7's records used them:

- **PUBLISHED** — a verbatim sentence or table cell from a Microsoft document, cited by URL.
- **MEASURED** — read off an instrument, with the instrument named.
- **VENDORED** — read out of `Splitwirez/aero-theme-for-avalonia` at the pinned `a705b7c0`. That is not
  Microsoft and not Windows 7; it is one person's reading of `aero.msstyles`.
- **file:line** — read out of this tree.
- **PREDICTED** — arithmetic on the above, not yet observed. Every prediction says so.

Where something could not be verified, it says **COULD NOT VERIFY** rather than being filled in.

---

## 0. Corrections to the reconnaissance, and the settled rulings

Three statements carried into this round do not match the tree, and the design does not inherit them.

| Claim | Tree | Evidence |
|---|---|---|
| "the user wrote 右边导航" | the pane is on the **LEFT** | `MainWindow.axaml:392` `ColumnDefinitions="Auto,*"`, `NavRailPane` at `Grid.Column="0"` (`:502`), `PagePane` at `Grid.Column="1"` (`:773`) |
| "`Themes/Aero/ControlStyles/` has 17 files" (r8-win7 §5.1) | it has **14** | `ls src/TanMenu.Wpf/Themes/Aero/ControlStyles/`; `AeroResources.axaml:199-213` merges Metrics + those same 14 |
| "upstream's `Menu.axaml` … is **not established** here" (r8-shell §6.2) | it exists and was read | GitHub contents API, `src/AeroAvalonia/ControlStyles` at `ref=a705b7c0`, run this session: `Menu.axaml`, `RadioButton.axaml`, `TabControl.axaml`, `TabItem.axaml`, `Window.axaml` are present; `ComboBox*.axaml` are not (confirming `AeroAttributionTests`' `NotVendored` note) |

The rulings this design is built on, restated so nothing below has to re-argue them:

1. The navigation pane is on the **left**, and item 2 deletes it.
2. The menu carries **five** commands: 设置、主目录、刷新、退出、关于. 刷新 is in the list because
   `RefreshAsync` (`LauncherViewModel.cs:506-517`) is the only **user-reachable** caller of
   `ReloadAsync(force: true)` — the other caller is `InitializeCoreAsync` (`:539`), the once-per-process
   startup build, which no user can ask for again — and `force` is the only way past `MenuService`'s
   fingerprint gate (`src/TanMenu.Wpf/Services/MenuService.cs:307-332`), whose own comment names a case
   it cannot catch: *"A target exe overwritten in place under the SAME .lnk still needs a manual 刷新"*
   (`MenuService.cs:523-524`).
3. The settings dialog uses **delayed commit**: 确定 / 应用 commit, 取消 discards.
4. `ToggleSwitch` → `CheckBox`.
5. Item 1 is not absolute. **Every retention is named with the reason it is not cosmetic** (§4).
6. The search field lives in the menu band **immediately after 关于**, with the separator between them
   and the whole group **left-aligned** — the request was 放在"关于"后面, and a right-aligned field puts
   the separator a window-width away from 关于, which is a different layout wearing the same words. A
   right-aligned variant (Office XP/2003's *"Type a question for help"* box, the one adjacent Microsoft
   precedent) was considered and **rejected as a departure from the request**. **No Microsoft-shipped
   Windows 7 application was found with a text field in its menu bar (r8-win7 §2.3, COULD NOT VERIFY),**
   so this is built as a **documented extrapolation**, not a reproduction, and §3.3 says so again where
   the geometry departs from the OS metric.
7. **Menu bar, not toolbar — SETTLED, and it is a deliberate departure from the guideline.** The user
   asked for "windows 经典的 mainmenu" in those words, so the row is a `Menu`. Three PUBLISHED sentences
   point the other way and are recorded here rather than argued away: `cmd-menus`'s *"The presence of
   menus goes without saying in a menu bar"* (four of the five items are direct commands, which violates
   that affordance); `cmd-toolbars`'s *"Partial toolbars can be combined with navigation buttons, a
   search box, or tabs"* (which sanctions exactly this row's shape, and whose Windows 7 exception
   right-aligns "standard, well-known commands such as Options, View, and Help" — i.e. 设置 and 关于);
   and the fact that a toolbar has **no** published height, so the 20-vs-23 arithmetic that forces the
   band to 25 DIP (§3.3) would not arise at all. Building it as a toolbar would satisfy every published
   sentence and depart only from the user's word. **The user's word wins; the three sentences are the
   cost, stated.**
8. **The Options dialog's size stays provisional at `540 × 540`, with T2 measuring it** (§7). If the
   tabs do not fit inside the PUBLISHED 800 × 600 floor, the ruling is **split the 文件夹 tab in two**
   (six tabs after T4's 关于 leaves, still ≤ 7) rather than accept scrolling — `win-property-win`'s
   *"Don't scroll property pages"* is kept.

---

## 1. The line item 1 draws

`Themes/Aero/` **is the theme**; item 1 must not touch it. Inside `Themes/Shell/` the test is:

> **A rule that restates a value the vendored theme already sets for that same control type is
> cosmetic and goes. A rule that gives a PRIMITIVE its geometry or its fill is not "layered on the
> theme" at all — there is no theme value under it.**

The second half is a fact about this tree, not a convenience. `AeroResources.axaml:199-213` merges
exactly fourteen `ControlStyles` dictionaries, and every `ControlTheme` in them has a
`TargetType` that is a *control*: Button/RepeatButton/ToggleButton, ButtonSpinner, CheckBox, ComboBox,
ComboBoxItem, HeaderedContentControl, ListBox, ListBoxItem, NumericUpDown, ProgressBar, ScrollBar,
ScrollViewer, TextBox, ToggleSwitch. Nothing themes a `Border`, a `TextBlock`, a `StackPanel`, an
`Image` or a `Panel`. Delete `Border.tm-toast` and the toast paints nothing; delete
`Image.tm-tile-icon` and the tile loses its 20 DIP icon slot. Those are not styles on the theme.

The in-tree precedent for what item 1 *does* want is already shipped and named: `Button.tm-update`
(`Launcher.axaml:870-876`) carries **no** `Theme=`, rides the vendored `{x:Type Button}`, and
deliberately omits `Background`, `BorderBrush`, `BorderThickness` and `CornerRadius`.

§4 applies this test to all 57 blocks with line counts.

---

## 2. The Windows 7 references this round is designed to

Only the statements the design actually leans on; the full survey is r8-win7.

**Menu bar.** A top-level item that is a direct command is legal and rare — PUBLISHED,
<https://learn.microsoft.com/en-us/windows/win32/menurc/about-menus>: *"A menu bar typically contains
menu names, but it can also contain command items."* and *"An item on the menu bar almost always opens
a menu. Menu bars rarely contain command items."* The UX Guide's affordance rule cuts the other way —
PUBLISHED, `cmd-menus`: *"The presence of menus goes without saying in a menu bar."* **Accepted
deviation**, settled in §0 ruling 7 with the two other published sentences that point at a toolbar.
`cmd-menus` also places every one of the five: Exit under File, Options
under Tools, About under Help, and sanctions the fifth — *"Don't feel obligated to use the standard
menu organization if it isn't suitable for your program."* Labels take **no ellipsis** (PUBLISHED,
`cmd-menus`: About/Options *"must display another window when clicked, but don't require additional
information from the user. Therefore they don't need ellipses."*) and the English string for 设置 is
**Options**, not Settings (PUBLISHED, `cmd-menus`: *"Settings — Don't use as a menu label. Use Options
instead."*). Access keys on all of them, no exceptions (PUBLISHED, `inter-keyboard`).

**A separator cannot be used in a menu bar** — PUBLISHED, `about-menus`. Item 5 asks for one anyway;
§3.3 builds it as a **toolbar** separator instead (`parts-and-states` gives `TOOLBAR` the part
`TP_SEPARATORVERT`), which is the correct Win32 vocabulary for a vertical divider.

**Property windows.** PUBLISHED, <https://learn.microsoft.com/en-us/windows/win32/uxguide/win-property-win>:
*"Options dialog boxes. Properties for an application are displayed in a modal dialog box."* and
*"All property window patterns except property inspectors use a delayed commit, meaning that changes
take effect only when users click OK or Apply."* Button order, stated identically in three articles:
*"Right-align the buttons and use this order (from left to right): OK, Cancel, and Apply."* Tabs:
*"Use horizontal tabs if the property window has seven or fewer tabs."* Scrolling: *"Don't scroll
property pages."* Title: *"Options dialog box titles — Use 'Options.'"* About: *"Is the content solely
an application's attributes? If so, use an About box."*

**Sizing, verbatim from** <https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-layout>
(relative pixels, 9 pt Segoe UI at 96 dpi): dialog margins **11**; between related controls **7**;
between unrelated controls **11**; between buttons **7**; smallest space between a non-interactive
control and any other control **3**; from the left edge of a group box **9**; first control in a group
box **16** down; **command buttons 75 × 23**; **text boxes … × 23** (one line); check boxes **17**;
static text **13**.

**The OS metric.** MEASURED this session (r8-win7 §1.3, `SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS)`
via P/Invoke from PowerShell 7, **on Windows 11, not Windows 7**): `SM_CYMENU` = 20 @96 dpi,
`NONCLIENTMETRICS.iMenuHeight` = 19, `lfMenuFont.lfHeight` = −12 (= 9 pt). VENDORED corroboration:
upstream `ControlStyles/Menu.axaml` declares `<x:Double x:Key="MenuBarHeight">20</x:Double>` — fetched
and read in full this session.

---

## 3. The task sequence

Nine tasks. Each leaves the app buildable, runnable and shippable. Risk first.

### T1 — The pending-changes buffer and the commit model

**This is the round's only behaviour change and the only task that touches persistence. It ships first
so that if a draft cannot satisfy the config contract, nothing else has been built on it.** The
settings surface is still a page inside the shell after T1; only the commit model changes.

#### Decision

New `src/TanMenu.Wpf/Views/Settings/SettingsDraft.cs`:

```
internal sealed class SettingsDraft
{
    public AppConfig Working { get; }           // a deep clone of ConfigService.Config
    public bool? AutoStartDesired { get; set; } // null = untouched; the ONE non-config field
    public bool? AutoStartAtSeed { get; }       // the OS state read once, when the draft was seeded
    public bool IsDirty { get; private set; }   // recomputed by Revalidate(), below
    public event Action? DirtyChanged;          // 应用's IsEnabled tracks this

    public static SettingsDraft From(ConfigService live, IAutoStartService autoStart);
    public void Revalidate();                   // recompute IsDirty; raise DirtyChanged if it moved
    public IReadOnlyList<Action> CopyOnto(AppConfig live); // named list only; returns the undo actions
}
```

`IsDirty` is **computed by comparison, not set by a flag**: `Revalidate()` walks the same eleven named
properties `CopyOnto` writes, compares `Working` against the live `AppConfig`, and ORs in
`AutoStartDesired is not null && AutoStartDesired != AutoStartAtSeed`. That is why the cards call
`Revalidate()` rather than a `MarkDirty()` — flipping a check box and flipping it back re-disables 应用,
exactly as it does for the ten config-backed properties, and there is no second source of truth to get
out of step. `DirtyChanged` exists because 应用 lives in a different visual tree from the cards.

- **The clone is a JSON round-trip through the existing source-generated context**:
  `JsonSerializer.Deserialize(JsonSerializer.Serialize(live.Config, ConfigType), ConfigType)!`,
  where `ConfigType` is `ConfigService.cs:18`'s `JsonTypeInfo<AppConfig>` from `ConfigJsonContext`
  (`src/TanMenu.Core/Serialization/JsonContexts.cs:29`). No reflection, so NativeAOT is unaffected, and
  it is the *same* serializer the file on disk goes through — a field that survives a save survives a
  clone, by construction. `ConfigService.CloneConfig()` is **not** revived: its deletion note
  (`ConfigService.cs:38-42`) says it "invites exactly the staged-edit workflow the redesign removed";
  Round 8 restores that workflow, but the helper belongs to the dialog, not to the live config service.
- **`CopyOnto` copies a NAMED LIST, never the whole object.** The list is exactly the properties the six
  cards assign today — verified exhaustively by grepping `_config.Config.… =` and `tool.Show =` across
  `src/TanMenu.Wpf/Views/Settings/*.cs`, ignoring rollback lambdas:

  | Property | Written today at |
  |---|---|
  | `General.Language` | `AppearanceCard.axaml.cs:189` |
  | `General.Appearance` | `AppearanceCard.axaml.cs:205` |
  | `General.FontFamily` | `AppearanceCard.axaml.cs:244` |
  | `General.ColButtonCount` | `AppearanceCard.axaml.cs:266` |
  | `General.AutoClose` | `BehaviorCard.axaml.cs:171` |
  | `General.TopMost` | `BehaviorCard.axaml.cs:188` |
  | `General.GlobalHotkeyEnabled` | `BehaviorCard.axaml.cs:374` |
  | `General.GlobalHotkey` | `BehaviorCard.axaml.cs:395`, `:411` |
  | `General.ShowDefaultTools` | `ToolsCard.axaml.cs:128` |
  | `General.DefaultTools[i].Show` (matched by `Command`) | `ToolsCard.axaml.cs:147` |
  | `RootFolder` | `FoldersCard.axaml.cs:218` |

  **A wholesale object swap would be a real defect, not a style preference.** `WindowHost.SavePlacement`
  writes `General.WindowMaximized` on the live config and debounces a `SaveAsync` (`WindowHost.cs:929-961`);
  swapping in a clone taken before that write would silently revert it. And `ToolsCard`'s own comment
  (`ToolsCard.axaml.cs:139-142`) records the same rule for the tools list — *"never rebuilding or
  re-saving the whole DefaultTools list from a snapshot"* — which per-entry matching preserves.
- **Every card's `_config.Config` reads and writes become `_draft.Working`.** For the ten config-backed
  rows nothing else about the cards changes: the property paths are identical, so `ReloadFromConfig` and
  every `_loading` guard keep working verbatim. **开机自启 is the one exception and it is not a
  search-and-replace — it has no `AppConfig` field at all.** Its rewrite is a bullet of its own below.
  `ISettingsCard` (`ISettingsCard.cs:6-12`) gains no member; it gains one sentence saying it now
  populates from the draft. (Its class summary also names `SettingsPage` as the discoverer, which T2
  deletes — corrected there, not here.)
- **The per-control `SettingsWrite.CommitAsync` calls are deleted** and replaced by `_draft.Revalidate()`.
  There are **12** call sites in the six cards, counted from the tree this session:
  `AppearanceCard.axaml.cs:190`, `:208`, `:245`, `:267`; `BehaviorCard.axaml.cs:172`, `:189`, `:376`,
  `:397`, `:413`; `FoldersCard.axaml.cs:221`; `ToolsCard.axaml.cs:130`, `:148`. (`AboutCard` and
  `UpdatesCard` have none — each says so in its own class summary.) Each `rollback:` lambda is deleted
  with its call — under delayed commit there is nothing optimistic to roll back. **Deleting these 12
  does not reach 开机自启: `BehaviorCard.OnAutoStartChanged` (`:206-224`) contains no `CommitAsync` call
  to delete**, which is exactly why it needs its own step.
- **`SettingsWrite.CommitAsync` itself SURVIVES UNCHANGED, with exactly one caller left**:
  `AppPage.axaml.cs:204`, the taskbar-pin InfoBar's 不再提示. That is a launcher-page action, not a
  property-window property, and immediate-apply is correct for it. Its doc comment is updated to say so.
- **New `SettingsCommit.ApplyAsync(draft, config, events, host, owner)`**:
  1. `draft.CopyOnto(config.Config)`
  2. `await config.SaveAsync()`; on failure, **re-read the live config from the untouched fields is not
     possible** — instead the pre-copy values are captured in `CopyOnto`'s return and restored, then the
     existing `MessageBox` + `SuppressHide(true/false)` bracket from `SettingsWrite.cs:34-44` is reused
     verbatim. The invariant that comment defends — *"the screen never silently shows a value that is not
     on disk"* — is satisfied the same way, one commit instead of one per control.
  3. If `draft.AutoStartDesired` is non-null and differs from `IAutoStartService.IsEnabled()`, call
     `SetEnabled(desired)` inside a try/catch that **logs and never dialogues** —
     `BehaviorCard.axaml.cs:194-205`'s stated contract, moved, not weakened. Then set
     `draft.AutoStartDesired = null`, which is what makes step 5's re-population an **unconditional
     re-verification against the OS** rather than a re-display of the click (see below).
  4. `AppTheme.Apply(app, draft.Working.General.Appearance)` — **moved here** from
     `AppearanceCard.axaml.cs:206-207`.
  5. `events.RaiseSettingsChanged()`, then re-populate every open card (`ISettingsCard.ReloadFromConfig`)
     and `draft.Revalidate()`, so 应用 goes disabled and 开机自启 re-reads the OS.

#### 开机自启 under delayed commit — the handler, the read path, and where the OS write happens

**Today it is not buffered at all, and no instruction above reaches it.** `OnAutoStartChanged`
(`BehaviorCard.axaml.cs:206-224`) calls `_autoStart.SetEnabled(desired)` on the click and contains **no
`SettingsWrite.CommitAsync` call**, so "delete the CommitAsync call sites" leaves the registry write
firing on click. And `RefreshAutoStartState` (`:96-115`) sets `AutoStartToggle.IsChecked` from
`_autoStart.IsEnabled()`, i.e. straight off the OS, so a buffered desire would be clobbered the next time
anything called it — `OnAttachedToVisualTree` (`:83`) and `ReloadFromConfig` (`:141`) both do. Three
things therefore change, and they are the **named writer**, the **named reader**, and the **commit**:

- **The writer — `OnAutoStartChanged` (`:206-224`).** Its body becomes, in full:

  ```csharp
  if (_loading || _isPortable)
      return;
  _draft.AutoStartDesired = AutoStartToggle.IsChecked == true;
  _draft.Revalidate();
  ```

  The `SetEnabled` call, its try/catch, and the trailing `RefreshAutoStartState()` all go. It was
  already a plain `void` handler with no `RunSafely` wrapper — unlike the five around it — because it
  never awaited anything; it still doesn't, and now it cannot throw either, so that stays as it is. The
  `_loading` and `_isPortable` guards are kept verbatim (portable builds never write a desire, which is
  what keeps `AutoStartDesired` null there and out of `IsDirty`). This is the **only** writer of
  `SettingsDraft.AutoStartDesired`.
  Its 12-line doc comment (`:194-205`) is **rewritten, not moved**: every sentence in it is about a
  registry write this method no longer performs. What survives is the *reason* — the packaged
  `StartupTaskAutoStartService` swallows every exception and never inspects the `StartupTaskState` its
  own WinRT call returns, so a silent policy refusal looks like success — and that reason now belongs to
  `SettingsCommit.ApplyAsync` step 3, which is where the write and the re-verification live.

- **The reader — `RefreshAutoStartState` (`:96-115`), renamed `ApplyAutoStartRow`.** One line changes:

  ```csharp
  AutoStartToggle.IsChecked = !_isPortable && (_draft.AutoStartDesired ?? _autoStart.IsEnabled());
  ```

  `IsEnabled = !_isPortable`, the try/catch around the OS read, and the **save-and-restore** of
  `_loading` (not a bare clear — `ReloadFromConfig` calls this from inside its own guarded block, `:141`)
  are all kept verbatim; the remark at `:90-95` that explains the save-and-restore stays true. The OS is
  now read **only when nothing is pending**, so it can no longer overwrite a desire — which is what makes
  the existing `OnAttachedToVisualTree` (`:80-85`) and `ReloadFromConfig` (`:141`) call sites correct
  without being touched. This is the **only** reader of `AutoStartDesired`.

- **The commit — `SettingsCommit.ApplyAsync` step 3, above.** That is the single place
  `IAutoStartService.SetEnabled` is called in the whole app after T1. Because step 3 clears
  `AutoStartDesired` back to null *before* step 5 re-populates the cards, step 5's `ReloadFromConfig` →
  `ApplyAutoStartRow` falls through to `_autoStart.IsEnabled()` and pushes the **live OS truth** onto the
  check box on both the success and the failure path. That is the same unconditional re-verification
  `:221-223` performs today, relocated — and it still catches
  `StartupTaskAutoStartService` silently no-op-ing on the packaged build, which is the defect the
  existing remark (`:194-205`) exists for.

- **取消 needs no undo**, because nothing was written. That is the whole point: one check box firing a
  registry write on click, next to four that wait for 确定, is exactly the incoherence delayed commit
  exists to remove.

#### The config write is atomic. The OS effects are not.

`ConfigService.SaveAsync` (`ConfigService.cs:135-186`) is **one** serialize plus one `File.Replace` swap
(with a documented plain-overwrite fallback on FAT32/exFAT/SMB, `:158-166`), so all eleven properties
land together or none of them do. Everything downstream of it does not have that property, and the
design has to say what a partial failure means:

- **The config is the record; the OS effects are consequences of it.** If step 2 returns, the commit
  succeeded — all eleven properties are on disk and 应用 goes disabled — even if a later step's OS effect
  fails. A failing OS effect does **not** roll the config back, and must not: `GlobalHotkeyService`
  already refuses to do that in its own words (`GlobalHotkeyService.cs:119-121`, *"We deliberately do NOT
  modify or persist config here: a transient conflict at startup must never permanently disable a working
  hotkey"*). What the user gets is a **notification**, not a rollback. Three fields committed and the
  fourth's OS effect failing is a **committed** state with a warning, not a half-commit.
- **Only one OS effect can both fail and want a dialog.** `GlobalHotkeyService.Apply` is subscribed to
  `SettingsChanged` in the constructor (`GlobalHotkeyService.cs:42`); on a rejected `RegisterHotKey` it
  raises `RegistrationFailed` (`:126-127`) → `App.WarnHotkeyRegistrationFailedAsync`
  (`App.axaml.cs:263-278`) → `MessageBox.ShowAsync(host.Window, …)` → `ShowDialog` **on the launcher
  window**, which the modal Options dialog has already disabled. The autostart write (step 3) is logged
  and never dialogued; `AppTheme.Apply`, `WindowHost.ApplyTopmost`, `TrayService.ApplyMenuText` and the
  view-model rebuild have no failure path that wants one.
- **Re-owner that dialog to the Options window while it is open.** `OptionsWindow` already needs a static
  single-instance field (§T2); it exposes it as `OptionsWindow.Current`, and
  `WarnHotkeyRegistrationFailedAsync` resolves its owner as `OptionsWindow.Current ?? host.Window`.
  `MessageBox.ShowCoreAsync`'s hidden-owner fallback (`Dialogs/MessageBox.cs:84-97`) still covers both.
  Nesting is safe: `WindowHost.SuppressHide` is **reference-counted, not a flag** (`WindowHost.cs:1113-1125`),
  so the warning's own `SuppressHide(true/false)` bracket cannot release T2's outer one.
- **The timing falls out correctly either way.** The warning is `Dispatcher.UIThread.Post`ed
  (`App.axaml.cs:165-166`), so it lands *after* `ApplyAsync` returns. On 应用 the dialog is still open and
  `OptionsWindow.Current` is non-null, so the warning owns to the dialog. On 确定 the dialog has closed,
  `Current` is null, and the owner falls back to the launcher — which is enabled again by then. 确定
  therefore **awaits `ApplyAsync` before it closes**, so the ordering is deterministic rather than a race.

#### Two things the commit model does NOT change

- **Commands stay immediate.** 固定到任务栏, 打开主目录, 打开/更改/重置数据文件夹, 清除缓存, 备份, 恢复,
  检查更新, 下载/安装更新 are commands, not properties; `win-property-win`'s delayed-commit rule is about
  property *changes*. Three of them rewrite `config.json` on disk (`FoldersCard.OnChangeDataClick:230`,
  `OnResetDataClick:260`, `OnRestoreConfigClick:383`), so **each re-seeds the draft when it completes** —
  a fresh `SettingsDraft.From(config, autoStart)`, which re-clones the config, re-reads the OS autostart
  state into `AutoStartAtSeed`, drops any pending `AutoStartDesired`, and leaves `IsDirty` false — or a
  later 确定 would copy a stale draft over the restored file.
- **The page grows the commit row** (its final home is T2's dialog; putting it here is what makes T1
  shippable): 确定 / 取消 / 应用, right-aligned, in that order, `75 × 23` each, `Spacing="7"`,
  `Margin="0,11,0,0"` — all PUBLISHED, §2. 应用 disabled until `IsDirty`. On the page, 取消 re-seeds
  the draft rather than closing anything.

#### Why

`win-property-win`'s delayed-commit sentence is unambiguous and the user chose it. The draft is a full
`AppConfig` clone rather than a bespoke record because that is the only shape under which the **ten
config-backed rows** change by search-and-replace instead of by rewrite — the `_loading` guards, the
reentrancy note at `BehaviorCard.axaml.cs:226-229` ("do not 'fix' this"), and `AppearanceCard`'s
`_fontSelectionGuard` all defend *control* reentrancy, which is unaffected by where the value is stored.
The eleventh row, 开机自启, is the one the clone cannot carry, and it is the one rewritten by hand above.

The named-property commit list rather than an object swap is forced by `SavePlacement`, above. It is
also the only version that is testable without a UI.

#### Blast radius

Files: `Views/Settings/SettingsDraft.cs` (new), `SettingsCommit.cs` (new), `SettingsWrite.cs` (comment
only), `ISettingsCard.cs` (comment only), `AppearanceCard.axaml{,.cs}`, `BehaviorCard.axaml{,.cs}`,
`FoldersCard.axaml{,.cs}`, `ToolsCard.axaml{,.cs}`, `UpdatesCard.axaml.cs`, `AboutCard.axaml.cs`,
`SettingsPage.axaml{,.cs}`.

Invariants that move: the immediate-apply-with-rollback contract (`SettingsWrite.cs:9-12`) stops
governing the settings surface and keeps governing `AppPage`'s 不再提示. `AppEvents.SettingsChanged`
fires **once per commit** instead of once per control change — `LauncherViewModel.OnSettingsChanged` →
rebuild → `ContentMetricsChanged` → `MainWindow.OnLauncherContentMetricsChanged` → `RefitToContent` is
unchanged in mechanism and fires far less. `MainWindow`'s `ApplyWindowFont` and `ApplyRailText`
subscriptions (`MainWindow.axaml.cs:95-96`) likewise.

**One behaviour is deliberately lost.** 外观 no longer previews live: `AppearanceCard`'s comment
(`:195-197`) says *"The whole app repaints live, before the write even reaches disk"*. Delayed commit
forbids that (*"changes take effect only when users click OK or Apply"*). 应用 is precisely the
affordance that gives it back on demand — which is also the argument that earns Apply its place (§7).

Fit arithmetic: **no term moves.**

Tests: no existing test reads the commit path. New `tests/…/Settings/SettingsDraftTests.cs`, **six**
facts: `Clone_IsIndependentOfTheLiveConfig`, `CopyOnto_TouchesOnlyTheNamedProperties` (asserts
`WindowMaximized`, `TaskbarPinPromptDismissed`, `SchemaVersion` and the retired fields are untouched),
`CopyOnto_MatchesToolsByCommandNotByIndex`, `Clone_RoundTripsEveryRetiredField`,
`AutoStartDesired_IsDirtyOnlyWhenItDiffersFromTheSeed` (set it to the seed value ⇒ not dirty; flip ⇒
dirty; flip back ⇒ not dirty), `CopyOnto_NeverWritesTheAutoStartState` (a fake `IAutoStartService`
records zero `SetEnabled` calls across seed → write desire → `CopyOnto` — the OS write belongs to
`SettingsCommit`, not to the draft).

#### Verification (no keyboard, no mouse)

1. `dotnet test` — 194 + 6 pass.
2. Run the app, open 设置 from the nav pane. Drive 外观's 语言 combo via **UIA
   `SelectionItemPattern.Select`** on its second item; assert with `Get-Content` on
   `Data/cache/logs/log-<date>.txt` that **no** `Config path` save follows and that the launcher's
   own labels have not changed (UIA `Name` on `NavAppsItem` still reads the old language).
3. Invoke 应用 via **UIA `InvokePattern`**; assert the log now shows the save and that
   `NavAppsItem`'s UIA `Name` has changed. Assert `应用` reports `IsEnabled=false` afterwards
   (UIA `IsEnabledProperty`).
4. Change 语言 again, invoke 取消, assert the combo's selection returns to the committed value and no
   save appears in the log.
5. Read `config.json` before and after and diff: only the committed key changed.
6. **开机自启 is buffered.** Read the current OS state first — `Get-ItemProperty
   HKCU:\Software\Microsoft\Windows\CurrentVersion\Run` for the unpackaged build, the branch
   `RegistryAutoStartService` writes (`App.axaml.cs:115-120` chooses it when there is no package
   identity and the build is not portable). Drive `AutoStartToggle` with UIA
   `TogglePattern.Toggle()`; assert the Run key is **unchanged** and 应用 reports `IsEnabled=true`.
   Invoke 取消; assert the Run key is still unchanged and the check box has returned to the OS state.
   Repeat, invoking 应用 instead; assert the Run key **has** changed and the check box still agrees
   with it (the re-verification). This is the one step that would have passed vacuously before this
   section was rewritten.

---

### T2 — The Options dialog, and the page model's teardown

#### Decision

- Vendor `ControlStyles/TabControl.axaml` (78 lines) and `ControlStyles/TabItem.axaml` (132 lines) from
  `a705b7c0`, byte for byte, with the fixed five-part header `AeroAttributionTests` requires
  (`AeroAttributionTests.cs:103-110`) and `Changes from upstream: none.` **No colour work is needed:**
  every key they name — `AeroTabControlBackground`, `AeroTabControlOuterBorderBrush`,
  `AeroTabControlInnerBorderBrush`, plus the `AeroButton*` family — already exists at
  `Themes/Aero/AVMAP/Brushes.axaml:184-186` and `Variant/Light.axaml:151-155` / `Dark.axaml`. Verified
  by reading both upstream files and grepping the tree this session. Add both to
  `AeroResources.axaml`'s merge list after `ScrollViewer.axaml`.
- New `src/TanMenu.Wpf/Dialogs/OptionsWindow.axaml{,.cs}`. **Modal**, **non-resizable**,
  `WindowStartupLocation="CenterOwner"`, `ShowInTaskbar="False"`, no `Icon`,
  `Background="{DynamicResource SurfaceBaseBrush}"`, **the system caption** — not MainWindow's
  hand-drawn one. Full specification in §7.
- The dialog hosts a `TabControl` with **six** `TabItem`s in today's order: 外观 · 行为 · 文件夹 ·
  常用工具 · 更新 · 关于, each containing the existing card `UserControl` **moved verbatim**. Six ≤ seven,
  so horizontal tabs are the sanctioned form (PUBLISHED, `win-property-win`). 关于 leaves in T4.
- **Where the tab headers' text comes from, and who re-labels them.** Today each group's title is the
  owning card's own `GroupHead` `TextBlock` — six of them, one per card file (`AppearanceCard.axaml:30`,
  `BehaviorCard.axaml:30`, `FoldersCard.axaml:56`, `ToolsCard.axaml:46`, `UpdatesCard.axaml:43`,
  `AboutCard.axaml:51`) — written from each card's `ReloadFromConfig` out of `AppLanguage`
  (`AppearanceCard.axaml.cs:107` `"Appearance"`, `BehaviorCard.axaml.cs:127` `"Behavior"`,
  `FoldersCard.axaml.cs:95` `"Folders"`, `ToolsCard.axaml.cs:61` `"CommonTools"`,
  `UpdatesCard.axaml.cs:88` `"Updates"`, `AboutCard.axaml.cs:46` `"About"`). **T6 deletes five of the six
  group boxes, which deletes those five `GroupHead` elements and breaks those five assignments** — so
  the header text has to have moved off the cards before then, and T2 is where it moves.

  **The `TabItem` headers take exactly those six `AppLanguage` keys, and `OptionsWindow` owns the
  writes.** `OptionsWindow.ApplyText()` assigns all six `TabItem.Header`s (plus the window `Title` and
  the three commit-button labels) from `AppLanguage.Text(key, language)` — no `Binding`, because
  `AppLanguage` has no change notification, which is the same reason `MainWindow.ApplyRailText` exists
  (`MainWindow.axaml.cs:96`). It is called from the constructor and re-called on
  `AppEvents.SettingsChanged`, subscribed in the constructor and **unsubscribed in `OnClosed`** — the
  dialog is per-open (below), so a missed unsubscribe would leak one handler per open. That is the
  re-label path: 语言 changes ⇒ 应用/确定 commits ⇒ `SettingsChanged` ⇒ `ApplyText()` ⇒ the six headers
  change with the cards behind them.

  Each card's own `GroupHead` write **stays as it is in T2** — the group boxes are still there and still
  need their heads — and it is **T6** that deletes the five heads and the five `GroupHead.Text` lines
  with them, at which point the `TabItem` header is the only remaining statement of the group's name.
  文件夹 keeps group boxes (§T6) but not one *named* `GroupHead`; its four inner boxes get their own
  heads, so `FoldersCard.axaml.cs:95` is rewritten rather than deleted.
- **Constructed fresh on every open, disposed on close.** That is what keeps
  `BehaviorCard.OnAttachedToVisualTree` (`:80-85` → `ApplyAutoStartRow` + `RefreshTaskbarPinStateAsync`)
  and `FoldersCard.OnAttachedToVisualTree` (`:82-86` → `RefreshDataInfoAsync`) working with **zero
  changes** — the exact tradeoff commit `28158a8` navigated in the other direction. Cost is nil:
  `AppearanceCard.PopulateFontCombo` already enumerates `FontManager.Current.SystemFonts`
  (`AppearanceCard.axaml.cs:65`) on every `ReloadFromConfig`, i.e. on every `SettingsChanged`, today.
- `SettingsLauncher.OpenCore` is rewritten: `_host.ShowAndActivate(); _host.SuppressHide(true); try {
  await new OptionsWindow(...).ShowDialog(owner) } finally { _host.SuppressHide(false); }`, with the
  hidden-owner fallback copied from `MessageBox.ShowCoreAsync` (`Dialogs/MessageBox.cs:84-97`) —
  `ShowDialog` throws on a non-visible owner and the launcher is routinely hidden. Single-instance
  guarded by a static field, `OptionsWindow.Current`, set in the constructor and cleared in `OnClosed`.
- **`App.WarnHotkeyRegistrationFailedAsync` re-owners to that field.** Its owner becomes
  `OptionsWindow.Current ?? host.Window` (`App.axaml.cs:270`). See §T1's "The config write is atomic. The
  OS effects are not." for why: it is the only `SettingsChanged` subscriber that can fail *and* wants a
  dialog, and the launcher it currently owns to is disabled while this dialog is modal on it.
- **The page model is torn down in this task.** See §6 for the full list.
- The nav pane survives T2 with **four** rows — 应用, 打开主目录, 刷新, 退出 — and its 设置 row becomes a
  fifth `Button` (not a `ListBoxItem`) whose `Click` calls `ISettingsLauncher.Open()`. `NavFooterRail`,
  the second `ListBox`, is deleted here; so is `NavRail`'s `SelectionChanged`. The pane itself dies in T3.

#### Why

`win-property-win` classifies TanMenu's 设置 as *"Properties for an application"* — an Options dialog
box — and assigns it **modal**. `win-dialog-box` pairs modality strictly: *"Modal dialog boxes … Use a
delayed commit model"*, *"Don't use OK buttons in modeless dialog boxes"*, *"Don't use Cancel buttons in
modeless dialog boxes."* Having chosen delayed commit with OK/Cancel in T1, modal is the only sanctioned
pairing.

Tabs rather than one long page because *"Don't scroll property pages"* is PUBLISHED and `SettingsPage`
is a `ScrollViewer` (`SettingsPage.axaml:78`). Six groups fit the ≤ 7 horizontal-tab rule exactly.

The per-open instance is chosen over a cached one because a cached dialog would need `Opened` hooked in
two card files to replace `OnAttachedToVisualTree`, which is more code and one more thing to get wrong.

#### Blast radius

Files: `Themes/Aero/ControlStyles/TabControl.axaml` + `TabItem.axaml` (new, vendored),
`Themes/Aero/AeroResources.axaml`, `Dialogs/OptionsWindow.axaml{,.cs}` (new),
`Services/ISettingsLauncher.cs`, `Services/IShellNavigator.cs` (**deleted**), `Services/WindowHost.cs`,
`App.axaml.cs` (two DI registrations removed; `WarnHotkeyRegistrationFailedAsync`'s owner re-resolved),
`MainWindow.axaml`, `MainWindow.axaml.cs`, `Views/SettingsPage.axaml{,.cs}` (**deleted**),
`Views/Settings/ISettingsCard.cs` (comment only — its class summary says *"SettingsPage discovers its
card groups structurally"*, and `SettingsPage` is deleted here; `OptionsWindow` is the discoverer now),
all six card files (re-parented only).

Fit arithmetic: **`chromeWidth` does not move in T2** — the pane is still 160 and the pane border still
1. `RefitToContent`'s guard is rewritten in remarks only; see §6.

Invariants: `SuppressHide` bracketing must now span the dialog's whole lifetime, exactly as the deleted
`SettingsLauncher.OpenCoreAsync` did before `28158a8` — otherwise a user with 失焦自动隐藏 on loses the
launcher the instant the dialog takes focus. `FoldersCard`'s four `TopLevel.GetTopLevel(this)` picker
calls (`:206`, `:418`, `:441`, `:461`) and `SettingsWrite`'s owner resolution (`:37`) all now resolve to
the **dialog**, which is correct for ownership.

Tests: `ShellConstantTests.NoTemplatePartSelector_NamesAPartOutsideTheAllowList` must keep its **empty**
allow-list — it does, because `TabItem.axaml`'s `Border#Outer` / `Border#Inner` /
`ContentPresenter#PART_ContentPresenter` and `TabControl.axaml`'s `ItemsPresenter#PART_ItemsPresenter`
are all declared inside those files' own `ControlTemplate`s, and its `TemplatePartNames` scan covers
every `*.axaml` under `src/` (`ShellConstantTests.cs:160`). `AeroAttributionTests` ×2 now cover **23**
vendored files instead of **21**. (Counted from the test, not estimated: `VendoredFiles()`
(`AeroAttributionTests.cs:93-96`) is everything under `Themes/Aero/` **minus** the three names in
`NotVendored` (`:58-79` — `LICENSE-aero-theme-for-avalonia.txt`, `AeroResources.axaml`,
`ComboBox.axaml`). The tree holds 24 files there today, so the vendored set is **21**, not 24. T2 adds
two, T3 adds one: **21 → 23 → 24**.) New
`OptionsWindowTests.CommitRow_IsOkCancelApplyRightAligned` (source test on the XAML: three buttons,
that order, `Width=75 Height=23`, `Spacing=7`, `HorizontalAlignment=Right`).

#### Verification

1. `dotnet test`.
2. Run the app. Drive the nav pane's 设置 row with **UIA `InvokePattern`**. Assert a second top-level
   window appears in the UIA tree with `Name` = 选项 / Options, `ClassName` matching Avalonia's window
   class, and that its `BoundingRectangle` is centred on the launcher's — `|dialogCentre − ownerCentre|
   ≤ 1 DIP` on both axes.
3. Assert the launcher's own UIA element reports `IsEnabled=false` while the dialog is up (modality).
4. Enumerate the dialog's `TabItem`s via UIA: six, in the stated order, each with a non-empty `Name`
   equal to `AppLanguage.Text(key, language)` for the six keys named above. Drive tab 3 with
   `SelectionItemPattern.Select` and assert `PART_SelectedContentHost`'s subtree changes.
5. **The re-label path.** With the dialog open, change 语言 on the 外观 tab and invoke 应用. Assert all
   six `TabItem` `Name`s **and** the window `Title` have changed to the other language without the
   dialog closing — that is `ApplyText()` running off `SettingsChanged`. Close and reopen; assert one
   handler, not two, by changing 语言 again and asserting the log shows one commit, not a doubled one.
6. `PrintWindow` (flag 2) the dialog. Its background is `SurfaceBaseBrush` = `AeroWindowBackground`
   (opaque), so it photographs correctly — unlike the launcher's caption band. Assert the tab strip's
   selected item paints `AeroTabControlBackground` (`#FFFFFFFF` Light).
7. Assert no `ScrollBar` element exists anywhere in the dialog's UIA subtree on any tab
   (*"Don't scroll property pages"*).
8. **Pin the size (§0 ruling 8).** For each of the six tabs, select it and read the `TabControl`'s
   content host's UIA `BoundingRectangle`, plus the desired size the tab reports with the window
   temporarily `SizeToContent="WidthAndHeight"`. Take the maximum over the six, add the `11` root
   margin on all sides and the commit row's `23 + 11`, and write that pair into `Width`/`Height` in
   place of the provisional `540 × 540`. If it does not fit inside `800 × 600`, **split the 文件夹 tab**
   — the ruling, not a judgement call at implementation time.

---

### T3 — The menu band replaces the pane, and every fit term moves

**The largest task, and it must be atomic**: the shell's chrome swaps from horizontal to vertical in one
step. Splitting it would ship a build whose fit arithmetic does not describe its own window.

#### Decision — the band's shape

The answer to "can a `TextBox` live in a `Menu`" is **settled up front and the answer is no, do not**.
`MenuBase.NeedsContainerOverride` accepts only `MenuItem` and `Separator` as their own containers;
anything else is wrapped in a generated `MenuItem` and becomes its `Header`, inheriting
`DefaultMenuInteractionHandler`'s key handling and click-closes-the-menu behaviour (decompiled Avalonia
12.1.0, r8-shell §6.3). So:

```
<Grid Grid.Row="1" x:Name="MenuBand" Height="25"
      x:DataType="vm:LauncherViewModel">        <!-- REQUIRED; see below -->
  <Menu x:Name="MainMenu">          <!-- fills the cell; paints the whole band -->
    <MenuItem x:Name="MenuOptions"  Click="OnOptionsClick"  />
    <MenuItem x:Name="MenuOpenRoot" Click="OnOpenRootClick" />
    <MenuItem x:Name="MenuRefresh"  Click="OnRefreshClick"  />
    <MenuItem x:Name="MenuExit"     Click="OnExitClick"     />
    <MenuItem x:Name="MenuAbout"    Click="OnAboutClick"    />
  </Menu>
  <!-- LEFT-aligned, offset to start exactly where 关于 ends: 放在"关于"后面 (§0 ruling 6). -->
  <StackPanel x:Name="SearchGroup" Orientation="Horizontal"
              HorizontalAlignment="Left" VerticalAlignment="Center"
              Margin="0,0,11,0">        <!-- reserves the trailing gutter; see §5's floor -->
    <Border x:Name="MenuRunSpacer" IsHitTestVisible="False"
            Width="{Binding #MenuAbout.Bounds.Right}" />
    <Border Classes="tm-bar-separator" />         <!-- 1 DIP wide, Margin="11,3" -->
    <Border Classes="tm-searchfield" x:Name="SearchField" Width="320"> … </Border>
  </StackPanel>
</Grid>
```

The `Menu` fills the cell so **Aero's own bar** — the gradient, the 7 DIP shine `Rectangle`, and the two
1 DIP bottom rules — runs the full window width (upstream `Menu.axaml`'s template, read this session).
The field and the separator are siblings *over* it, not children of it. `Menu` overrides
`KeyboardNavigation.TabNavigation` to `Once` (decompiled, r8-shell §6.3), so a field inside `Menu.Items`
might not be individually tab-reachable; a sibling is unaffected.

**Why a spacer rather than a column, and what is not yet verified about it.** Placing the group after
关于 and keeping Aero's bar full-width are in tension: a `Grid` with `ColumnDefinitions="Auto,Auto,*"`
and the `Menu` in column 0 would put the group in the right place but stop the bar painting past the
last item. So the `Menu` keeps the whole cell and the group is offset by the x of 关于's right edge,
taken from the one element that knows it — `MenuAbout` itself, which is in `MainWindow`'s name scope and
therefore bindable. `MenuItem.Bounds` is a `Rect` and `.Right` a `double`, so this is a plain
`Double`→`Double` element-name binding with no converter, and it re-evaluates on every language, font
and DPI change for free. There is no layout cycle: `MenuAbout`'s arrange depends on the `Menu`, which
fills the cell regardless of `SearchGroup`.
**COULD NOT VERIFY:** `MenuAbout.Bounds` is stated in its items panel's coordinate space, and the
vendored `Menu.axaml` is not in this tree yet, so its template's own left inset (the outer `Border`'s
`BorderThickness` + any `Padding`) is unknown. If it is non-zero the spacer is short by that amount.
T3's verification step 4 measures it, and the constant — read out of the vendored file once it lands —
is added to the spacer as an explicit `Margin` if it is not zero.

**Rejected: right-aligning the group** (`HorizontalAlignment="Right"`, `Margin="0,0,11,0"`), which is
Office XP/2003's *"Type a question for help"* geometry. It puts the separator a window-width away from
关于, which is not 放在"关于"后面 — a departure from the request, not a refinement of it.

This is **not** the thing Round 7 Task 1 removed. That was a control in the *window frame*, which
`win-window-frames` forbids verbatim (`MainWindow.axaml.cs:901-904`). The frame is the caption /
`WindowDrawnDecorations`; this band is inside the client area, below it.

#### Decision — the band's height: 25 DIP

Vendor `ControlStyles/Menu.axaml` from `a705b7c0` (129 lines, fetched and read in full this session)
with the standard header and **one** recorded change:

> `Changes from upstream:`
> 1. `MenuBarHeight` 20 → 25. Item 5 puts a search field in this bar and a one-line text box is
>    **23** relative pixels tall (PUBLISHED, `vis-layout`) — the same cell Round 7 Task 9 already cited
>    to set `Border.tm-searchfield`'s `MinHeight` (`Launcher.axaml:570-577`). The template's two 1 DIP
>    bottom-border `Rectangle`s live inside `MenuBarHeight`, so `23 + 2 = 25` leaves an item strip of
>    exactly 23 DIP.
> 2. `MenuFlyoutItemForeground` → `AeroForeground` on the top-level item's `Foreground` setter. The
>    upstream value is a FluentTheme key and this app has no Fluent ink anywhere else.

**25 departs from the MEASURED `SM_CYMENU` of 20 by 5 DIP, and that is item 5's cost, stated here rather
than discovered.** The rejected alternative was shrinking the field to 18 DIP inside a 20 DIP bar: 23 is
the one published height for a one-line text box, and the app already ships it. The third exit — putting
the row on a Windows 7-style toolbar instead (r8-win7 §2.2, which additionally right-aligns Options and
About) — is the period-correct container, has no published height at all, and would therefore dissolve
this arithmetic entirely. It is **rejected and recorded**, not open: §0 ruling 7 takes the user's word
"mainmenu" and books the three published sentences that favour a toolbar as the cost.

**Aero's top-level item never gets a pressed look for a direct command, and T3 must close that.**
Upstream binds pressed to `Selector="^[IsSubMenuOpen=True]"`, and Avalonia never sets `IsSubMenuOpen`
on a childless item — `MenuItem.HasSubMenu => !Classes.Contains(":empty")`, and `PointerPressed` returns
early when `!HasSubMenu` (decompiled, r8-win7 §1.6). `MBI_PUSHED` is a real published state
(`parts-and-states`, class `MENU`). Fix: **add `^:pressed` to the existing `^[IsSubMenuOpen=True]`
selector inside the vendored file**, as change 3 in its list — one selector, no new values.

#### Decision — the separator

`about-menus`: *"A separator cannot be used in a menu bar."* `Avalonia.Controls.Separator` is a bare
`TemplatedControl` whose look is 100 % theme-supplied and Fluent's is horizontal (r8-win7 §5.3). So the
divider is a 1 DIP `Border`, using the **toolbar** vocabulary Win32 does have — `TOOLBAR` /
`TP_SEPARATORVERT` (`parts-and-states`). New rule in `Launcher.axaml`:

```
<Style Selector="Border.tm-bar-separator">
  <Setter Property="Width" Value="1" />
  <Setter Property="Margin" Value="11,3" />
  <Setter Property="Background" Value="{DynamicResource AeroMenuBarBottomInnerBorderBrush}" />
</Style>
```

`11` = *"Between unrelated controls"*; `3` = *"Smallest space between a non-interactive control and any
other control"* (both PUBLISHED, `vis-layout`); `AeroMenuBarBottomInnerBorderBrush` is the darker of the
bar's own two bottom rules (`AVMAP/Brushes.axaml:333`) and needs no new key. **The 17 DIP resulting
height is AUTHORED — nothing published gives a vertical menu-bar separator a height.**

#### Decision — labels and access keys

`ApplyRailText` becomes `ApplyMenuText` — five `MenuItem.Header` writes plus five
`AutomationProperties.Name` writes, same count, same subscription (`MainWindow.axaml.cs:95-98`,
unsubscribed at `:443-444`). Headers carry the access-key marker `_`; the vendored template opts in via
`RecognizesAccessKey="True"` on `PART_HeaderPresenter` (verified in the fetched file), and
`Menu.OnAttachedToVisualTree` sets `AccessKeyHandler.MainMenu = this`, which is what makes bare **Alt**
activate the bar (decompiled, r8-shell §6.2).

| Item | Key today | English string | Access key | Basis |
|---|---|---|---|---|
| 设置 | **new** `MenuOptions` = ("设置", "Options") | Options | **O** | PUBLISHED, `cmd-menus` "Use Options instead"; `inter-keyboard` standard table gives O = Options |
| 主目录 | `MenuOpenRoot` | Main Folder | **M** | app-specific; PUBLISHED preference for wide letters "such as w, m" |
| 刷新 | `MenuRefresh` (`:220`) | Refresh | **R** | app-specific, no published assignment |
| 退出 | `MenuExit` (`:221`) | Exit | **x** | PUBLISHED, `inter-keyboard` standard table |
| 关于 | `About` (`:227`) | About | **A** | PUBLISHED, `inter-keyboard` standard table |

**The 主目录 row was shipped as 打开主目录 / "Open Main Folder" and the review round corrected it to the
label this table always named.** The verb-first string was minted for the Round 3 title strip's `⋯`
overflow menu and inherited unchanged through two moves; nothing but the string itself said "Open".
`AppLanguage`'s `MenuOpenRoot` and `MenuOpenRootWithAccessKey` both changed; the **key names did not**,
because the command still opens the root folder — only its label is a noun. The access key stays **M**
(it was the M of *Main Folder* before and still is).

No ellipses (PUBLISHED). **No icons** — `cmd-menus`: *"If you use icons, don't feel obligated to provide
them for all menu items."* That removes five of the app's `Segoe Fluent Icons` uses, shrinking the one
declared anachronism (`Tokens.axaml:436`, `Launcher.axaml:519`).

`AppLanguage.cs` gains `MenuOptions`; `NavApps` / `NavSettings` are **retired but kept** — `NavSettings`
still labels the tray's 设置 entry? No: the tray uses `TraySettings` (`TrayService.cs:103-113`,
`AppLanguage.cs:247-249`). `NavApps` and `NavSettings` lose their last readers and are **deleted**, with
`NavSettings`'s "设置" text re-minted as `MenuOptions` so the English half can differ.

#### Decision — the search field's move

`Border Classes="tm-header"` (`AppPage.axaml:68`) and its child `Border.tm-searchfield` subtree
(`:69-124`) move from `Views/AppPage.axaml` into `MainWindow.axaml`'s band; `tm-header` itself is
**deleted** (the band is the header now). Moving with it:

- `MainWindow` ctor: `MenuBand.DataContext = _appPage.ViewModel;`.
- **The bindings do NOT survive the move unchanged — `MainWindow.axaml` has to declare the type.**
  `AvaloniaUseCompiledBindingsByDefault` is `true` (`src/TanMenu.Wpf/TanMenu.Wpf.csproj:45`), and a
  compiled `{Binding}` resolves its path against the nearest ancestor's `x:DataType`. `AppPage.axaml`
  supplies one at `:6` (`x:DataType="vm:LauncherViewModel"`, with `xmlns:vm="using:TanMenu.Wpf.ViewModels"`
  at `:3`); `MainWindow.axaml`'s root (`:1-14`) declares **neither the namespace nor an `x:DataType`**.
  Moved as-is, `{Binding Filter}`, `{Binding SearchPlaceholderText}` and `{Binding ClearFilterText}`
  would be a **compile error**, not a silent no-op. So `MainWindow.axaml` gains
  `xmlns:vm="using:TanMenu.Wpf.ViewModels"` on the root and the band `Grid` gains
  `x:DataType="vm:LauncherViewModel"` (shown in §3.3's markup) — scoped to the band rather than put on
  the `Window`, because the `Window`'s own `DataContext` is not the view model and a root-level
  `x:DataType` would mis-type every other binding in the file.
  The fourth binding, `{Binding #SearchBox.Text, …}` on the two affordance `Border`s, and
  `{Binding #MenuAbout.Bounds.Right}` on the spacer, are **element-name** bindings: they resolve against
  the named element's own type and need no `x:DataType`. They do move unchanged.
- `AppPage.FocusSearchBox` (`:300`) → `MainWindow.FocusSearchBox`; `ConsumePendingFocus`
  (`MainWindow.axaml.cs:946-956`) calls it directly instead of forwarding.
- `OnSearchFocusChanged` (`:320-326`) including `SearchField.Classes.Set("focused", …)` (`:323`) and
  `SearchBox.SelectAll()`, `OnSearchClearPressed` (`:339`), `OnSearchKeyDown` (`:352`) → `MainWindow`.
  `OnSearchKeyDown`'s Down case calls `AppPage.FocusFirstTile` (`:423`), which becomes `internal`.
- `Ctrl+E` (`MainWindow.axaml.cs:497-505`) loses its page guard and its `PendingFocus` hop is kept —
  the request still goes through `PendingFocus` so `ConsumePendingFocus` keeps owning the timing.

#### Decision — the pane's deletion and the constants

Delete `MainWindow.axaml:395-720` (the pane's prose block through its closing `</Border>`; the
`NavRailPane` Border itself is `:502-720`). Delete the `Auto,*` column: the content `Grid` becomes
`RowDefinitions="Auto,*"` with the band at row 0 and `PagePane` at row 1, inside the existing
`<Grid RowDefinitions="30,*">`'s row 1.

`Border.tm-page-pane.app` / `:not(.app)` (`Launcher.axaml:1094-1104`) collapse to **one** rule:

```
<Style Selector="Border.tm-page-pane">
  <Setter Property="Background" Value="{DynamicResource SurfacePageBrush}" />
</Style>
```

The `Background` stays: `SurfacePageBrush` aliases `AeroListBoxBackgroundColor` and Windows 7 drew a
white item area inside an `#FCFCFC` window. **The `BorderThickness="1,1,0,0"` goes**: its left edge had
the nav pane to separate from and now has nothing, and its top edge is now redundant against the band's
own two bottom rules. `PagePane.Classes.Set("app", …)` (`MainWindow.axaml.cs:762`) is deleted with the
class flip; `Classes="tm-page-pane"` stays.

Constant changes — the full sweep is §5.

#### Blast radius

Files: `Themes/Aero/ControlStyles/Menu.axaml` (new, vendored + 3 changes), `AeroResources.axaml`,
`MainWindow.axaml`, `MainWindow.axaml.cs`, `Views/AppPage.axaml`, `Views/AppPage.axaml.cs`,
`Themes/Shell/Launcher.axaml`, `Services/WindowHost.cs`, `Core/Services/AppLanguage.cs`,
`tests/…/Themes/ShellConstantTests.cs`.

**One comment in `Launcher.axaml` becomes false in this task and is corrected in it.** The file's
focus-visual header (`:29-56`) says *"these five adorners are what replace it on everything this file
themes"* and *"Task 7's first cut wrote `Margin="-2"` on all five"*. T3 deletes `TmNavRowButton` and
`TmNavRowItem` and their `FocusAdorner` setters (`:388`, `:485`) with them, leaving **three** — at
`:149` (`TmTileButton`), `:215` (`TmIconButton`) and `:300` (`TmButton`). Both sentences read "three"
after this task. The measurement the header records was taken on a `TmNavRowButton` row, which no longer
exists; the record is kept and re-dated as historical rather than rewritten, because the finding it
records (a negative adorner margin renders nothing) is about `AdornerLayer`, not about that row.

> **SUPERSEDED BY THE REVIEW ROUND — the count is ONE, and not by another deletion.** Three setters on
> three `x:Key`'d `ControlThemes` reach only the buttons that name one of those themes, and the three
> dialogs this round built name none of them, so 确定/取消/应用, the About box's 确定 and every
> `MessageBoxWindow` button kept falling back to Fluent's two-ring adorner. The three were replaced by
> one bare `Style Selector="Button"` in the same file — the only rule in it that is not opt-in, which
> `App.axaml`'s layer list now says. §10.6 carries the A/B measurements.

Fit-arithmetic terms that move: `RailWidthDip` (deleted), `PaneBorderDip` (deleted),
`ChromeHeightDip` (31 → 55 via a new `MenuBandHeightDip = 25`), `MinWindowWidthDip` (872 → 711),
`MainWindow.axaml`'s `MinWidth` (872 → 711) and `Width` (1112 → 1000), `PlaceholderWidthDip`
(1112 → 1000). `TitleStripHeightDip` (30), `MinWindowHeightDip` (568), `PlaceholderHeightDip` (688) and
`BottomGapDip` (8) are **unchanged**.

Re-fit triggers: the `ShowPage` trigger is already gone (T2). Four remain — see §8.1.

Tests: `ShellConstantTests.RailWidth_MatchesTheRailPaneWidth` is **deleted** and replaced by
`MenuBandHeight_IsTheSameNumberInAllThreePlaces` — the band height lands in **three** unlinked places,
not two: the vendored `Menu.axaml`'s `<x:Double x:Key="MenuBarHeight">`, `WindowHost.MenuBandHeightDip`
(the fit's chrome term, §5), and `MainWindow.axaml`'s `MenuBand` `Height="25"` (§3.3's markup). The
existing `CaptionHeight_IsTheSameNumberInAllThreePlaces` (`ShellConstantTests.cs:59-80`) is the form to
copy exactly: three `Single()` reads, one `Assert.True(a == b && b == c)`. Two of the three would agree
while the third drifted, and the drift's only symptom is a band whose painted height and whose fit term
disagree — silently, which is what this file's guards exist for.
The old fact fails **on contact** otherwise: its `Single()` (`:242-249`) asserts exactly one match for
`x:Name="NavRailPane"` + `Width`, so deleting the pane fails it with "found 0".
`MinWindowWidth_IsTheSameNumberInBothPlaces` keeps passing with new numbers. New
`MenuBarTests`: five `MenuItem`s in the stated order, none with element children (so every one is a
command, not a submenu), none whose header string ends in `…`, five distinct access-key letters.

#### Verification

1. `dotnet test`.
2. Run the app. Read the newest `Window fit to content:` line from
   `src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Data\cache\logs\log-<date>.txt` and
   assert the chrome term reads exactly `chrome 0x55`. That term is the *only* observable of
   `ChromeHeightDip` (`WindowHost.cs:50-56`) and it is unambiguous.
3. Assert the same line's window width equals its page width (chrome 0) and its height is `568`.
4. UIA: enumerate `MainMenu`'s children — five, `ControlType.MenuItem`, `Name` equal to the
   language-appropriate string with the `_` stripped. Then assert the field sits **after 关于**, not at
   the window's right edge:
   - `SearchField.BoundingRectangle.Top` and `.Bottom` lie inside the band's own rect;
   - `SearchField.BoundingRectangle.Left − MenuAbout.BoundingRectangle.Right` equals the separator's
     whole footprint — `11 + 1 + 11 = 23` DIP = `34.5` physical px at scale 1.5 — **within ±2 physical
     px**, which is the bar. The tolerance is not slack: `Layoutable.UseLayoutRounding` defaults to
     `true`, so every DIP edge is snapped to a device pixel, and a 23 DIP gap at 1.5 cannot land on
     34.5 px. Asserting an exact fractional pixel is a bar a **correct** build fails. If the measured
     gap is off by a constant, that constant is the vendored `Menu` template's left inset flagged in
     §3.3 — record it, add it to `MenuRunSpacer`, and re-measure.
   - `SearchField.BoundingRectangle.Right` is at least 100 px **short of** the window's client right
     edge at the default width, which is what distinguishes "after 关于" from the rejected
     right-aligned variant.
5. UIA `InvokePattern.Invoke()` on `MenuRefresh`; assert a new `Menu rebuilt` / reload line appears in
   the log. Repeat for `MenuOpenRoot` (assert a `LaunchService` line) and `MenuOptions` (assert the
   dialog appears in the UIA tree). **Do not** invoke `MenuExit`.
6. `PrintWindow` (flag 2) the launcher. The band is below the caption, so it is over the opaque
   `SurfaceBaseBrush` client and photographs correctly. Sample a pixel 2 px below the band's top edge
   and assert it matches `AeroMenuBarShineColor0` composited over `AeroMenuBarBackgroundColor0`
   (`#FAFFFFFF` over `#FFD4DBED` in Light).
   **The two bottom rules are NOT asserted by exact colour equality.** They are 1 DIP each and the
   machine runs at 150 %, so each is 1.5 physical px: with layout rounding one row is the pure colour
   and the neighbouring row is a blend, and *which* row is which depends on where the band's bottom edge
   snapped. Exact equality on both is a bar a correct build fails. The bar instead is: scan the bottom
   **six** rows of the band's rect, and assert (a) the *nearest* colour to
   `AeroMenuBarBottomOuterBorderBrushColor` (`#FFF0F0F0` Light, `Variant/Light.axaml:273`) appears at a
   channel distance ≤ 8, (b) the nearest to `AeroMenuBarBottomInnerBorderBrushColor` (`#FFB6BCCC`,
   `:275`) likewise, and (c) the outer row is **above** the inner one. Ordering and identity are what
   the vendored template actually promises; a sub-pixel row's exact byte is not.
7. Repeat 6 with `general.appearance` = Dark: the shine sample becomes `#2FFFFFFF` over `#FF1A2133`
   (`Variant/Dark.axaml:264`, `:267`), and the two bottom rules become `#FF393939` / `#FF00000E`
   (`Dark.axaml:273`, `:275`) under the same ≤ 8 / ordering bar.

---

### T4 — 关于 becomes an About box

#### Decision

New `Dialogs/AboutWindow.axaml{,.cs}` — modal, `SizeToContent="WidthAndHeight"`, `CanResize="False"`,
`ShowInTaskbar="False"`, `CenterOwner`, `Background="{DynamicResource SurfaceBaseBrush}"`, system
caption. Content is the existing `Views/Settings/AboutCard` `UserControl`, unmoved on disk. `Margin="11"`
(PUBLISHED dialog margin) and a single right-aligned **确定** button, `75 × 23`.

`MenuAbout`'s `Click` opens it. The 关于 `TabItem` is removed from `OptionsWindow`, leaving **five** tabs.

#### Why

PUBLISHED, `win-property-win`: *"Is the content solely an application's attributes? If so, use an About
box."* `SettingsPage.axaml:86-87` already records that 关于 is *"the only group with no interactive
control"*. PUBLISHED, `cmd-menus`, standard menus: **Help → About `<program name>`**. And
`ctrl-group-boxes`: *"Is there more than one control in the group? If not, use a plain text label
instead"* — a one-control tab is the same case.

#### Blast radius

`Dialogs/AboutWindow.axaml{,.cs}` (new), `Dialogs/OptionsWindow.axaml` (one `TabItem` removed),
`MainWindow.axaml.cs` (`OnAboutClick`). `Views/Settings/AboutCard.axaml{,.cs}` unchanged. No constant,
no fit term, no test moves.

#### Verification

UIA `InvokePattern` on `MenuAbout`; assert a third window with `Name` = 关于/About appears, that its
subtree contains the version `TextBlock`'s `Name`, and that `OptionsWindow` now reports five `TabItem`s.

---

### T5 — Toggle switches become check boxes

#### Decision

Five `ToggleSwitch` → `CheckBox`: `BehaviorCard.axaml`'s `AutoCloseToggle`, `TopMostToggle`,
`AutoStartToggle`, `HotkeyEnabledToggle`, and `ToolsCard.axaml`'s `ShowToolsToggle`. Handlers move from
`IsCheckedChanged` to `IsCheckedChanged` on `ToggleButton` (same event name, same `IsChecked` reads —
`ToolsCard.axaml.cs:104` already constructs `CheckBox`es in code, so the pattern is in-tree).

**T5 swaps the control and nothing else. The label stays where it is.** The published end state is a
label **on** the check box (`ctrl-check-boxes`: the label describes the selected state, sentence case,
no ending punctuation, a unique access key each), and that is **T6's** move, not this task's. T5 leaves
each row's title `TextBlock` in place, with its `x:Name` and its `Classes="tm-card-title"`, and swaps
only the trailing control.

Two things force that split and both are checkable in the tree:

- Those titles are **named elements written from code-behind** — `AutoCloseTitle`, `TopMostTitle`,
  `AutoStartTitle`, `HotkeyEnabledTitle`, `HotkeyTitle` (`BehaviorCard.axaml:38`, `:50`, `:62`, `:116`,
  `:128`), assigned at `BehaviorCard.axaml.cs:129`, `:133`, `:137`, `:146`, `:150`. Deleting the
  `TextBlock`s without rewriting `ReloadFromConfig` in the same commit is a **compile break**, not a
  layout regression. T6 rewrites both sides together.
- `SettingsRowTests.EveryCardHeader_IsOneTitleLineAndOneSubtitleLine` (`SettingsRowTests.cs:182`) still
  exists at T5 and would fail on a removed title line. T6 is where it is deleted.

`ToggleSwitch.tm-card-toggle` (`Launcher.axaml:1472-1477`) is deleted here, with the switches,
taking its `Margin="0,0,-12,0"` — a compensation for the vendored `ToggleSwitch.axaml:58`'s hard
`<ColumnDefinition Width="12" MaxWidth="12"/>` that no `/template/` selector can reach. **That is the
one irreplaceable rule §4 has to lose, and it loses it because its subject is gone.**

The vendored `ControlStyles/ToggleSwitch.axaml` (203 lines) **stays on disk and stays merged**: it is a
vendored theme file, not a custom style, and item 1 does not touch `Themes/Aero/`. It simply has no
consumer. Recorded, not deleted.

#### Why

Windows 7 had **no toggle switch control at all** — it is in none of the UX Guide's 73 articles and not
in `parts-and-states`' class list (r8-win7 §4 row 1). Round 7's own review flagged it as an undeclared
anachronism. PUBLISHED, `ctrl-check-boxes`: *"check boxes should be used only to toggle an option on or
off"*, sized **17** relative pixels (`vis-layout`); the vendored Aero `CheckBox` draws a 13 DIP box
(`CheckBox.axaml`'s `Border#Outer` `Width="13" Height="13"`) plus `Padding="4,0,0,0"` — VENDORED, and
the 17 is the *row* height the guide gives, not the glyph.

#### Blast radius

`Views/Settings/BehaviorCard.axaml{,.cs}`, `ToolsCard.axaml{,.cs}`, `Themes/Shell/Launcher.axaml`
(one rule deleted, 6 markup lines / 44 with prose). **No card's `ReloadFromConfig` changes in T5** — the
five `…Title.Text` and five `…Sub.Text` assignments are untouched, and the five `IsChecked` assignments
read the same property on a `CheckBox` that they read on a `ToggleSwitch`. `SettingsRowTests`' four
facts all keep passing here; T6 is where two of them are deleted.

#### Verification

UIA on the 行为 tab: four elements of `ControlType.CheckBox`, zero of Avalonia's `ToggleSwitch` peer,
and the five title `TextBlock`s still present with their text (the split above, asserted rather than
assumed). Drive `AutoCloseToggle` with `TogglePattern.Toggle()` and assert 应用 becomes enabled
(T1's `IsDirty`) and that no save appears in the log until 应用 is invoked.

---

### T6 — The settings row grammar becomes the DLU grid

#### Decision

Delete twelve rules from `Launcher.axaml` — `Border.tm-card`, `TextBlock.tm-card-title`,
`.tm-card-sub`, `:is(Control).tm-card-icon`, `StackPanel.tm-card-text`, `TextBlock.tm-card-value`,
`:is(Control).tm-card-expander`, `StackPanel.tm-card-expander`, `.tm-card-values`, `.tm-card-actions`,
`TextBlock.tm-page-title`, `HeaderedContentControl.tm-group-box` — and rewrite all six card files onto
`vis-layout`'s ladder:

- A row is a label and a control, not a container. Two-column `Grid` (`Auto,*`), label left, control
  right; `5` between a label and its control; `7` between related rows; `11` between unrelated blocks.
- A tab whose controls are one homogeneous set uses **no group box** — PUBLISHED, `ctrl-group-boxes`:
  *"Are all the controls inside the group? If so, indicate the relationship on the larger surface, such
  as the parent dialog box or page."* 文件夹 keeps group boxes (root folder / data folder / cache /
  backup, four distinct sets), which is the *"Is there more than one control in the group?"* case
  passing.
- Where a group box survives it takes the vendored `HeaderedContentControl` theme's own numbers —
  `tm-group-box`'s three overrides (`CornerRadius 5→3`, `Padding 10→11`, `Margin→0,0,0,11`) are the
  cleanest cosmetic item in the file and go.
- `TextBlock.tm-group-head` **stays**, with `FontWeight` SemiBold → Normal (§T8).
- `tm-keycap`'s `MinHeight` 32 → 23 (see T8).
- **The six `GroupHead` `TextBlock`s and their six code-behind writes go with the group boxes.**
  Deleting five of the six `HeaderedContentControl Classes="tm-group-box"` wrappers deletes their
  `x:Name="GroupHead"` header `TextBlock`s (`AppearanceCard.axaml:30`, `BehaviorCard.axaml:30`,
  `ToolsCard.axaml:46`, `UpdatesCard.axaml:43`, `AboutCard.axaml:51`), so the five matching
  `GroupHead.Text = AppLanguage.Text(…)` lines (`AppearanceCard.axaml.cs:107`,
  `BehaviorCard.axaml.cs:127`, `ToolsCard.axaml.cs:61`, `UpdatesCard.axaml.cs:88`,
  `AboutCard.axaml.cs:46`) are **deleted in the same commit or the build does not compile**. The group's
  name now lives on its `TabItem`, written by `OptionsWindow.ApplyText()` from the same `AppLanguage`
  key — the path T2 established. 文件夹 keeps group boxes but not one outer `GroupHead`: its four inner
  boxes get their own heads, so `FoldersCard.axaml.cs:95` is **rewritten**, not deleted.

#### Why

Item 4. `Border.tm-card` + its nine dependants are Windows 11 Settings' row grammar; a Windows 7 row is
a label and a control on a 7-DLU grid and there is no such thing as a settings "row card" (r8-win7 §4
row 5). The page title goes because the dialog has a title bar — PUBLISHED, `win-dialog-box`: *"If the
title bar caption and icon are already displayed in a prominent way near the top of the window, you can
hide the title bar caption and icon to avoid redundancy"* — and, more simply, because `tm-page-title`'s
only consumer was `SettingsPage`, which T2 deleted.

#### Blast radius

All six card files, `Themes/Shell/Launcher.axaml` (12 rules, 54 markup / 276 lines with prose).

**Tests: `SettingsRowTests` 4 facts → 2.**
- `EveryCardBorder_WrapsTheStandardColumnGrid` — **deleted**. It matches `Border` with
  `Classes="tm-card"` exactly (`SettingsRowTests.cs:303-307`); there is nothing left for it to guard.
- `EveryCardHeader_IsOneTitleLineAndOneSubtitleLine` — **deleted**, same reason.
- `NoCardFile_RestatesAConstantTheThemeOwns` — **rewritten** as
  `NoSettingsFile_RestatesAConstantTheThemeOwns`: same ratchet, re-aimed from cards to every
  `Margin`/`Padding`/`Spacing` in `Views/Settings/*.axaml` and `Dialogs/OptionsWindow.axaml`, with
  `AeroLadder` (`:120`) extended from `{0,5,7,11,22}` to `{0,3,5,7,9,11,16,22}` — every added rung is a
  cell of the PUBLISHED `vis-layout` spacing table (§2). `AeroLadder` is a **hard-coded `string[]`
  declared in the test file** (`SettingsRowTests.cs:120`) that has never read `Tokens.axaml`; its
  doc comment (`:104-119`) carries the verbatim published sentences and the URL, and gains the three
  new rungs' citations. T7 deletes `Space1`/`Space2`/`Space3`, and the comment's *"22 is the `2 × Space3`
  the theme states"* (`:112`) is rewritten to cite `vis-layout` directly, because after T7 there is no
  `Space3` for it to point at.
- `NoHeaderSubtitleString_ContainsAHardLineBreak` — **rewritten** as
  `NoRowLabelString_ContainsAHardLineBreak`, re-aimed from `tm-card-sub` to the row label `TextBlock`s.
- `CardFileNames()` (`:292-298`) excludes `AboutCard.axaml`, which T4 made an About box's content
  rather than a settings row.

#### Verification

`dotnet test`. Then `PrintWindow` each tab of the dialog and assert, per tab, that every row label's
left edge shares one x (UIA `BoundingRectangle.Left` equal within 1 px across the tab's labels) and that
every control's left edge shares another — the two-column invariant, measurable with no input.

---

### T7 — The strip proper

#### Decision

Delete the two rules §4 classifies as cosmetic-or-inert and fix the one stale comment:

- `StackPanel.tm-empty Button` (`:846-848`) — restates `HorizontalContentAlignment=Center`, which both
  `TmButton` (`:297`) and the vendored `{x:Type Button}` already set. Its own comment describes a build
  where those buttons were unthemed.
- `TextBox.tm-search:focus /template/ Border#BgElement` (`:759-761`) — measured inert by the file's own
  Task 9 A/B (`:740-754`): with `Border.tm-searchfield.focused` carrying both the fill and the border,
  the field reads (93,93,93) Dark / (255,255,255) Light at both sample points with and without it. Its
  `:pointerover` twin (`:756-758`) is **retained** — the same measurement does not cover it.
- `Tokens.axaml:382-389`'s `RadiusControlInner` comment: both of its factual claims are false against
  this tree (it says the key has no `{DynamicResource}` consumer — `Launcher.axaml:650` is one — and it
  cites `tm-searchfield-focusbar`, which Task 9 deleted). Rewritten, not deleted; the comment-accuracy
  rule makes a false comment a defect. Its closing clause, *"for the same reason `Space1..Space3` above
  are kept"* (`:389`), also goes — see the next item, which deletes them.
- **`Space1` / `Space2` / `Space3` (`Tokens.axaml:413-415`) are DELETED.** They were previously written
  up as retained; that retention does not survive its own test. Grepped this session: **zero**
  `{DynamicResource}` consumers anywhere under `src/` or `tests/` — every hit is prose. The file itself
  says they can never gain one (`:405-407`: XAML cannot compose a `Thickness` out of a `Double`
  resource), which makes them documentation living in a resource dictionary, not style. And the reason
  offered for keeping them — *"T6's rewritten ladder test cites them"* — is **false against the tree**:
  `SettingsRowTests.AeroLadder` (`SettingsRowTests.cs:120`) is a hard-coded `string[]` that never reads
  `Tokens.axaml`, and the published ladder is stated verbatim, with its URL, in that test's own doc
  comment (`:104-119`) and again in §2 of this document. Deleting three unread keys loses nothing that
  is not stated in two other places. `SurfacePadding` (`:417`) **stays** — it is a `Thickness` and it
  has a consumer.
- **`FontSizeBody` and `FontSizeSubtitle` (`Tokens.axaml:434`, `:436`) stop restating vendored numbers
  and become aliases.** Today they are literals — `<x:Double>12</x:Double>` and `<x:Double>16</x:Double>`
  — and the file's own comment (`:427-428`) admits each equals a vendored key exactly:
  `AeroFontSizeNormal` 12 and `AeroFontSizeLarge` 16 (`Themes/Aero/AVMAP/Metrics.axaml:33-34`). A literal
  that restates a vendored value is the thing §1's test rejects. They are re-declared in the alias form
  this file already uses for its two variant-independent aliases:

  ```
  <StaticResource x:Key="FontSizeBody"     ResourceKey="AeroFontSizeNormal" />
  <StaticResource x:Key="FontSizeSubtitle" ResourceKey="AeroFontSizeLarge" />
  ```

  The form is **safe for exactly these two**, on the argument the file already makes at `:28-35` and
  `:360-363`: `<StaticResource>` resolves once at parse time and would freeze a variant, so it is
  forbidden for the seven themed keys — but `Metrics.axaml` is merged **outside** `AeroResources.axaml`'s
  `ThemeDictionaries`, there is no variant to get wrong, and `RadiusControl` (`:372`) and
  `ControlCornerRadius` (`:467`) are already written this way. `Tokens.axaml:31`'s *"used below for the
  two VARIANT-INDEPENDENT aliases"* becomes **four**.
  That is what earns the two keys their retention: the *name* is read from eleven `{DynamicResource}`
  sites in `Launcher.axaml` (`:137`, `:294`, `:662`, `:840`, `:855`, `:875`, `:919`, `:1252`, `:1255`,
  `:1527`, `:1598`), all on `TextBlock`s — and **no vendored file themes a `TextBlock`** (§1), so the
  setters have no theme value under them and cannot go. After this change the *value* has exactly one
  home, the vendored one. `FontSizeGroupHead` (15) stays a literal: it is PUBLISHED (11 pt = 14.667,
  rounded up) and has no vendored twin.
- `TmButton`'s `MinHeight` 32 → **23** and a new `MinWidth` **75** (PUBLISHED, `vis-layout`: *"Command
  buttons — 50 × 14 DLU — 75 × 23 relative pixels"*). `TextBox.tm-keycap`'s `MinHeight` 32 → 23 moves
  with it, because its own comment (`Launcher.axaml:1513-1525`) records that it is sized *"explicitly to
  match this button"* against a MEASURED 49/48/48/47/48/48 physical px across eight scroll stops. **This
  is a Windows 11 leftover Round 7 Task 9 caught in the search field and missed here.**

Everything else in `Themes/Shell/` is retained with a named reason. §4 is the list.

#### Blast radius

`Themes/Shell/Launcher.axaml`, `Themes/Shell/Tokens.axaml` (**three keys deleted, two re-declared as
aliases**, plus the comment fixes above), `tests/…/Themes/SettingsRowTests.cs` (the `AeroLadder` doc
comment's `Space3` citation). Every `TmButton` consumer
(AppPage ×5, FoldersCard ×8, BehaviorCard ×2, UpdatesCard ×2, plus the dialog's commit row) gets 9 DIP
shorter. **`ShellTokenTests` ×4 unaffected, checked rather than assumed**: `DefinedKeys`
(`ShellTokenTests.cs:144-147`) matches any `x:Key="…"` regardless of the element carrying it, so the two
re-declared keys stay in the defined set; `ReferencedKeys` (`:149-152`) only collects
`{Dynamic|StaticResource …}` markup extensions, and `Space1`/`Space2`/`Space3` appear in none, so
deleting them cannot leave a dangling reference. The dialog's `75 × 23` commit buttons and `TmButton` now agree
by construction rather than by coincidence, which is worth a `ShellConstantTests` fact:
`CommandButtonHeight_IsTheSameNumberInBothPlaces`.

#### Verification

`dotnet test`. `PrintWindow` the 文件夹 tab and assert a `TmButton`'s UIA `BoundingRectangle.Height` is
`23 × 1.5 = 34.5` → 34 or 35 physical px (it is a `MinHeight`, so assert `≥ 34` and `≤ 36`).

---

### T8 — The rest of item 4's sweep

#### Decision

| # | Change | Basis |
|---|---|---|
| 1 | `SplashPanel.axaml:92` `CornerRadius="12"` → `{DynamicResource RadiusControl}` (= `AeroControlCornerRadius`, VENDORED 3) | r8-win7 §4 row 2; `Tokens.axaml:372` already aliases the key |
| 2 | `MessageBoxWindow.axaml:51` `CornerRadius="15"` → `0` (the circular icon badge is a WinUI form) | r8-win7 §4 row 2; `Tokens.axaml`'s `OverlayCornerRadius = 0` and its "Windows 7's popups and flyouts were square" note |
| 3 | `TextBlock.tm-group-head` `FontWeight` SemiBold → Normal | PUBLISHED, `vis-fonts`: the group-header row gives size (11 pt) and colour (`#003399`) and no weight; *"Most UI text should be plain without any attributes"* |
| 4 | Nothing else | see below |

**Explicitly NOT changed, each with its reason:**

- **Dark mode stays.** Windows 7 had no dark variant and `vis-fonts` prescribes literal `#000000` on
  light; the dark values in `Themes/Aero/Variant/Dark.axaml` are the third-party reproduction's
  invention, not Aero's. It is a **user-facing feature** (`general.appearance`), not a style, and
  removing it is a feature deletion nobody asked for. **DECLARED ANACHRONISM.**
- **`IconFontFamily` stays.** Windows 7 had no icon font — its shell glyphs were bitmaps — so there is
  nothing to fall back to; deleting it renders every glyph as a private-use code point in the body font.
  Already declared at `Tokens.axaml:436` and `Launcher.axaml:519`. T3 removed five of its uses.
  **DECLARED ANACHRONISM, reduced.**
- **Acrylic stays.** It reaches exactly one 30 DIP band — the caption — because Round 7 Task 7 made the
  whole client area opaque (`MainWindow.axaml:392-393`). Aero's glass was a live blur of what is behind
  the window and Mica never blurs; acrylic is the closest platform primitive. **DECLARED ANACHRONISM,
  bounded to one band.**
- **InfoBar and Toast stay.** Their painted form is *already* Aero: `Border.tm-infobar`
  (`Launcher.axaml:889-895`) and `Border.tm-toast` (`:907-916`) are a transparent-or-window-surface fill
  inside the group-box hairline at Aero's own 3 DIP radius, converted in Round 7 Task 7. What has no
  Windows 7 counterpart is the *concept*; the published replacements are a balloon anchored to a
  notification-area icon and a modal message box, and both are worse for these two specific messages —
  a docked, dismissible pin prompt and a transient in-window error. **NAMED DEVIATION, argued.**
- **The hand-drawn caption stays** (§4).

#### Verification

`PrintWindow` the splash (visible on cold start before the build lands) and the 恢复 confirm box driven
via UIA; assert the corner pixels. `dotnet test` for the weight change is not possible — verify by
`PrintWindow` stroke-width comparison on a group-box header, or accept it as a source-diff-only change.

---

### T9 — Records

Rewrite `docs/superpowers/specs/2026-08-04-aero-conversion-design.md`'s navigation-pane section as
superseded, add the Round 8 record, update `MainWindow.axaml.cs`'s class summary (it still describes the
pane and the two pages), update `GeneralConfig.NavRailExpanded`'s remark (`AppConfig.cs`) — it currently
says *"the rail is permanently 48 px icon-only"*, which was already false after Round 7 Task 8 moved it
to 160 and is now false twice over. Bump to **0.14.0**.

---

## 4. The custom-style strip list

`Themes/Shell/Launcher.axaml` is 1,603 lines / **446 markup lines** (comment and blank lines stripped),
in **57 top-level blocks** — 5 `ControlTheme`s and 52 `Style`s, totalling **441 markup lines**; the other
5 are the `<Styles>` / `<Styles.Resources>` scaffolding. Counts below are from a parser run over the
file this session, not estimated.

### 4.1 Deleted — 23 blocks, 157 markup lines, 685 lines with their prose

| Block | Markup | With prose | Task | Why it is cosmetic or dead |
|---|---|---|---|---|
| `ControlTheme TmNavRowButton` | 49 | 89 (`:342-430`) | T3 | its three consumers are the pane's command rows |
| `ControlTheme TmNavRowItem` | 12 | 61 (`:431-491`) | T3 | its two consumers are the pane's page rows |
| `Grid.tm-nav-rail TextBlock.tm-icon` | 3 | 23 (`:921-943`) | T3 | the only reader of `Classes="tm-nav-rail"` |
| `TextBlock.tm-nav-label` | 6 | 27 (`:944-970`) | T3 | five consumers, all pane labels |
| `Border.tm-nav-separator` | 5 | 40 (`:971-1010`) | T3 | one consumer, `MainWindow.axaml:707` |
| `Border.tm-header` | 5 | 23 (`:494-516`) | T3 | the band is the header now |
| `Border.tm-page-pane.app` | 6 | 89 (`:1011-1099`) | T3 | replaced by one 3-line rule; the class flip has one page |
| `Border.tm-page-pane:not(.app)` | 5 | 5 | T3 | 设置 was its only subject |
| `ToggleSwitch.tm-card-toggle` | 6 | 44 (`:1434-1477`) | T5 | its five consumers become check boxes |
| `Border.tm-card` | 6 | 32 | T6 | Windows 11 row grammar |
| `TextBlock.tm-card-title` | 3 | 27 | T6 | ″ |
| `TextBlock.tm-card-sub` | 6 | 8 | T6 | ″ |
| `:is(Control).tm-card-icon` | 5 | 39 | T6 | ″ |
| `StackPanel.tm-card-text` | 4 | 15 | T6 | ″ |
| `TextBlock.tm-card-value` | 5 | 79 | T6 | ″ |
| `:is(Control).tm-card-expander` | 3 | 10 | T6 | ″ |
| `StackPanel.tm-card-expander` | 3 | 3 | T6 | ″ |
| `StackPanel.tm-card-values` | 3 | 9 | T6 | ″ |
| `StackPanel.tm-card-actions` | 6 | 17 | T6 | ″ |
| `TextBlock.tm-page-title` | 5 | 6 | T6 | one consumer, `SettingsPage`, deleted in T2 |
| `HeaderedContentControl.tm-group-box` | 5 | 31 | T6 | three setters overriding the vendored theme's own |
| `StackPanel.tm-empty Button` | 3 | 5 | T7 | restates a value `TmButton` **and** the vendored Button both set |
| `TextBox.tm-search:focus /template/ Border#BgElement` | 3 | 3 | T7 | measured inert (`Launcher.axaml:740-754`) |
| **Total** | **157** | **685** | | |

Outside `Themes/Shell/`, T3 deletes **≈326 lines** of `MainWindow.axaml` (`:395-720`, the pane's prose
block through its closing `</Border>`; the `NavRailPane` `Border` itself is `:502-720`), replaced by
**≈40 lines** of menu band.

### 4.2 Retained — 34 existing blocks + 2 new, 284 markup lines. Every one with its reason.

The 284 is `441 − 157` (§4 and §4.1) **and** the sum of the Markup column below; the two agree. They did
not on first writing — the `tm-section-head` row said "×3 / 20" against four blocks totalling 23 — which
is why every row now carries its line ranges.

| Block | Markup | Reason it is not cosmetic |
|---|---|---|
| `ControlTheme TmTileButton` | 49 | **Dimensional and alignment-bearing.** Its template insets content inside the border; the vendored Button's presenter is a *sibling* overlaying the borders. Chrome goes 43 → 33 DIP and every tile narrows ~10 DIP, with the label sitting **on** the 2 DIP double border. And `HorizontalContentAlignment` `Stretch` → `Center` gives every vertical column a ragged left edge, because the tile has no `Width` (Round 4 item 3 deleted it). |
| `ControlTheme TmIconButton` | 32 | **There is no theme value under it.** The Aero layer has exactly one Button theme and it is a *raised command button*. The InfoBar's dismiss ✕ (`Controls/InfoBar.axaml:31`) is a flat 32×32 affordance; without this it becomes a raised Aero button inside a 41-line notice bar. Its hover/pressed fills are unverifiable on this machine (this machine's TanMenu **is** pinned, so `AppPage`'s InfoBar never opens — stated at `Launcher.axaml:252-255`). |
| `ControlTheme TmButton` | 50 | **Three setters are genuinely ours** — `Padding="11,0"`, `Cursor="Hand"`, `FocusAdorner` — plus `MinHeight`, which T7 corrects 32 → 23 and pairs with `MinWidth="75"`. `TextBox.tm-keycap` is measured against it (`:1513-1525`). Dropping it gives Aero's `Padding="3"` and no height floor, and the hotkey box stops matching the 清除 button beside it. |
| **3** × `FocusAdorner` setters — `TmTileButton` (`:149`), `TmIconButton` (`:215`), `TmButton` (`:300`). The file has **five** today; T3 deletes the other two with `TmNavRowButton` (`:388`) and `TmNavRowItem` (`:485`), so "5 retained" would be counting two blocks §4.1 deletes. Their markup is already inside the three themes' line counts above, hence "—". A fourth and fifth may be *added* by T3's menu item and T2's tab item if the vendored themes declare none — that is a new rule, not a retention, and it is decided when those files land. | — | **Accessibility affordance with a published form.** Neither the vendored Aero layer nor Avalonia's own `Button`/`ListBoxItem` themes declare a `FocusAdorner`, so without them a Tab draws **Fluent's two-ring Windows 11 rectangle** over an Aero control. PUBLISHED, `inter-keyboard`: *"Input focus is indicated with a dotted rectangle called the focus rectangle."* **The margin must stay positive** — Task 7's first cut wrote `-2` and rendered nothing, because `AdornerLayer.ArrangeOverride` arranges every adorner into `new Rect(adorned.Bounds.Size)` and pushes a second clip (`Launcher.axaml:38-56`). |
| `Border.tm-searchfield` + `:pointerover` + `.focused` | 15 | **Geometry and the whole focus signal.** The inner `TextBox` carries local `Background="Transparent"` and `BorderThickness="0"` (`AppPage.axaml:112-116`), so nothing else paints the field at all. The `MinHeight="23"` is the PUBLISHED text-box height and it is the number T3's band height is derived from. |
| `TextBox.tm-search:pointerover /template/ Border#BgElement` | 3 | **Live suppression.** Without it Aero paints an opaque block over the TextBox's rect but not the field's 7 DIP left padding or the glyph — a lighter rectangle inset inside the field. Never exercised on this machine (no mouse), which the file states rather than glosses. |
| `TextBox.tm-search /template/ TextBlock#PART_Watermark` | 3 | **PUBLISHED requirement the theme does not meet.** `ctrl-text-boxes`: *"Draw the prompt text in italic gray."* The grey half is the vendored template's `Opacity="0.5"`; the face is this. |
| `TextBox.tm-search` | 7 | Zeroes the vendored `Padding` so the field's own 7,0,0,0 is the only left inset, and sets the caret brush. |
| `Border.tm-search-affordance` ×3 | 12 | **The magnifier↔× swap is PUBLISHED** (`ctrl-search-boxes`). Three `Border`s with no theme under them. |
| `TextBlock.tm-icon` + `.subtle` + `.small` | 15 | The icon-font family, size and ink. No theme targets `TextBlock`. |
| `Image.tm-tile-icon` + `Button.tm-tile:disabled Image.tm-tile-icon` | 7 | **20 DIP is one of the tile's two width terms** (`Launcher.axaml:86-87`). No theme targets `Image`. |
| `TextBlock.tm-section` (`:800-805`, 6), `Border.tm-section-head` **×4** (`:811-815` 5, `:816-818` 3, `:819-821` 3, `:822-824` 3), `TextBlock.tm-folder` (`:825-827`, 3) — **six** blocks | 23 | **Load-bearing interaction.** The heading is clickable when the category is backed by a real folder; the fade-in folder glyph (`Opacity` 0 → 1 on hover) is its *only* affordance. |
| `StackPanel.tm-group` | 3 | **`VerticalAlignment=Top` paired with the *absence* of one on the panel Border.** In a horizontal `StackPanel` a child at default `Stretch` arranges to the row height, so without it every panel comes out as tall as the tallest category. |
| `Border.tm-group-panel` | 7 | **`Padding="11,11,6,6"` is one half of a pair** — the tile's `Margin="0,0,5,5"` is the other, and `6 + 5 = 11` is what makes the inset uniform. Guarded by `AppPageGroupTests`. |
| `StackPanel.tm-empty` + `> TextBlock` + `> TextBlock.title` | 18 | Empty-state typography and centring. No theme value. |
| `Button.tm-update` | 7 | **The model item 1 wants**: no `Theme=`, rides the vendored Button, sets five things and deliberately omits `Background`/`BorderBrush`/`BorderThickness`/`CornerRadius`. |
| `Border.tm-infobar` | 7 | Nothing paints a bare `Border`; deleting it yields an invisible notice. Already Aero's own outline (T8). |
| `Border.tm-toast` + `> TextBlock` | 14 | Same; deleting it yields an invisible toast. |
| `TextBox.tm-keycap` | 7 | Sized against `TmButton`, measured (`:1513-1525`). |
| `TextBlock.tm-group-head` | 5 | **Aero has no heading ink.** PUBLISHED, `vis-fonts`: group headers 11 pt `#003399`. The vendored `HeaderedContentControl` theme sets no `Foreground` or `FontSize` on its Header. T8 drops the `SemiBold`. |
| **NEW** `Border.tm-page-pane` | 3 | Replaces the two-look pair; keeps the white item area Windows 7 drew inside an `#FCFCFC` window. |
| **NEW** `Border.tm-bar-separator` | 4 | Item 5's divider; Win32 has no menu-bar separator and Avalonia's `Separator` is horizontal under every theme in the tree. |

### 4.3 Retained outside `Launcher.axaml`

- **The Aero caption** — `MainWindow.axaml:103-143` (`TmCaptionButton`) + `:206-347`
  (`Window.WindowDecorationsTheme`), 268 lines, plus the 11 `Caption*` tokens only it reads.
  **Reason: no vendored Aero file themes a `Window` or `WindowDrawnDecorations`** — verified by grep
  over all 24 files under `Themes/Aero/` (the whole directory, i.e. a superset of the 21 the attribution
  test calls vendored), and by reading `AeroResources.axaml:199-213`'s merge list. Deleting it does not
  fall back to the theme; it falls back to **FluentTheme's Windows 11 title bar**, which is the exact
  thing Round 7 Task 1 removed and which item 4 forbids. Deleting only `TmCaptionButton` would leave
  three Aero *command* buttons floating in a glass band. This is a third category — chrome no theme
  supplies.
- **`Tokens.axaml`: 32 of its 35 keys retained, 3 deleted.** The aliases onto Aero are cosmetically
  inert but are *names*, read from ~44 `{DynamicResource}` sites across four files and two test files;
  `MessageBoxWindow.axaml` reads `SurfaceBaseBrush` even though `Launcher.axaml` cannot reach it.
  `HeadingBrush` and `IconFontFamily` have no Aero twin. `OverlayCornerRadius` and
  `ContentControlThemeFontFamily` reach **Avalonia's own** templates, and the second is half of the CJK
  guard that `AeroAttributionTests.NoVendoredFile_ReferencesTheInterFontFamily` enforces. There are
  **9** aliases today — 7 themed (written as a `SolidColorBrush` over a `{DynamicResource}` colour,
  because `<StaticResource>` would freeze a variant, `Tokens.axaml:28-42`) plus 2 variant-independent
  `<StaticResource>` ones (`RadiusControl` `:372`, `ControlCornerRadius` `:467`); **T7 makes it 11** by
  re-declaring `FontSizeBody` and `FontSizeSubtitle` in the same form instead of restating
  `AeroFontSizeNormal`/`AeroFontSizeLarge`'s numbers.
  **`Space1`/`Space2`/`Space3` are DELETED (T7).** The retention that stood here does not survive its
  own test: they have zero `{DynamicResource}` consumers, can never gain one (`Tokens.axaml:405-412` —
  XAML cannot compose a `Thickness` from a `Double` resource), and the reason offered for keeping them
  — that T6's ladder test cites them — is false against the tree: `SettingsRowTests.AeroLadder`
  (`SettingsRowTests.cs:120`) is a hard-coded array that never reads `Tokens.axaml`. Three keys nothing
  reads are documentation in a resource dictionary, which is precisely the "cosmetic" this section is
  supposed to catch. The published ladder keeps two homes — that test's doc comment with its verbatim
  quotes and URL (`:104-119`), and §2 of this document.
- **`SplashPanel.axaml:19-73`**, 55 lines of local `Style.Animations` (`tm-spinner`, `tm-dot`).
  Nothing in any theme animates a splash.

**Net: 157 markup lines deleted, 284 retained (+7 new). 685 lines deleted from `Launcher.axaml` with
prose, ≈326 more from `MainWindow.axaml`.**

---

## 5. The dead-constant sweep

| Constant | Today | After | Basis |
|---|---|---|---|
| `MainWindow.RailWidthDip` (`MainWindow.axaml.cs:390`) | 160 | **deleted** | its subject is deleted |
| `MainWindow.PaneBorderDip` (`:407`) | 1 | **deleted** | `Border.tm-page-pane`'s `BorderThickness` goes with the pane it separated from |
| the composed `chromeWidth` (`:360`) | `RailWidthDip + PaneBorderDip` = **161** | **0** | the menu band is above the page, not beside it |
| **NEW** `WindowHost.MenuBandHeightDip` | — | **25** | vendored `Menu.axaml`'s `MenuBarHeight`, raised 20 → 25 for the 23 DIP field + the template's two 1 DIP bottom rules. **It lands in three unlinked places** — that key, this constant, and `MainWindow.axaml`'s `MenuBand` `Height="25"` — which is what `MenuBandHeight_IsTheSameNumberInAllThreePlaces` guards |
| `WindowHost.ChromeHeightDip` (`WindowHost.cs:71`) | `TitleStripHeightDip + 1` = **31** | `TitleStripHeightDip + MenuBandHeightDip` = **55** | the pane's 1 DIP top border is gone; the band arrives |
| `WindowHost.TitleStripHeightDip` (`:58`) | 30 | **30** | unchanged; still tied to `ExtendClientAreaTitleBarHeightHint` and the root Grid's row 0 by `CaptionHeight_IsTheSameNumberInAllThreePlaces` |
| `WindowHost.MinWindowWidthDip` (`:101`) | 872 | **711** | `872 − 161 = 711` was always the *content pane* number (`WindowHost.cs:85-94`); with `chromeWidth = 0` the window width **is** the content pane |
| `MainWindow.axaml:6` `MinWidth` | 872 | **711** | mirrors the above; `MinWindowWidth_IsTheSameNumberInBothPlaces` enforces it |
| `WindowHost.PlaceholderWidthDip` (`:161`) | 1112 | **1000** | it moved 1000 → 1112 by the pane's 112 DIP growth (`:154-159`); the growth is reverted |
| `MainWindow.axaml:6` `Width` | 1112 | **1000** | kept equal to the placeholder on purpose (`:154-159`) |
| `WindowHost.MinWindowHeightDip` (`:129`) | 568 | **568** | unchanged |
| `WindowHost.PlaceholderHeightDip` (`:162`) | 688 | **688** | unchanged |
| `WindowHost.BottomGapDip` (`:142`) | 8 | **8** | unchanged |

**The 711 has to clear one new floor.** With the field left-aligned immediately after 关于 (§0 ruling 6),
the band's minimum width is the five menu items, then the separator's whole footprint (`11 + 1 + 11 = 23`
DIP), then the 320 DIP field, then 11 DIP of trailing margin so the field never touches the window edge.
ESTIMATED for the English strings (`Options` / `Open Main Folder` / `Refresh` / `Exit` / `About` = 39
characters at ~6.2 DIP average advance at `AeroFontSizeNormal` = 12, plus 5 × 16 DIP `Padding="8,0"`,
plus 5 × 2 DIP `BackgroundBorder Margin="1"`) ≈ 332 DIP for the menu and `23 + 320 + 11 = 354` DIP for
the group after it, **≈686 DIP total**. 711 clears it by 25. **This estimate is the one number in this
document I could not measure, and T3's verification measures it** — read every `MenuItem`'s UIA
`BoundingRectangle.Width` at `general.language = en-US` and sum. If the sum exceeds 700,
`MinWindowWidthDip` rises to `ceil(sum + 354)` and `MainWindow.axaml`'s `MinWidth` with it.
(Left-aligning does not change this floor: the same five terms are laid end to end either way. What it
changes is that below the floor the field is the thing that clips, not the menu — which is why the
trailing 11 is a margin on `SearchGroup` and not a `Grid` column.)

**`ShellConstantTests` fails on contact and must be edited in the same commit.**
`RailWidth_MatchesTheRailPaneWidth` (`:82-97`) hard-requires an `x:Name="NavRailPane"` with a `Width`
**and** a `RailWidthDip` constant, and `Single()` (`:242-249`) asserts **exactly one** match — deleting
either side fails it with "found 0". It is **deleted**, and its successor
`MenuBandHeight_IsTheSameNumberInAllThreePlaces` makes **three** reads, on the model of
`CaptionHeight_IsTheSameNumberInAllThreePlaces` (`ShellConstantTests.cs:59-80`):
`<x:Double x:Key="MenuBarHeight">(?<v>…)</x:Double>` out of the vendored
`Themes/Aero/ControlStyles/Menu.axaml`; `MenuBandHeightDip\s*=\s*(?<v>…)` out of `WindowHost.cs`; and
`x:Name="MenuBand"[^>]*Height="(?<v>…)"` out of `MainWindow.axaml`. Two-of-three is not enough: the
painted band and the fit's chrome term can drift apart on their own, and nothing errors when they do.
`MinWindowWidth_IsTheSameNumberInBothPlaces` is **kept**, with new numbers.

**Four measurement records lose their premise for the third time.** `WindowHost.cs:88-92` names them:
`Launcher.axaml`'s `tm-card-value` and `tm-card-actions` blocks, `FoldersCard.axaml`'s wrap
measurement, and `WindowHost`'s own callers — all stated as "872 DIP window, 711 DIP content pane".
Two of the four (`tm-card-value`, `tm-card-actions`) are **deleted outright by T6**. `FoldersCard`'s
wrap measurement becomes a statement about the **Options dialog's** width, not the window's, and must be
re-taken in T6's verification. `WindowHost`'s own remark is rewritten to "711 DIP window, 711 DIP
content pane".

---

## 6. The page-model teardown (T2)

**Deleted:**

- `enum ShellPage`, `interface IShellNavigator`, `sealed class ShellNavigator` — the whole of
  `Services/IShellNavigator.cs` (36 lines). Its DI registrations at `App.axaml.cs:126` and `:130`.
- `MainWindow.ShowPage` (`:703-714`), `ShowPageCore` (`:716-786`), the `_inShowPage` field and its
  57-line remark (`:61-79`), the `wasSettings` local (`:723`), the two `SelectedIndex` writes
  (`:725-726`), the `PagePane.Classes.Set("app", …)` write (`:762`), and the `Loaded`-posted
  `RefitToContent` on 设置→应用 (`:784-785`).
- `OnNavSelectionChanged` (`:807-845`) and the `_navReady` field (`:59`) — both `ListBox`es go.
- `_settingsPage` (`:44`), its lazy construction (`:727-729`) and `OnClosed`'s `_settingsPage?.Dispose()`
  (`:457`).
- `Views/SettingsPage.axaml` (92 lines) + `.axaml.cs` (46 lines).
- `WindowHost`'s `_navigator` field (`:166`), its constructor parameter (`:302-304`), and `Hide`'s
  `_navigator.Navigate(ShellPage.App)` (`:1021`).
- `MainWindow.OnKeyDown`'s Escape `else _host.Hide()` branch (`:510-513`) and the Ctrl+E page guard
  (`:499-500`) — both collapse to the single-page case. **Escape now always routes to
  `_appPage.ViewModel.HandleSearchEscape()`**, which is what the launcher did before the rail existed
  (`:466-470`).
- `Border.tm-page-pane:not(.app)` — 设置 was its only subject.

**Survives:**

- `_appPage` (`:43`) and its eager field-initializer construction; `PageHost.Content = _appPage` once,
  in the constructor.
- `ConsumePendingFocus` + `PendingFocus` (`:915-956`) — still needed for summon / Ctrl+E / 清除筛选 /
  Up-off-the-grid focus. Its page test becomes trivially true and is kept for the same reason as below.
- `PagePane` and `Classes="tm-page-pane"`, minus the class flip.
- `SettingsLauncher` — rewritten body, same two-member interface, same call sites (tray menu at
  `TrayService.cs`, empty state at `AppPage.axaml:416`).

**`RefitToContent`'s detach guard: RETAINED VERBATIM, remarks rewritten.**

```csharp
if (!ReferenceEquals(PageHost.Content, _appPage))
    return;
```

After Round 8 `PageHost.Content` has **exactly one writer** — `MainWindow`'s constructor — so the guard
is a constant `true` and the reproduced defect (a window latched at **850x568 DIP** for the session, when
the guard was temporarily removed; record in
`docs/superpowers/reports/2026-08-02-win11-shell-round3-findings.md`) is unreachable. **It is kept
anyway, and this is not defensive tidiness:** it costs one reference comparison, and it is the literal
line whose removal produced that defect. Deleting it converts a documented, reproduced failure into a
latent one that returns the instant anything ever reassigns `PageHost.Content` — and Round 8 adds a
second window that owns page-like content, which is precisely when that becomes plausible again.

What the guard *means* changes, and the remark must say so: it stops being a **page test** ("is 设置 on
screen") and becomes an **attachment invariant** ("`_appPage` is hosted, therefore styled, therefore
measurable"). The mechanism paragraphs (`:271-341` — the decompiled `InvalidateStyles` /
`FrameType.Style` trace, and the note that `TmTileButton`'s theme frame survives a detach while every
class-scoped style frame does not) stay verbatim; they are still what makes a detached measurement
wrong. The paragraph about `SettingsLauncher.OpenCore` reaching the defect through
`ShowAndActivate` → `Opened` → `InitializeAndFitAsync` (`:343-348`) is **deleted** — `OpenCore` no
longer navigates, and a modal `ShowDialog` does not reparent the owner's content.

**Rejected alternative:** replacing it with `((Visual)_appPage).GetVisualRoot() is null`. It is a truer
attachment test, but it changes behaviour in a case nobody has measured — whether a hidden Avalonia
window's content still reports a visual root — and the fit is the one subsystem in this app with a
reproduced regression history. Not worth it for zero functional gain.

---

## 7. The Options dialog in full

**Modality: modal.** PUBLISHED, `win-property-win`: *"Options dialog boxes. Properties for an
application are displayed in a modal dialog box."* Reinforced by `win-dialog-box`'s strict pairing —
modal ⇒ delayed commit ⇒ OK/Cancel; modeless ⇒ immediate commit ⇒ Close; *"Don't use OK buttons in
modeless dialog boxes"*; *"Don't use Cancel buttons in modeless dialog boxes. Rather, use Close
instead."* Having chosen delayed commit, modeless is not available.

**Title:** 选项 / **Options**. PUBLISHED, `win-property-win`: *"Options dialog box titles — Use
'Options.' Use title-style capitalization. Don't use ending punctuation."* Not "TanMenu 设置".

**Caption:** the system caption, as `MessageBoxWindow` uses. `CanResize="False"`,
`ShowInTaskbar="False"`, no `Icon` — PUBLISHED, `win-dialog-box`: *"Dialog boxes don't have title bar
icons"*, *"Resizable dialogs can have a Maximize button"* (so a fixed one has none), *"Don't disable the
Close button."* MainWindow's hand-drawn Aero caption is **not** reused: re-drawing chrome for a second
window is authoring more of exactly what item 1 strips, and `MessageBoxWindow.axaml:11-17` already
records the rule — *"a system caption is right for a platform surface."* **Whether Win32 still draws a
greyed minimize box on an Avalonia `CanResize="False"` window is COULD NOT VERIFY without a build; T2's
verification checks it via UIA and, if present, `SystemDecorations` is narrowed.**

**Layout:** a `DockPanel` — `TabControl` filling, commit row docked `Bottom`. `Margin="11"` on the root
(PUBLISHED dialog margin). **No `ScrollViewer` anywhere** (PUBLISHED: *"Don't scroll property pages.
Instead of using scrollbars, make the property pages larger and lay out the pages efficiently."*). The
`ScrollContentPresenter` padding bug `SettingsPage.axaml:22-41` documents at length becomes moot with
the scroller.

**Tabs:** horizontal, `TabStripPlacement="Top"` (the vendored `TabItem` theme's default branch), five
after T4 — 外观 · 行为 · 文件夹 · 常用工具 · 更新. PUBLISHED: ≤ 7 ⇒ horizontal; *"Use concise tab
labels. Use one or two words"*; *"Don't put icons on tabs"*; *"Don't assign a unique access key"* (tabs
cycle with Ctrl+Tab); *"Don't assign effects to changing tabs"*; *"Otherwise, select the first page by
default."* The tab index is **not** persisted — *"If users are likely to start with the last page
displayed, make the page tab persist"* is conditional and there is no evidence for the antecedent here.

**Commit row:** right-aligned, single row, bottom, above nothing else.

| | Label | Size | Access key | State |
|---|---|---|---|---|
| 1 | 确定 / OK | 75 × 23 | **none** | `IsDefault="True"` |
| 2 | 取消 / Cancel | 75 × 23 | **none** | `IsCancel="True"`, never disabled |
| 3 | 应用 / Apply | 75 × 23 | **A** | disabled unless `draft.IsDirty` |

`Spacing="7"` between them, `Margin="0,11,0,0"` above. All PUBLISHED (§2). No access key on 确定/取消
because *"Enter and Esc are used for their access keys"*; `A` on 应用 because *"Assign 'A' as the access
key."* `Ok` and `Cancel` strings already exist (`AppLanguage.cs:18-19`); `Apply` is new.

**Apply is a NAMED DEVIATION.** PUBLISHED, `win-property-win`: *"Use Apply buttons only with property
sheets, not with options dialog boxes."* PUBLISHED, `win-dialog-box`: *"Don't use Apply buttons in
dialog boxes that aren't property sheets or control panels."* Both are set aside, deliberately, because
(a) the user chose the three-button set, and (b) the guideline that admits Apply is satisfied on its own
terms — *"Provide an Apply button only if the property sheet has settings (at least one) with effects
that users can evaluate in a meaningful way"* — and TanMenu has three such settings whose effect is
visible on the launcher behind the dialog: 外观 (theme variant), 字体, 每列个数. Apply is also what
gives back the live 外观 preview T1 removes.

**Semantics.** 确定 = commit + close. 应用 = commit + stay open + re-disable itself. 取消 = discard +
close. Title-bar × = the same as 取消 — PUBLISHED, `win-property-win`: *"Make sure the Close button on
the title bar has the same effect as Cancel or Close. Never give it the same effect as OK."*

**The unsaved-changes prompt is a NAMED DEVIATION.** PUBLISHED, `win-property-win`: *"Don't confirm if
users discard their changes."* The recon reports a narrow exception for high-effort settings and for the
title-bar × specifically (r8-win7 §3.2) — **that is a paraphrase in the recon, not a verbatim quote, and
I could not verify the exception's exact wording.** The instruction for this round is that closing with
unsaved edits prompts, so:

- 取消 **and** the title-bar × both prompt, identically, when `draft.IsDirty`. Making them identical is
  what preserves the one rule that is verbatim ("the same effect as Cancel").
- The prompt is the existing `MessageBox` with `MessageBoxButtons.YesNo`, bracketed by
  `SuppressHide(true/false)`.
- The defensible ground for the deviation: two of the dialog's settings are high-effort — a captured
  global hotkey (`BehaviorCard.OnHotkeyKeyDown`) and a picked root folder
  (`FoldersCard.OnPickRootClick`), each of which costs the user a real interaction to re-enter.

**Sizing and placement.** `WindowStartupLocation="CenterOwner"` with `MessageBox.ShowCoreAsync`'s
hidden-owner fallback to `CenterScreen` (`Dialogs/MessageBox.cs:84-97`) — PUBLISHED: *"Initially display
dialogs 'centered' on top of the owner window"*, *"Don't use the Always on Top attribute."* Explicit
`Width`/`Height` rather than `SizeToContent`, because a tabbed dialog that resized per tab would violate
*"Don't assign effects to changing tabs."*

**The size is a PREDICTION, and the fallback is SETTLED (§0 ruling 8).** Provisional
`Width="540" Height="540"` — the deleted `SettingsWindow`'s own numbers
(`git show 28158a8^:src/TanMenu.Wpf/SettingsWindow.axaml`, line 22:
`Width="540" Height="540" MinWidth="480" MinHeight="460"`), which held the same six groups under a
`TabControl`. It clears the PUBLISHED floor (*"Support the minimum Windows Vista screen resolution of
800 x 600 pixels"*). **T2's verification (step 8) measures each tab's desired size and pins
`Width`/`Height` to the maximum plus margins**; the provisional number is a starting point, not a claim.
If the measured maximum will not fit inside `800 × 600`, the ruling is **split the 文件夹 tab in two**
(six tabs, still ≤ 7) and keep `win-property-win`'s *"Don't scroll property pages."* The softer
`winenv-ctrl-panels` sentence — *"You can vertically scroll long pages, as long as the most important
controls are visible without scrolling"* — is **not** taken.

**A failed OS effect at commit time owns to THIS window, not to the launcher.** §T1's "The config write
is atomic. The OS effects are not." settles it: `OptionsWindow` publishes a static `Current`, and
`App.WarnHotkeyRegistrationFailedAsync` (`App.axaml.cs:263-278`) resolves its owner as
`OptionsWindow.Current ?? host.Window`. Owning it to `host.Window` while this dialog is modal on that
window is a `ShowDialog` on a disabled owner; owning it here is not.

**`WindowHost` does not and must not serve it.** `WindowHost` is bound to one window (`_window`,
`WindowHost.cs:168`); `RestorePlacement`/`FitToContent`/`SavePlacement`/`Hide`/`ShowAndActivate` all read
and write it. `WindowPlacement.ResolveBottomCentred` is a pure static and *could* be called, but its rule
is "horizontally centred, bottom edge 8 DIP above the taskbar" (`WindowPlacement.cs:11-17`) — the
launcher's summon anchor, not a dialog's placement. The dialog needs no fit at all: settings content is
authored, not scanned from disk, which is the entire reason `FitToContent` exists.

**`ISettingsCard`, the `_loading` guards, `SettingsWrite.CommitAsync`, and the OS-backed refreshes** —
all four settled in T1/T2 above. In one line each: `ISettingsCard` keeps its single member and gains a
sentence; every `_loading` guard is untouched because the property paths are unchanged;
`SettingsWrite.CommitAsync` survives with one caller (`AppPage.axaml.cs:204`); the OS-backed refreshes
keep working for free because the dialog is constructed per open.

**How a settings change reaches the launcher.** Unchanged in mechanism, changed in frequency:
`SettingsCommit.ApplyAsync` → `AppEvents.RaiseSettingsChanged()` →
`LauncherViewModel.HandleSettingsChangedAsync` (async rebuild) → `ContentMetricsChanged` →
`MainWindow.OnLauncherContentMetricsChanged` → `RefitToContent` at `DispatcherPriority.Loaded`. Also on
that signal: `MainWindow.ApplyWindowFont` and `ApplyMenuText` (`MainWindow.axaml.cs:95-96`),
`TrayService.ApplyMenuText`, `WindowHost.ApplyTopmost`, and the live `OptionsWindow`'s own re-label.

---

## 8. Cross-cutting

### 8.1 Re-fit triggers after Round 8

Five today (`MainWindow.axaml.cs:247-258`). **Four after**:

1. `InitializeAndFitAsync` — the startup build, on every summon (`:237-241`).
2. `OnNoticeVisibilityChanged` — a docked notice opening or closing (`:411`).
3. `OnLauncherContentMetricsChanged` — a 每列个数 / 字体 / 语言 change, now once per commit (`:438-439`).
4. `OnPropertyChanged` — Maximized → Normal (`:542-554`).

**Deleted: the 设置 → 应用 trigger** (`ShowPageCore:784-785`). There is no page switch.

### 8.2 The fit arithmetic, with a predicted geometry

**Measured baseline** —
`src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Data\cache\logs\log-20260804.txt:54`
(the only line on this machine with post-Task-8 chrome; the user-data log still shows `chrome 49x31` and
is a pre-Task-8 build — do not quote it):

```
Window fit to content: 1736.1666666666665,816 1647.6666666666667x568 DIP
  (page 1486.6666666666667x508.6666666666667 + chrome 161x31) on work area 0,0 5120x1392 at scale 1.5
```

**Terms after Round 8:**

| Term | Before | After | Why |
|---|---|---|---|
| `chromeWidth` | 161 | **0** | pane deleted, pane border deleted |
| `chromeHeight` | 31 | **55** | `30 + 25`; the pane's 1 DIP top border deleted, the band arrives |
| page width | 1486.667 | **1486.667** (PREDICTED unchanged) | the search header contributed `320 + 22 = 342` DIP of desired width, far below the tile grid's; removing it cannot move a maximum |
| page height | 508.667 | **470.667** (PREDICTED) | `Border.tm-header` = field `MinHeight` 23 + `Padding="11,7"`'s 7 top + 7 bottom + its 1 DIP bottom border = **38** |

`ResolveContentFit` → `ResolveBottomCentred(1486.667 + 0, 470.667 + 55, …)`
(`WindowPlacement.cs:105-116`):

- W = 1486.667, ≥ `MinWindowWidthDip` 711 → **1486.667**
- H = 525.667, < `MinWindowHeightDip` 568 → clamped to **568**
- x = (5120 − 1486.667) / 2 = **1816.667**
- y = 1392 − 568 − 8 = **816**

**PREDICTED log line:**

```
Window fit to content: 1816.6666666666667,816 1486.6666666666667x568 DIP
  (page 1486.6666666666667x470.6666666666667 + chrome 0x55) on work area 0,0 5120x1392 at scale 1.5
```

**Flagged as a prediction, and here is what it rests on.** Round 4's equivalent missed by 16 DIP through
a bad input and Round 7's by a modelled border term, so the inputs are stated separately: the *page
width* is asserted unchanged (an argument, not a measurement — if the tile grid ever measured below 342
DIP the header would have been the binding term and the prediction fails, which is impossible on a
non-empty launcher but is the assumption); the *page height* delta of 38 is arithmetic on
`Launcher.axaml:512-516` and `:570-577`, both read this session; the *chrome* terms are the constants
above. **The only observable of `chromeHeight` is the log line's own `chrome {w}x{h}`** — 525.667 + 0 is
below the 568 floor, so the window's height is identical either way (`WindowHost.cs:50-56`).

**Headroom.** `568 − 525.667 = 42.33 DIP`. The band could grow to **67 DIP** before the window's height
starts to move. That is real but not unlimited, and it is stated rather than assumed.

**Placeholder.** `(5120 − 1000) / 2 = 2060`, which is exactly the x the log shows for the pre-Task-8
`1000x688` placements (`log-20260804.txt:22`, `:35`) — a free cross-check that the revert is arithmetically
correct.

### 8.3 Test delta, by name

194 tests pass today (147 `[Fact]`/`[Theory]` attributes).

**Deleted (3):** `ShellConstantTests.RailWidth_MatchesTheRailPaneWidth`;
`SettingsRowTests.EveryCardBorder_WrapsTheStandardColumnGrid`;
`SettingsRowTests.EveryCardHeader_IsOneTitleLineAndOneSubtitleLine`.

**Rewritten (2):** `SettingsRowTests.NoCardFile_RestatesAConstantTheThemeOwns` →
`NoSettingsFile_RestatesAConstantTheThemeOwns` (ladder extended to the published 3/9/16 rungs, scope
widened to the dialog); `SettingsRowTests.NoHeaderSubtitleString_ContainsAHardLineBreak` →
`NoRowLabelString_ContainsAHardLineBreak`.

**Unchanged but now covering more (2):** `AeroAttributionTests.EveryVendoredAeroFile_CarriesTheUpstreamCopyrightHeader`
and `NoVendoredFile_ReferencesTheInterFontFamily` — **21 → 24** vendored files (TabControl and TabItem in
T2, Menu in T3). Counted from the test rather than from the directory: `VendoredFiles()`
(`AeroAttributionTests.cs:93-96`) is `Directory.GetFiles(AeroDir, "*", AllDirectories)` **minus** the
three names in `NotVendored` (`:58-79`), and the directory holds 24 files today, so the set under test
is 21. `Menu.axaml` carries three "Changes from upstream" entries, which the header form already
requires a section for.

**Unchanged (7+):** all four `ShellTokenTests`; `ShellConstantTests.CaptionHeight_IsTheSameNumberInAllThreePlaces`,
`.MinWindowWidth_IsTheSameNumberInBothPlaces` (new numbers), `.NoTemplatePartSelector_NamesAPartOutsideTheAllowList`
(**allow-list stays empty** — every new `/template/` target is declared inside a vendored template under
`src/`); `AppPageGroupTests.GroupPanel_CarriesTheVisibilityBinding` (`tm-group-panel` is retained).

**New (≈14):** `ShellConstantTests.MenuBandHeight_IsTheSameNumberInAllThreePlaces`,
`.CommandButtonHeight_IsTheSameNumberInBothPlaces`; `SettingsDraftTests` **×6** (§T1);
`MenuBarTests.FiveCommands_InOrder`, `.NoMenuItemHasChildren`, `.NoLabelEndsInAnEllipsis`,
`.AccessKeysAreDistinct`; `OptionsWindowTests.CommitRow_IsOkCancelApplyRightAligned`,
`.NoTabHostsAScrollViewer`, `.EveryTabHeaderComesFromAppLanguage` (source test: six `TabItem`s, each
labelled from `OptionsWindow.ApplyText()` and none carrying a literal header string — the guard against
T6's `GroupHead` deletion silently leaving a tab unnamed).

**Net: ≈194 → ≈205.**

### 8.4 The config contract

**Nothing on disk breaks.** No property is added, removed or renamed in `AppConfig` or `GeneralConfig`.
`AppConfig.CurrentSchemaVersion` stays **1** — there is no migration, because there is no value to
migrate.

**Every retired field stays deserializable**, and each one's remark is checked: `WindowWidth`,
`WindowHeight`, `WindowLeft`, `WindowTop`, `NavRailExpanded`, `ShowInTaskbar`, `ButtonSize`. **One
remark is factually wrong and T9 fixes it**: `NavRailExpanded`'s says *"the rail is permanently 48 px
icon-only"*, which Round 7 Task 8 falsified (160, labelled) and Round 8 falsifies again (no rail). It
stays retired-in-place, with a correct remark.

The draft's clone goes through the same `ConfigJsonContext` as the file, so a property that survives a
save survives a clone. `SettingsDraftTests.Clone_RoundTripsEveryRetiredField` asserts it.

**`SettingsCommit` writes only the eleven named properties** (§T1) — `WindowMaximized`,
`TaskbarPinPromptDismissed`, `SchemaVersion`, `ThemeName` and every retired field are provably untouched,
which is what stops a commit from reverting `WindowHost.SavePlacement`'s concurrent write.

### 8.5 NativeAOT

Nothing new is reflection-based. The draft clone uses the existing source-generated
`ConfigJsonContext.AppConfig` `JsonTypeInfo` — the same one `LoadAsync`/`SaveAsync` use
(`ConfigService.cs:18`, `:54`, `:145`). `Menu`, `MenuItem`, `TabControl`, `TabItem`, `CheckBox` and
`Window` are ordinary Avalonia controls whose templates are XAML-compiled; `Click` handlers are named
methods bound by the XAML compiler, not by reflection. No new `PackageReference`, no new trimming root,
no `[DynamicDependency]`. Verified by inspection, not by an AOT publish — **T9's verification runs
`dotnet publish -c Release` with `PublishAot=true` and asserts zero `IL2xxx`/`IL3xxx` warnings.**

### 8.6 Light and dark

Every key the band, the tabs and the dialog need is already defined in **both** variants — verified this
session by grepping `Themes/Aero/Variant/Light.axaml` and `Dark.axaml`:

- Menu bar: `AeroMenuBarBackgroundColor0/1`, `ShineColor0/1`, `SideBorderBrushColor0/1`,
  `BottomOuterBorderBrushColor`, `BottomInnerBorderBrushColor`, `ItemHoverBackgroundColor`,
  `ItemHoverBorderBrushColor`, `ItemSelectedBackgroundColor`, `ItemSelectedBorderBrushColor` —
  `Light.axaml:264-284`, `Dark.axaml:264-284`.
- Tabs: `AeroTabControlBackgroundColor`, `OuterBorderBrushColor`, `InnerBorderBrushColor` —
  `Light.axaml:151-155` and its Dark twin; the `AeroButton*` family both variants already ship.

`ShellTokenTests.EveryThemedTokenIsDefinedInBothVariants` continues to guard `Tokens.axaml`.

**One inherited dark-mode defect, declared and NOT fixed.** `Variant/Dark.axaml:279` carries upstream's
own `<!-- [TODO: Dark-ify AeroMenuBarItem* colours] -->`, and the four item-state colours are
**byte-identical in both variants** — `#10FFFFFF` hover fill, `#40000000` hover border, `#20000000`
selected fill, `#A0000000` selected border. Over the Dark bar (`#FF1A2133 → #FF292E3E`) a 6 % white
hover is close to invisible. **Round 8 does not change them**, for a reason it can defend: a hover state
cannot be produced on this machine (no mouse, and no synthetic path sets `:pointerover`), so any new
value would be authored blind — and authoring colours into a vendored variant file without a measurement
is exactly what the accuracy rule forbids. The defect is recorded here and in the vendored file's change
list as **COULD NOT VERIFY, inherited from upstream.**

---

## 9. Decisions that were open, and how they were settled

**None are open.** Both were ruled on before the tasks were cut, and both rulings live in §0 so nothing
downstream has to re-argue them. They are restated here only so a reader coming to this section for an
answer finds one.

1. **Menu bar, not a Windows 7-style toolbar — SETTLED, and recorded as a deliberate departure.**
   The user asked for "windows 经典的 mainmenu" in those words, and the user's word wins. **§0 ruling 7**
   carries the ruling and the three published sentences that point the other way — `cmd-menus`'s *"The
   presence of menus goes without saying in a menu bar"*; `cmd-toolbars`'s *"Partial toolbars can be
   combined with navigation buttons, a search box, or tabs"* and its Windows 7 exception right-aligning
   "standard, well-known commands such as Options, View, and Help"; and the absence of any published
   toolbar height, which is what would have dissolved the 20-vs-23 arithmetic that forces the band to
   25 DIP. Those three are the cost of the ruling, not an argument against it; §2 and §3.3 both point
   back at §0 ruling 7 rather than restating them.

2. **The dialog's size stays provisional at `540 × 540`, with T2 measuring — SETTLED.** **§0 ruling 8**
   carries it, §7 carries the derivation, and T2's verification step 8 is where the number is pinned. If
   the measured maximum does not fit inside the PUBLISHED `800 × 600` floor, the ruling is **split the
   文件夹 tab** (six tabs, still ≤ 7) and keep *"Don't scroll property pages"*; `winenv-ctrl-panels`'
   softer scrolling sentence is **not** taken.

**Still flagged as COULD NOT VERIFY, and each says so where it is used** — these are unmeasured facts,
not unsettled choices, and every one has a verification step assigned:

- No Microsoft-shipped Windows 7 application with a text field in its menu bar (§0 ruling 6).
- The vendored `Menu.axaml` template's own left inset, which the search group's spacer is offset by
  (§3.3; measured in T3's verification step 4).
- Whether Win32 draws a greyed minimize box on an Avalonia `CanResize="False"` window (§7).
- The exact wording of `win-property-win`'s unsaved-changes exception (§7).
- The band's minimum width, ESTIMATED at ≈686 DIP (§5; measured in T3's verification).
- Upstream's own dark-mode `AeroMenuBarItem*` defect, inherited and not fixed (§8.6).

---

## 10. The record — Round 8 as shipped

Written by **Task 9** against the tree at `7b655c9` plus this task's own edits. Everything below was
read out of the code, the commits or an instrument this session; nothing is inferred from §§0-9.

### 10.1 The nine tasks

| Task | Commits | Tests | What shipped, and where it left the design |
|---|---|---|---|
| T1 delayed commit | `bf770d7` | 194 → 206 | `SettingsDraft` + `SettingsCommit` as specified. 开机自启 buffered by hand, as §T1 said it must be. |
| T2 the Options dialog | `d5b72c8` + `22f450b` | → 213 | Dialog **MEASURED at 711 × 452**, not the provisional 540 × 540. Page model deleted whole. `TabControl.axaml`, `TabItem.axaml` and `Converters/AdjustThicknessForTabsConverter.cs` vendored. Three caption facts (minimize box, maximize box, icon) had to be fixed in Win32 rather than in markup. |
| T3 the menu band | `b354c79` + `92082ef` | → 218 | As designed, 25 DIP band and left-aligned group included. `ControlStyles/Menu.axaml` vendored with three one-line changes. Every fit term moved and the §8.2 prediction held (see 10.2). |
| T4 the About box | `c4442ca` + `bec7ae1` | → 221 | Six tabs → **five**. `Dialogs/AboutWindow` + `Dialogs/DialogCaption`. |
| T5 check boxes | `e52144e` | 221 | Five `ToggleSwitch`es → `CheckBox`es; `ToggleSwitch.tm-card-toggle` deleted; the vendored theme stays on disk, recorded consumerless. |
| T6 the DLU grid | `7e9bee6` + `d2267b4` | → 220 | Went past its stated blast radius, with reasons: the dialog's height **re-measured 452 → 479**, 维护 split into 缓存 + 备份, four button strips re-docked, the draft-language `MessageBox` defect fixed, `AboutCard`'s doubled 关于 head deleted. Its review then found the whole surface had lost its body size and gave every card root `FontSizeBody`. |
| T7 the strip | `87ac2ea` | → 221 | Two rules and three keys deleted, `FontSizeBody`/`FontSizeSubtitle` re-declared as aliases, `TmButton` to the published 75 × 23. |
| T8 radii and weights | `e2c5b72` + `7b655c9` | → 222 | Extended row 3 to all three `SemiBold` setters, with the published table quoted for the extension, and added `ShellTypographyTests` — which §T8 had said a `dotnet test` could not do, true of a rendered assertion and not of a source one. |
| T9 the record | this commit | 222 | Below. |

### 10.2 Where this design's own numbers were wrong, provisional or beaten

| §  | The design said | The build says |
|---|---|---|
| §0 r8, §7 | dialog **provisional 540 × 540**, T2 to measure | **711 × 479** client (725.33 × 516 DIP outer at 150 %, MEASURED again by T9 through `GetClientRect`/`GetWindowRect`). Clears the published 800 × 600 floor. |
| §0 r8 | if it does not fit, **split 文件夹** → six tabs | not needed. T6's re-layout took 108.67 DIP off the tab; the strip holds **five** tabs, MEASURED live by T9 in the UIA tree: 外观 \| 行为 \| 文件夹 \| 常用工具 \| 更新. |
| §8.3 | *"Net: ≈194 → ≈205"* | **222**. The estimate missed by 17: `SettingsDraftTests` alone shipped 14 attributes against the six §T1 named, `MenuBarTests` 5 against 4, `OptionsWindowTests` 5 against 3, and `AboutWindowTests` + `ShellTypographyTests` were not forecast at all. |
| §8.2 | the PREDICTED log line | matched, to every field: `1486.6666666666667x568 DIP (page 1486.6666666666667x470.6666666666667 + chrome 0x55)`, re-read by T9 off a fresh sandbox log. **One digit differs**: the x is `1816.6666666666665`, not the design's `…667` — the design did the arithmetic, the code prints the double. |
| §5 | band minimum width **ESTIMATED ≈686 DIP** | **MEASURED 646 DIP** in Chinese and **630.67 DIP** in English, by the review round through UIA at 150 %: the menu run (`MenuOptions.Left` → `MenuAbout.Right`) is **438 px = 292 DIP** / **415 px = 276.67 DIP**, plus the separator's whole 23 DIP footprint (`Width` 1 + `Margin` 11,3), plus the 320 DIP field, plus the 11 DIP trailing margin. **T9 measured 670 / 663.33 DIP** off a run of **474 / 464 px**; the review round's relabelling of 打开主目录 → 主目录 took 36 px (two CJK glyphs at 12 DIP) off the Chinese run and 49 px ("Open ") off the English one, which is the whole of the difference. Both figures, before and after, are under `MinWindowWidthDip` 711, so the estimate's *conclusion* — that the band does not set the window's floor — holds with more room than it claimed. |
| §1, §8.3 | `AeroResources.axaml` merges **fourteen** `ControlStyles` dictionaries; **21** vendored files | **17** and **25**. `Menu`, `TabControl`, `TabItem` and one converter were vendored by this round's own tasks. The half of §1 that matters is untouched: every `TargetType` under `Themes/Aero/` is still a *control*, so a rule that gives a `Border`/`TextBlock`/`StackPanel`/`Image`/`Panel` its geometry still has no theme value under it. |
| §3 T9 | *"update `MainWindow.axaml.cs`'s class summary (it still describes the pane and the two pages)"* | **already done, by T3 and its review.** T9 read it and changed nothing: the summary describes the band, the single page and the two moves, and names the pane only as what was deleted. Recorded here because "the task did nothing" is a result worth being able to check. |
| §T3 | the vendored `Menu.axaml` left inset — COULD NOT VERIFY | resolved in T3: `PART_ItemsPresenter` is a *sibling* of the bordered `Border`, so there is no inset to correct, and the gap was measured to confirm it. |
| §7 | whether Win32 draws a greyed minimize box — COULD NOT VERIFY | resolved in T2: the first build drew an **enabled** minimize box, a greyed maximize box and the app icon. `Dialogs/DialogCaption` fixes all three in Win32; both dialogs read style `0x16C80000`, exstyle `0x00200101`. |

### 10.3 Two environment facts this round established, recorded because no spec held them

**1. `dotnet publish` needs `vswhere.exe` on `PATH`, and it is not there by default.** ILC shells out to
it to locate the MSVC linker and the Windows SDK libraries for the native link step. On this machine
`Get-Command vswhere.exe` finds nothing while
`C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe` exists, so every publish in this
round ran after:

```powershell
$env:PATH = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:PATH
```

The failure lands *after* several minutes of compiling, which is why it is worth knowing in advance.
The two GitHub workflows already do the same thing in a step named *Ensure vswhere is on PATH*, and
`README.md`'s Publish section now carries the one-liner for a local run.

**2. Synthetic `Ctrl`+*key* cannot be tested in this app, at all.** `PostMessage` is the only key
instrument available here (there is no real keyboard, and `SendInput`/`SetForegroundWindow` no-op), and
it **cannot express a modifier**: Avalonia's Win32 backend derives `KeyModifiers` from the live keyboard
state, which `PostMessage` never updates, so a posted `WM_KEYDOWN` for `E` arrives as plain `E` no
matter what is posted around it. Task 3's review established this with a control experiment — synthetic
`Ctrl+E` moved focus neither from a band item nor from inside the page, where the code path is not in
dispute — so a negative result measures the instrument, not the code. **A test that posts a modifier
proves nothing; do not write one, and do not read an old one as evidence.** Plain keys work (Alt, the
arrows, Enter, Escape) and UIA patterns work, which is how every keyboard verification in this round was
actually driven. The finding also lives in `MainWindow.axaml.cs`'s `Ctrl+E` branch, where someone
changing that accelerator would meet it.

### 10.4 What Task 9 itself changed

- `Directory.Build.props` **0.13.0 → 0.14.0**, with the reason and the "still nothing tagged since
  v0.9.6" fact in the comment.
- `README.md` rewritten for the shipped shell: the menu band, the Options dialog and its delayed
  commit, the row grammar, the About box, `MinWidth` 711 and the 1000 × 688 placeholder, the test
  inventory, the vendoring counts (fifteen control themes, three converters, 25 vendored files) — and
  the glyph census, which was **26 instances over 21 code points** and is **7 over 6**.
- `packaging/portable/RELEASE-NOTES.md` rewritten for 0.14.0 in both languages, still addressed to a
  user coming from 0.9.x because nothing between has ever been packaged.
- `AppConfig.cs`'s `NavRailExpanded` remark, which claimed *"the rail is permanently 48 px icon-only"*
  — false since Round 7, Task 8 and false twice over since Round 8, Task 3. Its two test comments
  carried the same sentence and were corrected with it.
- Supersession headers on the five Windows 11 specs and the two Aero ones, each naming what Round 8
  falsified rather than repeating "deprecated": the row spec's *"all sixteen rows still obey it"*, the
  shell redesign's *"the shell's SHAPE survives"*, Round 6's *"the field lives inside the 应用 page
  now"*, Round 4's rail argument, and the Aero conversion's whole navigation-pane task.
- `docs/portable-release.md` and `packaging/portable/README-PORTABLE.txt`: the English pin path is
  **Options > Behavior**, because the English menu label is Options (the Chinese one is still 设置).
- `MainWindow.axaml`'s band-width paragraph, which cited *"this task's report"* — a scratch file that
  is not in this repository. It carries the measured number now and points here.

### 10.5 Carried forward, knowingly

- ~~**Posting `Esc` to the Options dialog closes it *and* opens a second one.** Reported by T5, which
  reproduced it on a **pre-T5 build** — so it predates this round and no task here owns it. **T9 did
  not re-test it**; every dialog this task drove was closed through 取消's `InvokePattern` instead.~~
  **BOTH SENTENCES WERE FALSE AND THE BUG IS FIXED — see §10.6.** *"It predates this round"* does not
  follow from *"reproduced on a pre-T5 build"*: a pre-T5 build is still a **Round 8** build, and the
  dialog the bug happens in **did not exist before Round 8, Task 2**, so nothing about it can predate
  the round. This round owns it. *"T9 did not re-test it"* was true when written; the whole-round
  review then did, found the symptom **worse** than reported (no second window, no visible launcher
  either, and 设置 dead for the rest of the session), and the review round diagnosed and fixed it.
  The mechanism was also not what T5's report implied: **no second dialog is ever constructed.**
- **Dark-mode check-box contrast measures 2.35:1** (light 6.25:1). Those are the **vendored Aero
  brushes, unmodified**; no threshold is claimed here and nothing in this round changed them. *(The
  whole-round review could not reproduce that pair — its own sample, the 16 × 16 box against its own
  fill, gives 1.77 / 1.88:1 — and the region behind 2.35 / 6.25 is not stated anywhere in the record.
  Neither figure is confirmed here; what is certain is that nothing in this round painted them.)*
- **Upstream's own dark `AeroMenuBarItem*` defect** (§8.6) — four item-state colours byte-identical in
  both variants, carrying upstream's own `[TODO: Dark-ify …]`. Inherited, not fixed, because a hover
  state cannot be produced on this machine and authoring a colour blind is what the accuracy rule
  forbids.
- **`ControlContentThemeFontSize` = 14** reaches every Fluent-templated surface this app has not
  overridden — the message box among them. T6's review found it, fixed the settings surface at the
  card roots and deliberately did not re-point the key, because that moves type on surfaces nobody has
  measured. Recorded in `AppearanceCard.axaml`'s header.
- **Four modern idioms T8 declared and did not sweep**: `SplashPanel.axaml`'s two `FontWeight="Light"`
  lines, its `BoxShadow` and its accent gradient, and `MessageBoxWindow.axaml`'s icon-glyph `Bold`.
  The splash is a branded surface this round never touched otherwise, and the Bold is the guide's own
  sanctioned case. `ShellTypographyTests` names both files as **scope with a reason**, not as an
  exemption list.
- **Two COULD NOT VERIFYs stand**: no Microsoft-shipped Windows 7 application with a text field in its
  menu bar (§0 ruling 6), and the exact wording of `win-property-win`'s unsaved-changes exception —
  which is why the discard prompt is booked as a **named deviation** in `OptionsWindow.axaml.cs`
  rather than dressed up as compliance.

### 10.6 The review round — the five things the whole-round review sent back

**1 · HIGH — `Esc` in the Options dialog killed 设置 for the session. ROOT CAUSE FOUND, FIXED, TESTED.**

The review's hypothesis — a **second** `OptionsWindow` constructed, claiming `Current`, the first one's
`OnClosed` then declining to clear it — is **REFUTED**. Only one 选项 HWND ever exists. What happens,
measured by `EnumWindows` + `IsWindow`/`IsWindowVisible` over the app's own process before and after:

| | dialog HWND | launcher HWND |
|---|---|---|
| dialog up | alive, **visible** | alive, visible, **disabled** (modal) |
| after the posted `Esc` | **alive**, *invisible* | **alive**, *invisible* |
| +3 s, +12 s, after summoning the launcher | **alive**, still invisible | alive, visible again |

**The dialog is never closed. It is hidden.** The chain, each link measured rather than reasoned:

1. Avalonia routes a key to the **focused element**, not to the HWND the message arrived on. Opened
   from the menu band by keyboard, focus is still on the band, so an `Esc` posted to the *dialog's*
   HWND ran **`MainWindow.OnKeyDown`** → `HandleSearchEscape()` → the filter was empty → `_host.Hide()`.
   The log line `Config saved (0 folders)` in the same second is `Hide`'s own `FlushPlacement`, which
   is what identifies the caller: `autoClose` is `false` in that sandbox, so the `Deactivated` path —
   the one caller that *did* test the suppression count — cannot have been it.
2. **Hiding the launcher takes the modal it owns off screen with it, without closing it.** Both HWNDs
   go invisible together and both stay alive.
3. So `OnClosed` never runs, and `OptionsWindow.Current` goes on pointing at that window — **accurately**.
   Every later 设置 takes `SettingsLauncher.OpenCoreAsync`'s single-instance branch and `Activate()`s a
   window nobody can see. Silently, for the rest of the session, which is exactly the failure that
   branch's own remark predicted for a stuck guard. 关于 and 刷新 keep working because neither goes
   through it.

**Isolated against the instrument, in the same session:** with UIA focus moved *inside* the dialog
first, the identical posted `Esc` **closes it cleanly** (dialog HWND destroyed, launcher visible, 设置
reopens). So the trigger is stale focus on the launcher, which is reachable here because no window in
this session is ever the system foreground window (`GetForegroundWindow()` returns 0). **Whether a real
keyboard reproduces the trigger is still unverified** — but the *consequence* is instrument-independent
and is what was fixed: nothing may hide the launcher while it owns a modal.

**The fix, at the choke point:** `WindowHost.Hide()` returns immediately while `_suppressDepth > 0`.
`SuppressHide` already meant "something modal we own has the user's attention"; only the `Deactivated`
caller tested it, and that is the one caller that could never cause this. All three that could —
`MainWindow.OnClosing`, `HandleSearchEscape`, `LauncherViewModel.LaunchItem` — are launcher gestures
nobody can mean while a dialog is up. The duplicated test in `Deactivated` was deleted with the fix.

**The hardening, behind it:** `OpenCoreAsync` activates the existing dialog only if it **`IsVisible`**;
a non-visible one is **logged as a warning** and released through a new `OptionsWindow.ReleaseCurrent`,
so the field can never own the session again. The review's other suggestion — clear `Current` in
`OnClosing` too — is **rejected on the code**: `OnClosing` is cancellable and *is* cancelled on two
paths (the discard prompt, a commit in flight), so clearing there would null the field while the dialog
is still on screen and let a second modal stack on the same owner.

**Verified after the fix, live:** the reviewer's exact sequence leaves both windows up and the dialog
usable; 取消 then closes it and 设置 reopens; `Esc` with focus inside the dialog still closes it; `Esc`
in the *launcher* still hides the launcher. `config.json` byte-identical throughout.

**2 · MEDIUM — two focus visuals. FIXED, measured by the review's own A/B method.**

One `Style Selector="Button"` in `Themes/Shell/Launcher.axaml` replaces the three `FocusAdorner`
setters that lived inside `TmTileButton` / `TmIconButton` / `TmButton`. A `Style` Setter outranks a
`ControlTheme` Setter, so keeping the three would have left three declarations deciding nothing.
A/B `PrintWindow` (flag 2, 150 %), a synthetic `VK_TAB` between the captures, diffed over each
control's UIA rect + 6 px:

| Surface | Before (review) | After |
|---|---|---|
| Options 取消 (vendored `{x:Type Button}`) | **1245 px**, a solid ~2 DIP black square frame *replacing* the Aero border | **424 px**, bbox **107 × 29** — the dotted rectangle, inside the intact Aero border |
| About box 确定 | same solid frame | **424 px**, bbox **107 × 29** |
| `MessageBoxWindow` 是 (built in code) | same | **424 px**, bbox **107 × 29** |
| `PickRootBtn` (`TmButton`) | 424 / 5922 dotted | **424 px**, bbox 107 × 29 — unchanged to the pixel |
| a launcher tile (`TmTileButton`) | 602 dotted | **602 px** — unchanged |

Same pass: `MessageBoxWindow.axaml.cs`'s run-time buttons go from `MinWidth = 84` with no height floor
to **`MinWidth = 75`, `MinHeight = 23`** — PUBLISHED, `vis-layout` *"Command buttons — 75x23"*. MEASURED
on the live discard prompt: **113 × 35 px = 75.33 × 23.33 DIP**, the same figure the Options commit row
reads. Not widened to a fixed `Width`, because these captions are built at run time in two languages.

**3 · Minor — `StrokeDividerBrush` deleted**, Light and Dark, by the same test T7 applied to
`Space1/2/3` and did not apply here: zero `{DynamicResource}` consumers in `src/` or `tests/` since T3
deleted `Border.tm-header` and `Border.tm-nav-separator`. `Tokens.axaml`'s census goes **32 → 31 keys**
(20 themed, 11 variant-independent) and its alias block records what the key was for.

**4 · Minor — the menu item is 主目录 / "Main Folder"**, not 打开主目录 / "Open Main Folder". §3.3 above
carries the change; §10.2's band-width row carries the two measurements it moved.

**5 · The record corrected** — §10.5's first bullet, above.

**Tests: 222 → 226.** `WindowHostHideTests` (3) drives `WindowHost.Hide` with no window attached and
reads the guard off whether the placement flush wrote `config.json` — the control case, the suppressed
case, and the nested balance the dialog's own message boxes produce. `OptionsWindowTests` gains one
source test pinning the launcher's `IsVisible` branch and the *absence* of a `Current` clear in
`OnClosing`.
