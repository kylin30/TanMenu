# TanMenu — Fluent 2 redesign (replaces the Windows 2000 theme)

> Design spec. Every claim about Avalonia below was measured with a throwaway probe against
> Avalonia 12.1 on 2026-07-30, not recalled — the measurements are quoted where they change a
> decision. Where this file and the code disagree, the code wins and this file is the bug.

## Goal

Delete the Windows 2000 retro theme and rebuild TanMenu's default look in the Fluent 2 design
language. The scope is **visual language plus layout form**, not a re-skin: the launcher keeps its
purpose (see every shortcut at a glance and launch one) but is rebuilt in Fluent 2's vocabulary —
search-first, section headings instead of 3D group boxes, flowing tiles, an overflow menu instead of
a text menu bar, acrylic and rounded corners, and light/dark that follows the OS.

## What Avalonia 12.1 actually gives us

Three measurements decided the architecture.

**1. `FluentTheme` is Fluent *1* (Windows 10 / UWP), not Fluent 2.** It exposes 1054 string-keyed
resources that do flip correctly between `ThemeVariant.Light` and `Dark`, but the vocabulary is the
UWP palette — `SystemBaseHighColor`, `SystemAltHighColor`, `SystemChromeMediumColor`, and per-control
brushes derived from them (`ButtonBackground` = `#33000000` light / `#33FFFFFF` dark). None of the
Fluent 2 semantic tokens exist: `TextFillColorPrimaryBrush`, `CardBackgroundFillColorDefaultBrush`,
`SubtleFillColorSecondaryBrush`, `AccentFillColorDefaultBrush`, `SolidBackgroundFillColorBaseBrush`
and `SurfaceStrokeColorFlyoutBrush` all resolve to MISSING.

**2. Overriding the base colours does NOT propagate.** Layering a dictionary that redefines
`SystemBaseHighColor` on top of `FluentTheme` leaves every derived brush unchanged — they are baked
with `{StaticResource}` at parse time. Measured: after overriding `SystemBaseHighColor` to red,
`ButtonForeground` was still `#FF000000`.

*This kills the tempting shortcut of "retint all 1054 resources from a handful of base colours."
Do not try it again.*

**3. Scalar theme keys DO propagate.** The same experiment showed `ControlCornerRadius` (3 → 4) and
`ContentControlThemeFontFamily` (Avalonia's bundled Inter → `Segoe UI Variable Text`) taking effect,
because control templates consume those with `{DynamicResource}`.

So: **metrics and typography can be corrected globally with a few keys; colour cannot.** That splits
the work cleanly and is the reason for the file layout below.

Also measured: `SystemAccentColor` is a hardcoded `#FF0078D7` in the theme, not the user's
personalization accent (this machine's is `#0078D4`). The accent therefore has to come from
`PlatformSettings` at run time.

## Architecture

| Action | File |
|---|---|
| Delete | `Themes/Retro.axaml` (394 lines) |
| Delete | `wwwroot/fonts/*.ttf` (4 files, 22.7 MB) and their `AvaloniaResource` entry |
| Add | `Themes/Fluent2/Tokens.axaml` — the single source of design tokens |
| Add | `Themes/Fluent2/Launcher.axaml` — control themes and class selectors for the launcher only |
| Add | `Services/AppTheme.cs` — appearance setting → `ThemeVariant`, plus the runtime accent |
| Rewrite | `MainWindow.axaml` (+ code-behind) — new layout and keyboard model |
| Rewrite | `ViewModels/LauncherViewModel.cs` filtering — real filtering replaces dim/highlight |
| Simplify | `Services/AppFonts.cs` — system fonts only |
| Edit | `App.axaml`, `TanMenu.Wpf.csproj`, `Models/AppConfig.cs`, `Services/ConfigService.cs`, `SettingsWindow` |

`Tokens.axaml` is the app's vocabulary; `Launcher.axaml` is the dialect one window speaks. Keeping
them apart means a colour change touches only Tokens, a new launcher control touches only Launcher,
and a test can assert that Launcher contains no hardcoded colour literals.

## Design tokens

`Tokens.axaml` holds two `ThemeDictionaries` (Light, Dark). Values are the Fluent 2 neutral ramp,
expressed the way Fluent 2 expresses it — alpha over a neutral base rather than opaque greys, so the
acrylic backdrop shows through.

| Token | Light | Dark | Used for |
|---|---|---|---|
| `SurfaceBase` | `#F3F3F3` | `#202020` | opaque fallback when acrylic is unavailable |
| `SurfaceLayer` | `#B3FFFFFF` | `#4D3A3A3A` | the content layer over acrylic |
| `SubtleFillSecondary` | `#0A000000` | `#0FFFFFFF` | tile hover |
| `SubtleFillTertiary` | `#06000000` | `#0BFFFFFF` | tile pressed |
| `TextPrimary` | `#E4000000` | `#FFFFFFFF` | tile labels, search text |
| `TextSecondary` | `#9B000000` | `#C8FFFFFF` | section headings |
| `TextTertiary` | `#72000000` | `#8BFFFFFF` | hints, placeholder |
| `TextDisabled` | `#5C000000` | `#5CFFFFFF` | tiles whose target is missing |
| `StrokeControl` | `#0F000000` | `#12FFFFFF` | search box, tile focus base |
| `StrokeSurfaceFlyout` | `#33000000` | `#33000000` | the window's 1 px outline |
| `StrokeDivider` | `#0F000000` | `#15FFFFFF` | header rule |
| `FocusStrokeOuter` | `#E4000000` | `#FFFFFFFF` | keyboard focus ring |

Shape: `RadiusControl` 4, `RadiusOverlay` 8. Spacing scale: 4 / 8 / 12 / 16 / 20.
Type ramp: Caption 12, Body 14, BodyStrong 14 + SemiBold, Subtitle 20 + SemiBold.

Accent is **not** a static token, and it is not one colour either. Windows keeps an eight-shade
palette at `HKCU\…\Explorer\Accent\AccentPalette`, and a Windows 11 app selects from it by context
rather than tinting the base itself: an accent fill on a light surface uses `Darker1` so it holds
white text, on a dark surface `Lighter2` — and a light fill then needs *dark* text on it. Accent
TEXT steps one further (`Darker2` / `Lighter3`). `AppTheme.ResolveAccent` is a pure function over the
palette so that selection is unit-tested rather than eyeballed; `AppTheme.PublishAccent` wraps it in
a `ThemeDictionaries` dictionary pushed into `Application.Resources`, which is searched before
`Application.Styles` and therefore wins over Tokens.axaml's design-time defaults. With no readable
palette every shade falls back to `#0078D4` rather than being derived — a plausible-but-wrong tint is
worse than the documented default.

The same dictionary overrides the three Avalonia keys that measurement showed do propagate:
`ControlCornerRadius` → 4, `OverlayCornerRadius` → 8, `ContentControlThemeFontFamily` → the resolved
UI font. That is what pulls the settings window and the message box into Fluent 2 proportions
without touching a single control template.

The settings window otherwise stays on Avalonia's own control colours. That is deliberate and
unchanged from the previous design: a settings dialog is where users expect the platform's controls,
and re-tinting ~1054 baked brushes to gain a slightly different grey is not worth the surface area.

## Typography

Windows 11's type ramp, including its line heights, which are part of the spec: Caption 12/16,
Body 14/20, Body Strong 14/20 SemiBold, Subtitle 20/28 SemiBold.

Segoe UI Variable ships three **optical sizes** and Windows 11 selects between them by size band —
Small below 14, Text at 14, Display from 18 up — because a face drawn for body copy looks loose when
scaled to a heading. All three are separate addressable family names (verified installed:
`Segoe UI Variable Small` / `Text` / `Display`). Each falls back to Segoe UI for Windows 10 and
appends `Microsoft YaHei UI` so CJK never renders as tofu.

The optical size is applied **only while the user has not chosen a font**. A font setting that
silently stopped applying to headings would look like a bug, so the Subtitle face comes from
`LauncherViewModel.TitleFontFamily`, which returns the user's family when one is set and the Display
stack otherwise. Everything at 14 px simply inherits, where the default already *is* the Text size.

All four bundled TTFs are deleted: the app default becomes the system font, the font dropdown lists
only "默认（系统字体）" plus installed families, and `ConfigService.Normalize` maps the three retired
bundled names (`Alibaba PuHuiTi`, `Pixel`, `Press Start 2P`) to `""`.

Payload effect: 68.6 MB → roughly 46 MB.

## Windows 11 platform conformance

Beyond the visual language, the app follows the conventions a Windows 11 app is expected to observe.
Each was verified against the running build, not assumed:

| Convention | What the app does | How it was verified |
|---|---|---|
| **Materials** — Mica backs long-lived app windows, acrylic backs transient light-dismiss surfaces | Settings window: Mica. Launcher (hides on blur): acrylic. Both fall back to opaque `SurfaceBase` when the platform declines | Sampling the settings window's blank area across positions gives *different* values (wallpaper tint bleeding through); a flat fallback would give one |
| **Rounded corners** — Windows 11 rounds windows, but not borderless ones | `DWMWA_WINDOW_CORNER_PREFERENCE` = `DWMWCP_ROUND` on the launcher's HWND | `DwmGetWindowAttribute` returns 2, and the pixel at the window's corner is desktop wallpaper rather than window chrome |
| **Iconography** — Segoe Fluent Icons | Search `E721`, More `E712`, Close `E8BB`, Folder `E8B7`, Download `E896`, with Segoe MDL2 Assets as the Windows 10 fallback at the same code points | Both fonts confirmed installed; glyphs render as the shell's |
| **Accent** — select from the system palette | See the token section above | `AppThemeTests` |
| **Focus visuals** — a two-ring focus rectangle | 2 px outer ring in the text colour, 1 px inner ring in the opposite colour, drawn as a focus *adorner* so showing it cannot reflow the grid | — |
| **Type ramp** — optical sizes and line heights | See the typography section above | Family names confirmed addressable |

## Layout

The popup is a rounded 8 px acrylic surface with a 1 px `StrokeSurfaceFlyout` outline and no title
bar — Fluent's flyout idiom rather than a window. Minimize and maximize are gone; both were
duplicates of "hide" and a fixed-size popup can do neither.

```
┌ acrylic, radius 8, 1px outline ───────────────────────────┐
│  🔍  搜索或输入名称…                            [⋯]  [✕]  │  header, 48px, divider below
├────────────────────────────────────────────────────────────┤
│  常用工具                                                   │  section heading, BodyStrong/TextSecondary
│  [▢ 计算器] [▢ 记事本] [▢ 任务管理器]                       │  tiles, WrapPanel
│                                                            │
│  工具  🗀                                                   │  folder glyph appears on hover, opens the folder
│  [▢ aida64] [▢ BCompare] [▢ FastCopy] …                    │
└────────────────────────────────────────────────────────────┘
```

- **Tile**: 168 px wide *(superseded by Round 4, item 3 —
  `docs/superpowers/specs/2026-08-02-win11-shell-round4-design.md` §3: the fixed width is deleted and a
  tile sizes to its content, uniform per column; the 20 px icon box and radius 4 below still hold)*,
  20 px icon + Body label, radius 4, transparent at rest,
  `SubtleFillSecondary` on hover, `SubtleFillTertiary` pressed with no content offset (Fluent 2 does
  not shift content the way the bevel did). A tile whose target is missing renders `TextDisabled`
  and does not react to the pointer.
- **Sections** stack vertically; tiles wrap inside each section. This replaces the old
  one-row-of-group-boxes grid, which was a Windows 2000 idiom with no Fluent 2 counterpart.
- **Size**: width is content-driven and capped at 1100 px; height is capped at 72 % of the work
  area, after which the content area scrolls with Fluent's overlay scrollbar.
  This retires `WindowHost`'s fit-to-work-area `ScaleTransform`: shrinking the whole UI to avoid a
  scrollbar was a workaround for a layout that could not scroll. Scrolling is the Fluent answer.
- **Overflow menu** (`⋯`) holds 打开主目录 / 刷新 / 设置 / 关于 / 退出, replacing the text menu bar.

## Interaction

- Focus starts in the search box on every reveal.
- Typing filters for real: non-matching tiles are removed and the grid reflows. The old behaviour
  (nothing hidden, matches ringed gold, others faded) existed to keep the popup from resizing; a
  search-first layout that does not actually filter is a contradiction.
- Empty result shows a short "未找到匹配项" state with a clear-filter action.
- `↓` or `Tab` from the search box enters the tile grid; arrows move within and across sections;
  `Enter` launches the focused tile; `Enter` in the search box launches the first match.
- `Esc` clears a non-empty filter, otherwise hides the launcher.

## Configuration

Two changes to `config.json`, both handled by `ConfigService.Normalize` so existing files keep
loading:

- `general.themeName`: canonical value becomes `"Fluent"`. Every historical id, including the
  outgoing `"Classic"`, is coerced to it on load.
- `general.appearance`: **new**, one of `"System"` (default) / `"Light"` / `"Dark"`. Absent in every
  existing file, which deserializes to the default.

The settings window's Appearance tab loses the single-entry theme dropdown — with one theme it never
did anything — and gains an appearance dropdown bound to the new value.

## Testing

- `JsonContractTests` gains cases for the new `appearance` field and the `"Fluent"` canonical theme,
  and keeps proving that a pre-migration `config.json` still loads.
- `ConfigServiceTests` covers coercion of the retired font names and of `"Classic"`.
- `MenuModelsTests` covers the new filtering (a non-match is absent, not dimmed) and the keyboard
  traversal order across sections.
- A style test asserts `Launcher.axaml` contains no `#RRGGBB` literal, so colour cannot leak out of
  `Tokens.axaml`.
- The UI-Automation smoke script is updated: the menu-bar item names disappear, so it asserts the
  overflow button, the search box, section headings and tiles instead, and it runs once per
  appearance value to prove both variants render.

## Risks

- **Acrylic may be unavailable** (remote desktop, transparency disabled, older GPU path). The window
  must check `ActualTransparencyLevel` and fall back to opaque `SurfaceBase`; a blank or black popup
  is the failure mode to avoid.
- **Removing the bundled fonts changes what existing users see.** Anyone who had selected the
  bundled Alibaba PuHuiTi silently moves to the system font. This is stated in the release notes.
- **Real filtering changes muscle memory** for anyone who relied on the gold-ring behaviour.
- **NativeAOT**: everything here is XAML and ordinary code; no new reflection surface. The publish
  must still come back with 0 ILC warnings.
