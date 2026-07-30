# TanMenu — Avalonia 12 + NativeAOT migration record

> Written after the migration, from the code as it stands. Every number here was measured, not
> estimated. Where this file and the code disagree, the code wins — and this file is the bug.
>
> **Partly superseded (0.11.0).** This records the 0.10.0 move off WPF + BlazorWebView, and the
> engine half of it — NativeAOT, source-generated COM, the JSON contracts, the build environment —
> is still current. The **"One theme" section below is history**: the Windows 2000 theme it
> describes was itself replaced by the Fluent 2 design language in 0.11.0, which also deleted the
> bundled fonts and added light/dark. See
> [`docs/superpowers/specs/2026-07-30-fluent2-redesign-design.md`](../superpowers/specs/2026-07-30-fluent2-redesign-design.md).

## Why

The goal was startup and runtime speed. WPF cannot deliver it: it has no NativeAOT support (the
`dotnet/wpf` roadmap lists it as a long-term aspiration only), and neither can a Blazor UI, which
depends on runtime reflection. Avalonia does support NativeAOT, so the whole UI stack moved.

The dominant win is not the AOT compiler, though — it is deleting WebView2. The launcher is a
tray-resident popup that is summoned and dismissed all day; the old build paid for a Chromium
process tree on every recall, which `WindowHost` itself documented as "a paged-out WebView2
round-trip … after the app has sat idle for a long time".

## Measured outcome

| | Before (0.9.6) | After |
|---|---|---|
| Shipped payload | 564 files / 248 MB (portable zip 108 MB) | **11 files / 68.6 MB** |
| Processes at rest | app + WebView2 host + Chromium children | **1** |
| Runtime prerequisite | WebView2 Evergreen runtime | **none** |
| Trim/AOT warnings | 17 (all `System.Text.Json`) | **0** |
| ILC publish | n/a | **0 warnings**, ~55 s |
| Unit tests | 55 | **98** |

The 50 MB executable is dominated by ~22 MB of embedded CJK fonts (AlibabaPuHuiTi Regular + Bold,
Fusion Pixel); the code itself is ~28 MB. The three remaining DLLs are Avalonia's native renderer
(`libSkiaSharp`, `libHarfBuzzSharp`, `av_libglesv2`) — Avalonia loads them by filename, so any
packaging change must keep them beside the exe or the app renders nothing.

## Decisions worth keeping

**The assembly is still named `TanMenu.Wpf` although no WPF remains.** The exe filename is shipped
identity: Velopack updates from 0.9.x, the MSIX `Executable` entry, the per-user Run-key autostart
value (`RegistryAutoStartService` compares the stored path against `Environment.ProcessPath`), and
the pinned taskbar shortcut all reference `TanMenu.Wpf.exe`. Renaming it silently turns autostart off
and orphans the taskbar pin for every existing user. The csproj carries this reasoning at the top.

**One theme.** The CSS theme packs (WinXP / Win7 / Windows11 / Pixel, plus user-authored
`user:<name>` stylesheets) went away with the WebView. `Themes/Retro.axaml` replaces them with a
natively drawn Windows 2000 look: every raised surface is a real two-pixel double bevel, and pressing
a button inverts the bevel and shifts its content one pixel down-right. `AppConfig.CanonicalTheme`
("Classic") is the only legal value; `ConfigService.Normalize` coerces every historical id to it on
load. Deliberately not reusing `"Win7"` — that would name a theme the app no longer renders.

**User CSS themes are gone, and their files are left alone.** `ThemeService` used to seed
`<dataRoot>\themes` with templates and bilingual guides. The feature is removed, but nothing deletes
what it already wrote into users' data folders.

## NativeAOT constraints this codebase now lives under

1. **Built-in COM is unusable** ([IL3052]) — it generates its interop stubs at run time. All shell COM
   goes through `TanMenu.Core.Interop.ShellLink`, a source-generated (`[GeneratedComInterface]` +
   `ComWrappers`) wrapper. `BuiltInComInteropSupport` is `false` in the csproj; turning it back on
   would not help, it would just hide the problem until run time.
   The failure mode if this regresses is silent: `ShortcutResolver` swallows the exception and every
   `.lnk` resolves to `null`, i.e. an empty launcher with no error anywhere.
2. **String buffers.** The COM source generator rejects `StringBuilder`, so `IShellLinkW`'s
   string-out parameters are raw `char*`. Unlike `StringBuilder`, a raw buffer is not implicitly
   trimmed at the NUL — `ShellLink.GetPath` clears and trims it explicitly.
3. **All JSON is source-generated.** Three files, two option sets — `config.json` has its own;
   both caches use the serializer defaults — see below.
4. **`InvariantGlobalization` must stay off.** `AppLanguage` resolves `Language="Auto"` from
   `CultureInfo.CurrentUICulture.Name`, and group/font lists sort with
   `StringComparer.CurrentCultureIgnoreCase`. Invariant mode would silently give every Chinese user
   an English UI. It buys almost nothing on Windows anyway (ICU comes from the OS).
5. **Shell COM stays on one long-lived STA thread.** `ComWrappers` RCWs do not get built-in COM's
   automatic apartment marshalling, so `MenuService`'s STA worker is now a correctness requirement
   rather than an optimization.

## On-disk formats are contracts

Three files already exist on users' machines and are read back by exact property name:

| File | Options | Pinned by |
|---|---|---|
| `config.json` | camelCase, indented, skip-null, non-ASCII escaped as `\uXXXX` | `JsonContractTests` |
| `linkCache.json` | serializer **defaults** (PascalCase, compact), local `LastWriteTime` | `JsonContractTests` |
| `iconCache.json` | serializer **defaults**; keys literally `Mtime` / `Size` / `Png` | `JsonContractTests` |

Changing any of them does not fail a build or a test-free run — it silently discards the user's
settings or forces a full cold re-extraction of every icon. The tests read verbatim pre-migration
files, so a drift shows up as a test failure rather than a support ticket.

## Build environment

The ILC native link step shells out to `link.exe` and locates it with `vswhere.exe`. That requires
Visual Studio's C++ toolchain **and** `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` on
`PATH`; without it the managed compile succeeds and only the final link fails, with a confusing
`MSB3073 … 'vswhere.exe' is not recognized`. Publishing also takes minutes rather than seconds, so
neither packaging script passes a "no restore" switch — a clean runner has to fetch the ILC packs.
