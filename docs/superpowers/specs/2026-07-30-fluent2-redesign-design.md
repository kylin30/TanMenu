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

Accent is **not** a static token. `AppTheme` reads
`TopLevel.PlatformSettings.GetColorValues().AccentColor1` at startup, falls back to `#0078D4`, and
publishes `AccentDefault` / `AccentText` into `Application.Resources` so both variants pick it up.

The same dictionary overrides the three Avalonia keys that measurement showed do propagate:
`ControlCornerRadius` → 4, `OverlayCornerRadius` → 8, `ContentControlThemeFontFamily` → the resolved
UI font. That is what pulls the settings window and the message box into Fluent 2 proportions
without touching a single control template.

The settings window otherwise stays on Avalonia's own control colours. That is deliberate and
unchanged from the previous design: a settings dialog is where users expect the platform's controls,
and re-tinting ~1054 baked brushes to gain a slightly different grey is not worth the surface area.

## Typography

Segoe UI Variable Text (Windows 11) → Segoe UI (Windows 10) → the Avalonia default, with
`Microsoft YaHei UI` appended so CJK never renders as tofu. Both fonts were confirmed present on the
development machine. All four bundled TTFs are deleted: the app default becomes the system font, the
font dropdown lists only "默认（系统字体）" plus installed families, and `ConfigService.Normalize`
maps the three retired bundled names (`Alibaba PuHuiTi`, `Pixel`, `Press Start 2P`) to `""`.

Payload effect: 68.6 MB → roughly 46 MB.

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

- **Tile**: 168 px wide, 20 px icon + Body label, radius 4, transparent at rest,
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
