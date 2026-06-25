# 复古像素风格主题（Pixel）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 TanMenu 启动器新增一个 8-bit 复古像素主题「像素」，与 Win98/XP/7/11 并列。

**Architecture:** 方案 A——从零手写一份 `wwwroot/lib/pixel/pixel.css`，自给自足实现应用依赖的 `.window`/`.title-bar`/`.window-body`/`button`/`input`/`fieldset` 外壳 API（不依赖也不加载 98.css）。再在 4 个现有接线点注册主题：`RetroWindow.razor`（按主题加载样式表）、`Index.razor`（`ResolveTheme`/`ThemeClass`）、`SettingsWindow.xaml.cs`（主题下拉）、`app.css`（`.theme-pixel` 作用域微调）。

**Tech Stack:** .NET 10 WPF + `Microsoft.AspNetCore.Components.WebView.Wpf`（BlazorWebView）、纯 CSS、已打包的 Fusion Pixel(`'Pixel'`)/Press Start 2P 像素字体。

依据 spec：`docs/superpowers/specs/2026-06-25-pixel-theme-design.md`。

## Global Constraints

- 目标框架 `net10.0-windows10.0.19041.0`（x64）；**不新增任何依赖包**。
- 字体不需新增打包：`'Pixel'`(Fusion Pixel) 与 `Press Start 2P` 的 `@font-face` 已在 `wwwroot/css/app.css` 第 1–15 行。
- 正文必须用 `'Pixel'`（中文安全）；标题栏纯英文可用 `Press Start 2P`。Press Start 2P 渲染不了中文。
- **不改其它主题的尺寸变量**（`app.css` 的 `.body.size-*` 的 `--btn-font/--btn-pad/--btn-icon`）；像素尺寸调整只在 `.body.theme-pixel.size-*` 作用域。
- 增量、向后兼容：新增 `"Pixel"` 不得影响旧配置；未知主题名仍回退 `Win98`（`Index.razor` `ResolveTheme` 现有行为）。
- 色板（verbatim）：面板 `#40407a`、外框 `#15152e`、高光/暗面 `#5a5a99`/`#1b1b3a`、标题 `#34ace0`、奶白 `#f7f1e3`、墨 `#1b1b3a`、按钮面 `#4b4b8f`、强调黄 `#ffb142`。
- `wwwroot/lib/**` 是 BlazorWebView 自动服务的静态资源（现有 `98.css` 等同样在此），**无需改 csproj**。

## 验证方式说明（贯穿所有任务）

本特性是纯 CSS + 主题接线，无合适单测（`ResolveTheme`/`ThemeClass`/`Stylesheets`/`Themes` 均为私有成员，且渲染依赖 WebView）。每个任务的验证 = **构建通过 + 运行肉眼核对清单**：

- 构建：`dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug --nologo`，期望末尾 `0 个错误`（C# 编译只能抓接线错误，抓不到 CSS 外观）。
- 运行核对：用 **`/run` 或 `/verify` 技能**启动应用并截图（或开发者手动 `dotnet run --project src/TanMenu.Wpf/TanMenu.Wpf.csproj`，用托盘/全局热键唤起启动器 → `选项` → 主题选「像素」→ `应用`），对照该任务的"核对清单"。
- 既有 44 个 Core 单测须保持全绿（本特性不触及 Core）：`dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj --nologo`。
- CSS 的具体像素值（边厚、字号、间距）允许在核对时按 spec 观感微调——规则本身是完整可用的，微调不算占位。

---

## File Structure

- **Create** `src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css` — 整套像素主题样式（Task 1 建骨架，Task 2–4 逐段补全）。
- **Modify** `src/TanMenu.Wpf/Components/Retro/RetroWindow.razor` — `Stylesheets` 加 Pixel 分支（Task 1）。
- **Modify** `src/TanMenu.Wpf/Components/Index.razor` — `ResolveTheme` + `ThemeClass` 加 Pixel（Task 1）。
- **Modify** `src/TanMenu.Wpf/SettingsWindow.xaml.cs` — `Themes` 数组加 `("Pixel","像素")`（Task 1）。
- **Modify** `src/TanMenu.Wpf/wwwroot/css/app.css` — `.theme-pixel` 搜索框聚焦环 + 像素尺寸变量（Task 4）。

---

### Task 1: 注册「像素」主题 + 最小可见骨架

让主题在设置里可选、选中后启动器以靛蓝底 + 像素字体渲染（外壳细节留待 Task 2–4）。这是把 4 个接线点一次锁定的整合任务。

**Files:**
- Create: `src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css`
- Modify: `src/TanMenu.Wpf/Components/Retro/RetroWindow.razor:34-40`（`Stylesheets`）
- Modify: `src/TanMenu.Wpf/Components/Index.razor:340-354`（`ResolveTheme`、`ThemeClass`）
- Modify: `src/TanMenu.Wpf/SettingsWindow.xaml.cs:18-24`（`Themes`）

**Interfaces:**
- Produces: 主题键字符串 `"Pixel"`（config `General.ThemeName` 取值之一）；CSS 根类 `.body.theme-pixel`；样式表路径 `/lib/pixel/pixel.css`；CSS 变量 `--px-bg/--px-panel/--px-frame/--px-hi/--px-lo/--px-title/--px-cream/--px-ink/--px-accent/--px-btn`（Task 2–4 复用）。

- [ ] **Step 1: 新建最小 `pixel.css`（色板变量 + 像素字体 + 面板底）**

`src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css`：
```css
/* TanMenu 像素主题 (8-bit). 从零实现 .window 外壳 API，不依赖 98.css。
   正文 Fusion Pixel('Pixel') 中文安全；标题栏纯英文用 'Press Start 2P'。 */
.body.theme-pixel {
    --px-bg: #2c2c54;
    --px-panel: #40407a;
    --px-frame: #15152e;
    --px-hi: #5a5a99;
    --px-lo: #1b1b3a;
    --px-title: #34ace0;
    --px-cream: #f7f1e3;
    --px-ink: #1b1b3a;
    --px-accent: #ffb142;
    --px-btn: #4b4b8f;
}

/* 默认像素字体 + 像素渲染 + 去圆角（全窗口）。.button-body 自带 font-family，
   故用 !important 压过它（与 Index.razor 现有窗口级字体覆盖一致）。 */
.theme-pixel .window,
.theme-pixel .window * {
    font-family: 'Pixel', 'Press Start 2P', monospace;
    image-rendering: pixelated;
    border-radius: 0 !important;
}
.theme-pixel .button-body { font-family: 'Pixel', monospace !important; }

.theme-pixel .window-body {
    background: var(--px-panel);
    color: var(--px-cream);
}
```

- [ ] **Step 2: `RetroWindow.razor` 注册样式表**

把 `Stylesheets`（当前 34–40 行）改为含 Pixel 分支：
```razor
    private string[] Stylesheets => Theme switch
    {
        "Windows11" => new[] { "/lib/fluent2/fluent2.css" },
        "Win7" => new[] { "/lib/win7theme/7.css" },
        "WinXP" => new[] { "/lib/xpcss/98.css", "/lib/xpcss/fonts.css", "/lib/xpcss/xp-luna.css" },
        "Pixel" => new[] { "/lib/pixel/pixel.css" },
        _ => new[] { "/lib/xpcss/98.css", "/lib/xpcss/fonts.css" },
    };
```
（`WindowClass` 不动：Pixel 用默认 `"window active"`，不要 Win7 的 `glass`。）

- [ ] **Step 3: `Index.razor` 加 `ResolveTheme` + `ThemeClass` 分支**

`ResolveTheme`（340–346 行）加一行：
```csharp
    private static string ResolveTheme(string? name) => name switch
    {
        "WinXP" => "WinXP",
        "Win7" => "Win7",
        "Windows11" => "Windows11",
        "Pixel" => "Pixel",
        _ => "Win98",
    };
```
`ThemeClass`（348–354 行）加一行：
```csharp
    private string ThemeClass => _theme switch
    {
        "WinXP" => "winxp",
        "Win7" => "win7",
        "Windows11" => "win11",
        "Pixel" => "pixel",
        _ => "win98",
    };
```

- [ ] **Step 4: `SettingsWindow.xaml.cs` 主题下拉加一项**

`Themes`（18–24 行）加 `("Pixel", "像素")`：
```csharp
    private static readonly (string Key, string Label)[] Themes =
    {
        ("Win98", "Windows 98"),
        ("WinXP", "Windows XP"),
        ("Win7", "Windows 7"),
        ("Windows11", "Windows 11"),
        ("Pixel", "像素"),
    };
```

- [ ] **Step 5: 构建**

Run: `dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug --nologo`
Expected: `0 个错误`（仅既有无关警告）。

- [ ] **Step 6: 运行核对（用 /run 或 /verify 技能截图）**

唤起启动器 → `选项` → 主题选「像素」→ `应用`。核对清单：
- [ ] 主题下拉里出现「像素」并可选中。
- [ ] 启动器内容区底色变靛蓝 `#40407a`，文字奶白。
- [ ] 中文（`常用工具`、文件夹名、搜索框占位）以像素字体渲染、清晰不糊、无方块缺字。
- [ ] 切回「Windows 98」一切照旧（回归检查）。

- [ ] **Step 7: 提交**

```bash
git add src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css \
        src/TanMenu.Wpf/Components/Retro/RetroWindow.razor \
        src/TanMenu.Wpf/Components/Index.razor \
        src/TanMenu.Wpf/SettingsWindow.xaml.cs
git commit -m "feat(theme): 注册像素主题 + 最小骨架"
```

---

### Task 2: 窗口外框 + 标题栏

把 `.window` 外框和 `.title-bar`（含最小化/关闭按钮、图标）做成 8-bit 像素观感。

**Files:**
- Modify: `src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css`（追加）

**Interfaces:**
- Consumes: Task 1 的 `.body.theme-pixel` 色板变量。
- Produces: 像素化的 `.window`/`.title-bar`/`.title-bar-controls button`/`.window-body.has-space` 外观。

- [ ] **Step 1: 追加窗口外框 + 标题栏样式**

在 `pixel.css` 末尾追加：
```css
/* ---- 窗口外框 ---- */
.theme-pixel .window {
    background: var(--px-frame);
    color: var(--px-cream);
    border: 3px solid var(--px-frame);
    box-shadow: inset 2px 2px 0 var(--px-hi),
                inset -2px -2px 0 var(--px-lo);
    padding: 0;
}

/* ---- 标题栏 ---- */
.theme-pixel .title-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    background: var(--px-title);
    color: var(--px-ink);
    padding: 3px 4px;
    box-shadow: inset 0 -2px 0 var(--px-lo);
}
.theme-pixel .title-bar-text {
    display: flex;
    align-items: center;
    gap: 6px;
    font-family: 'Press Start 2P', 'Pixel', monospace;
    font-size: 10px;
    color: var(--px-ink);
}
.theme-pixel .title-bar-icon { width: 16px; height: 16px; image-rendering: pixelated; }

.theme-pixel .title-bar-controls { display: flex; gap: 4px; }
.theme-pixel .title-bar-controls button {
    width: 18px; height: 16px;
    padding: 0;
    background: var(--px-btn);
    color: var(--px-cream);
    border: none;
    box-shadow: inset 2px 2px 0 var(--px-hi), inset -2px -2px 0 var(--px-lo);
    font-family: 'Press Start 2P', monospace;
    font-size: 8px;
    line-height: 1;
    cursor: pointer;
}
.theme-pixel .title-bar-controls button:active {
    box-shadow: inset -2px -2px 0 var(--px-hi), inset 2px 2px 0 var(--px-lo);
}
/* 无内容的控制按钮自绘像素字形（RetroWindow 用 aria-label 区分） */
.theme-pixel .title-bar-controls button[aria-label="Minimize"]::after { content: "_"; position: relative; top: -3px; }
.theme-pixel .title-bar-controls button[aria-label="Close"]::after { content: "X"; }

.theme-pixel .window-body.has-space { padding: 8px; }
```

- [ ] **Step 2: 构建**

Run: `dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug --nologo`
Expected: `0 个错误`。

- [ ] **Step 3: 运行核对（/run 或 /verify 截图，主题=像素）**

- [ ] `.window` 外框为厚硬像素边（深框 + 高光/暗面 3D 角），**无圆角**。
- [ ] 标题栏为纯青条 `#34ace0`，标题「TanMenu」奶白/墨色、Press Start 2P。
- [ ] 标题图标 16px、像素清晰（不被抗锯齿模糊）。
- [ ] 最小化「_」、关闭「X」为像素小按钮，按下有内陷反馈。

- [ ] **Step 4: 提交**

```bash
git add src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css
git commit -m "feat(theme): 像素主题窗口外框与标题栏"
```

---

### Task 3: 启动按钮（含 hover/active/disabled）

`RetroButton` 渲染 `<button><div class="button-body">…</div></button>`；空状态也有 `<button>`。统一在 `.window-body button` 作用域内做像素按钮。

**Files:**
- Modify: `src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css`（追加）

**Interfaces:**
- Consumes: Task 1 色板变量。
- Produces: 像素化的 `.window-body button` 及 `:hover/:active/:disabled` 态、`.button-body`/`.button-icon` 外观。

- [ ] **Step 1: 追加按钮样式**

在 `pixel.css` 末尾追加（注意：box-shadow 不跨规则叠加，故每个态都整段重声明）：
```css
/* ---- 启动按钮 ---- */
.theme-pixel .window-body button {
    background: var(--px-btn);
    color: var(--px-cream);
    border: none;
    padding: 0;
    cursor: pointer;
    box-shadow: inset 2px 2px 0 var(--px-hi), inset -2px -2px 0 var(--px-lo);
}
.theme-pixel .window-body button:hover:not(:disabled) {
    box-shadow: inset 0 0 0 2px var(--px-accent),
                inset 2px 2px 0 var(--px-hi),
                inset -2px -2px 0 var(--px-lo);
}
.theme-pixel .window-body button:active:not(:disabled) {
    box-shadow: inset -2px -2px 0 var(--px-hi), inset 2px 2px 0 var(--px-lo);
}
.theme-pixel .window-body button:disabled,
.theme-pixel .window-body button.disabled {
    color: #8a8ab0;
    opacity: 0.6;
    cursor: default;
    box-shadow: inset 1px 1px 0 var(--px-hi), inset -1px -1px 0 var(--px-lo);
}
.theme-pixel .button-body { color: inherit; }
.theme-pixel .button-icon img { image-rendering: pixelated; }
```

- [ ] **Step 2: 构建**

Run: `dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug --nologo`
Expected: `0 个错误`。

- [ ] **Step 3: 运行核对（/run 或 /verify 截图，主题=像素）**

- [ ] 启动按钮为像素斜角块（高光在左上、暗面在右下），无圆角。
- [ ] 鼠标悬停出现黄 `#ffb142` 描边；按下整体内陷（高光/暗面翻转）。
- [ ] 失效快捷方式（`Disabled`）按钮呈褪色禁用观感、不响应 hover。
- [ ] 按钮上的中文名清晰（Fusion Pixel），图标像素清晰。

- [ ] **Step 4: 提交**

```bash
git add src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css
git commit -m "feat(theme): 像素主题启动按钮与状态"
```

---

### Task 4: 搜索框 + 分组框 + 菜单栏 + 滚动条 + 尺寸/聚焦微调

补齐剩余外壳，并在 `app.css` 加 `.theme-pixel` 作用域的聚焦环与像素尺寸变量。

**Files:**
- Modify: `src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css`（追加）
- Modify: `src/TanMenu.Wpf/wwwroot/css/app.css`（追加 `.theme-pixel` 规则）

**Interfaces:**
- Consumes: Task 1 色板变量。
- Produces: 像素化的 `.tm-search-input`/`fieldset`/`legend.groupbox-title`/`.tm-menuitem`/滚动条；`.theme-pixel` 的搜索聚焦环与 `--btn-font/--btn-icon` 尺寸覆盖。

- [ ] **Step 1: `pixel.css` 追加搜索框/分组框/菜单栏/滚动条**

`.tm-menubar` 的布局（含搜索框 `margin-left:auto` 靠右）由 `app.css` 负责，这里只给外观、不重排布局：
```css
/* ---- 搜索框（app.css 把边框交给主题定义） ---- */
.theme-pixel .tm-search-input {
    background: var(--px-cream);
    color: var(--px-ink);
    border: none;
    padding: 3px 5px;
    font-family: 'Pixel', monospace;
    box-shadow: inset 2px 2px 0 var(--px-lo), inset -2px -2px 0 #ffffff;
}
.theme-pixel .tm-search-input::placeholder { color: #6a6a8a; }

/* ---- 分组框 fieldset/legend ---- */
.theme-pixel .window-body fieldset {
    border: 2px solid var(--px-frame);
    background: transparent;
    margin: 0;
    padding: 6px;
}
.theme-pixel .window-body legend.groupbox-title {
    background: var(--px-title);
    color: var(--px-ink);
    padding: 1px 6px;
    font-family: 'Pixel', monospace;
    box-shadow: inset 1px 1px 0 var(--px-hi), inset -1px -1px 0 var(--px-lo);
}
.theme-pixel .window-body legend.groupbox-title.clickable { cursor: pointer; }

/* ---- 菜单栏项 ---- */
.theme-pixel .tm-menuitem {
    color: var(--px-cream);
    padding: 2px 6px;
    cursor: pointer;
    font-family: 'Pixel', monospace;
}
.theme-pixel .tm-menuitem:hover { background: var(--px-title); color: var(--px-ink); }

/* ---- 滚动条（#form-content 超长时 overflow-y） ---- */
.theme-pixel #form-content::-webkit-scrollbar { width: 14px; height: 14px; }
.theme-pixel #form-content::-webkit-scrollbar-track { background: var(--px-lo); }
.theme-pixel #form-content::-webkit-scrollbar-thumb {
    background: var(--px-btn);
    box-shadow: inset 2px 2px 0 var(--px-hi), inset -2px -2px 0 var(--px-lo);
}
```

- [ ] **Step 2: `app.css` 追加 `.theme-pixel` 聚焦环 + 像素尺寸变量**

在 `app.css` 末尾追加（`.body.theme-pixel.size-*` 三类选择器特异性高于现有 `.body.size-*`，故覆盖生效；**不动其它主题**）：
```css
/* 像素主题：搜索框聚焦时叠加黄色像素环（保留下陷边） */
.theme-pixel .tm-search-input:focus {
    outline: none;
    box-shadow: inset 2px 2px 0 #1b1b3a, inset -2px -2px 0 #ffffff, 0 0 0 2px #ffb142;
}
/* 像素字体在 12px 整数倍最清晰：把按钮字号对齐到 12/24，仅限像素主题 */
.body.theme-pixel.size-small  { --btn-font: 12px; --btn-icon: 16px; }
.body.theme-pixel.size-medium { --btn-font: 12px; --btn-icon: 24px; }
.body.theme-pixel.size-large  { --btn-font: 24px; --btn-icon: 32px; }
```

- [ ] **Step 3: 构建 + Core 测试**

Run: `dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug --nologo` → `0 个错误`
Run: `dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj --nologo` → `通过: 44`

- [ ] **Step 4: 运行核对（/run 或 /verify 截图，主题=像素，逐项）**

- [ ] 搜索框为下陷像素输入框（奶白底、深字），聚焦时叠加黄色像素环；中文输入/占位正常。
- [ ] 分组（`常用工具`/文件夹）为像素 fieldset + 青色 legend 标签；可点的分组标题有手型指针。
- [ ] 顶部菜单项（`打开主目录`/`选项`/`刷新`/`关于`/`退出`）为像素文字，hover 高亮。
- [ ] 小/中/大三档按钮字号清晰不糊（12/12/24），图标 16/24/32 对应缩放。
- [ ] 快捷方式很多时 `#form-content` 出现像素滚动条，滚动正常、窗口不溢出。
- [ ] 切到「像素」再切回其它主题，窗口尺寸/位置重新测量正确，无残留靛蓝/像素样式。

- [ ] **Step 5: 提交**

```bash
git add src/TanMenu.Wpf/wwwroot/lib/pixel/pixel.css src/TanMenu.Wpf/wwwroot/css/app.css
git commit -m "feat(theme): 像素主题搜索框/分组/菜单栏/滚动条与尺寸微调"
```

---

## Self-Review（写计划者自查，已完成）

**1. Spec 覆盖：**
- §3 接入点（RetroWindow/Index.ResolveTheme+ThemeClass/SettingsWindow.Themes/app.css）→ Task 1 + Task 4。✓
- §4 外壳部件：`.window`/标题栏 → Task 2；`button`/`.button-body`/状态 → Task 3；`input`/`fieldset`/`legend`/菜单栏/滚动条/`:disabled` → Task 3+4。✓
- §5 字体（Fusion Pixel 正文 + Press Start 2P 标题 + `.button-body` `!important`）→ Task 1（正文）+ Task 2（标题）；尺寸对齐 → Task 4。✓
- §6 边界（字体降级 `font-display:swap` 现成；增量回退 Win98 → Task 1 Step 3/6；ContentVersion 现成）→ 覆盖（含回归核对项）。✓
- §7 验证（构建 + 运行肉眼 + 44 测试绿）→ 每任务 Step 验证 + Task 4 跑测试。✓

**2. 占位扫描：** 无 TBD/“稍后”/“适当处理”；每个 CSS/代码步骤都给了完整内容。CSS 像素值可在核对时按观感微调（spec 已声明视觉验收），非占位。✓

**3. 类型/命名一致：** 主题键 `"Pixel"`、类 `.theme-pixel`、路径 `/lib/pixel/pixel.css`、变量 `--px-*` 在 Task 1 定义，Task 2–4 一致复用。`RetroWindow` 用 `aria-label="Minimize"/"Close"`（与组件源一致）、`RetroButton` 用 `.button-body`/`.button-icon`、`RetroGroupbox` 用 `legend.groupbox-title.clickable`、`Index` 用 `.tm-menubar/.tm-menuitem/.tm-search-input/#form-content`——均与已读源码一致。✓
