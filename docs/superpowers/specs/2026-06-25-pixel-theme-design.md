# 复古像素风格主题（Pixel）设计

- 日期：2026-06-25
- 状态：已批准（待实现）
- 范围：为 TanMenu 启动器新增一个"像素"主题，与现有 Win98 / WinXP / Win7 / Windows11 并列。

## 1. 目标与背景

在启动器（BlazorWebView）的主题下拉里新增一个 **8-bit 复古像素风格** 主题。沿用与其它主题一致的"带标题栏的窗口外壳"，但整体像素化——硬边斜角、像素字体、无圆角、有限调色板。原生 WPF 设置窗口不在范围内（和现有主题一样不受主题影响）。

### 已有可复用资产
- 主题机制：`RetroWindow.razor` 为每个主题加载一套 CSS 框架（98.css / 7.css / fluent2.css），它们共享同一套 `.window` / `.title-bar` / `.window-body` 外壳 API；切主题即换链接的样式表。
- `wwwroot/lib/nes/nes.min.css`（NES.css 8-bit 框架）已打包但未作为主题接入——本方案不使用它（见"方案选择"）。
- 两款像素字体已 `@font-face` 打包并出现在字体选择器：
  - **Fusion Pixel**（CSS 名 `'Pixel'`，`fonts/fusion-pixel-12px-monospaced-zh_hans.ttf`）——12px 等宽，**支持简体中文**。
  - **Press Start 2P**（`fonts/PressStart2P-Regular.ttf`）——经典 8-bit，仅 ASCII。

### 关键约束
启动器内容为中文（搜索框占位、`常用工具`、文件夹/快捷方式名）。Press Start 2P 无法渲染中文，因此正文必须用 **Fusion Pixel**；Press Start 2P 仅用于纯英文（标题栏的 "TanMenu"）。

## 2. 决策

| 维度 | 决定 |
|------|------|
| 视觉方向 | 像素风窗口：保留标题栏窗口外壳，整体像素化（复用现有 `.window` 外壳契约） |
| 调色板 | 8-bit 彩色：见下方色板表 |
| 实现方式 | **方案 A**：从零手写 `pixel.css`，自给自足，不依赖也不加载 98.css |
| 正文字体 | Fusion Pixel（`'Pixel'`，中文安全）；标题栏纯英文用 Press Start 2P |

### 方案选择说明
- 方案 A（手写 pixel.css）被选中：完全可控、最纯粹的像素观感，自洽于"每主题一套样式表"的现有模型。
- 未选方案 C（98.css + 覆盖层）：复用更多但需小心覆盖以免露出 Win98 灰。
- 未选方案 B（NES.css）：NES.css 无标题栏概念（外壳仍需手写），且其调色板 / Press Start 2P（无中文）/ 尺寸都需覆盖，得不偿失。

### 色板
| 用途 | 色值 |
|------|------|
| 窗口面板底 | `#40407a` |
| 窗口外深框 | `#15152e` |
| 斜角高光 / 暗面 | `#5a5a99` / `#1b1b3a` |
| 标题栏 | `#34ace0`（纯色青） |
| 文字（深底上） | `#f7f1e3`（奶白） |
| 文字 / 图标（浅底上） | `#1b1b3a` |
| 按钮面 | `#4b4b8f`，高光 `#6c6cb0` / 暗面 `#23234a` |
| 强调（hover 描边等） | 黄 `#ffb142` / 青 `#34ace0` |
| 输入框底 | `#f7f1e3`（奶白），深字 |

## 3. 接入点

新增 1 个文件 + 4 处现有改动：

1. **新文件 `wwwroot/lib/pixel/pixel.css`** —— 整套主题样式（第 4 节）。
2. `Components/Retro/RetroWindow.razor`：
   - `Stylesheets` switch 增 `"Pixel" => new[] { "/lib/pixel/pixel.css" }`。
   - `WindowClass` 保持普通 `"window active"`（不加 Win7 的 `glass`）。
3. `Components/Index.razor`：
   - `ResolveTheme(name)` 增 `"Pixel" => "Pixel"`。
   - `ThemeClass` 增 `"Pixel" => "pixel"`（→ `.body.theme-pixel`）。
4. `SettingsWindow.xaml.cs`：`Themes` 数组加 `("Pixel", "像素")`。
5. `wwwroot/css/app.css`：仅按需加少量 `.theme-pixel` 作用域微调（如搜索框 `.tm-search-input`、菜单栏 `.tm-menuitem` 的对齐/留白），主体样式都在 `pixel.css`。

> 字体无需新增打包：Fusion Pixel 与 Press Start 2P 的 `@font-face` 已在 `app.css`（约第 1–15 行）。

## 4. pixel.css —— 外壳部件与视觉规格

从零实现应用所依赖的、与 98.css 同名的外壳 API（不依赖 98.css）。需覆盖的选择器与观感：

- `.window` / `.window.active`：靛蓝面板 `#40407a`，厚硬像素边（外框 `#15152e` + 2px 高光/暗面做厚 3D 角），**无圆角**。
- `.title-bar` / `.title-bar-text` / `.title-bar-icon`：纯青条 `#34ace0`、奶白字 `#f7f1e3`；标题文字用 Press Start 2P；标题图标 `image-rendering: pixelated`。
- `.title-bar-controls button`：小像素按钮，自绘 `_`（最小化）/ `✕`（关闭）像素字形（用 `::after` content 或像素背景图）。
- `.window-body` / `.window-body.has-space`：靛蓝底 + 内边距。
- `button` 与 `.button-body`（启动按钮，`RetroButton.razor` 渲染）：面 `#4b4b8f`，分层 `box-shadow` 硬斜角（0 模糊、无圆角），Fusion Pixel 字，`:hover` 描黄边 `#ffb142`，`:active` 内陷，`:disabled` 褪色。
  - 注意：`RetroButton.razor.css` 给 `.button-body` 声明了自有 `font-family`（"Pixelated MS Sans Serif"）。pixel.css 用 `!important` 把 `.button-body` 默认字体改为 Fusion Pixel（与 `Index.razor` 现有的窗口级字体覆盖同样用 `!important` 压过 `.button-body` 一致）。
- `input[type=text]`（搜索框 `.tm-search-input`）：下陷像素输入框，奶白底深字、硬黑内边框。`app.css` 把输入框的边框/圆角/内边距交给各主题定义，故此处必须给出像素化样式。
- `fieldset` / `legend`（`RetroGroupbox.razor` 用到的分组框）：像素边 + legend 标签。
- 滚动条（`#form-content` 超长菜单时 `overflow-y` 出现）：像素化样式（或保留默认，作为可接受降级）。
- `:disabled` 态：显式像素化（98.css 的 `:disabled` 规则因 Sass 残留失效，本主题不依赖它，必须自给——参见 `app.css` 中针对 Win98 的同类注释）。
- 全局：斜角一律用分层 `box-shadow`（无模糊、无圆角）；`image-rendering: pixelated`。

## 5. 字体与尺寸

- 默认字体 Fusion Pixel（中文安全）；标题栏纯英文用 Press Start 2P。
- 用户字体覆盖：设置里若选了非默认字体，仍按 `Index.razor` 现有的窗口级 `!important` 覆盖机制生效（与其它主题一致）。Pixel 的字体是"默认"（`FontFamily` 为空 = "默认（主题字体）" 时生效）。
- 像素字体在其设计字号（12px 的整数倍）最清晰。**要求**：Pixel 主题仅在 `.theme-pixel` 作用域内，把按钮字号对齐到 12px 的整数倍（例如小/中 = 12px、大 = 24px，配合图标尺寸区分大小），避免半像素糊；**不改其它主题的 13/14/16**（`app.css` 的 `.body.size-*` 变量）。具体像素值在实现计划里定。

## 6. 边界与错误处理

- 字体加载失败：`@font-face` 已带 `font-display: swap`，优雅降级到等宽再到应用默认；中文始终由 Fusion Pixel 兜底。
- 增量、向后兼容：新增 "Pixel" 不影响旧配置；未知/旧主题名仍回退 Win98（`ResolveTheme` 现有行为）。
- 窗口测量：`WindowHost.ContentVersion` 指纹已包含 `ThemeName`，切到/切出 Pixel 会触发重新测量与重新布局（现成机制，无需改动）。

## 7. 验证 / 验收

纯 CSS / 主题接线，无合适单测（`ResolveTheme`/`ThemeClass` 为 Blazor 组件私有成员，且渲染依赖 WebView）。既有 44 个 Core 单测应保持全绿（本改动不触及 Core）。

验收 = 构建 + 运行应用、在设置切到"像素"主题，肉眼核对：
- 标题栏（青条、奶白标题、最小化/关闭像素字形、像素图标）。
- 启动按钮（硬斜角、hover 黄描边、`:active` 内陷、`:disabled` 褪色）。
- 搜索框（下陷像素输入框，中文占位/输入正常）。
- 中文文件夹/快捷方式名清晰（Fusion Pixel）。
- 禁用项（失效快捷方式）呈禁用观感。
- 超长菜单出现滚动时观感可接受。
- 切主题后窗口尺寸/位置重新测量正确，无残留灰/圆角。

可用 run / verify 技能启动应用并截图核对。

## 8. 不在范围

- 原生 WPF 设置窗口的像素化（与现有主题一致，不主题化）。
- 把 NES.css / macOS9 / win9x 其它皮肤接成主题。
- 图标位图的像素化重绘（真实 PNG 应用图标保持原样）。
