## TanMenu 0.11.0

- 界面改用 Fluent 2 设计语言重做，并按 Windows 11 应用规范对齐：
  - 材质分工：设置窗用 Mica（长驻窗口），启动器用亚克力（失焦即隐的瞬时面板）；系统不支持时各自回退为不透明底色。
  - 窗口圆角交给 DWM（`DWMWA_WINDOW_CORNER_PREFERENCE`），无边框的启动器也有原生圆角与投影。
  - 图标改用系统的 Segoe Fluent Icons（Windows 10 回退 Segoe MDL2 Assets），与资源管理器等系统界面同款。
  - 字体走 Windows 11 类型阶梯，并按字号切换 Segoe UI Variable 的三种光学尺寸（正文用 Text、标题用 Display）；你若在设置里指定了字体，则一律以你的选择为准。
  - 强调色取自 Windows 个性化的完整调色板：浅色主题用较深一档保证白字对比度，深色主题用较浅一档并自动改用深色文字。
  - 键盘焦点框为 Win11 的内外双环。
- 新增浅色 / 深色，默认跟随系统；在「设置 → 外观」里可以固定为浅色或深色。原来只有一个条目的「主题」下拉已由它取代。
- 布局重排：搜索框前置为主入口，输入即真正过滤（不匹配的项会消失，空的分组连标题一起收起）；分组框改为分区标题 + 自动换行的磁贴；菜单栏文字按钮收进右上角的「更多」菜单。
- 键盘可达：↑↓←→ 在磁贴间移动，Enter 启动，Esc 先清空搜索、再隐藏窗口。
- 窗口不再整体缩放来迁就屏幕，改为限制最大尺寸后滚动。
- 不再内嵌字体，安装体积从 68.6 MB 降到约 46.9 MB。原先可选的「阿里巴巴普惠体 / Fusion Pixel / Press Start 2P」随之移除，配置里若仍是这三者会在载入时改回默认（系统字体）；选择的是已安装的系统字体则不受影响。
- 「每列按钮数」设置项移除：磁贴按可用宽度自动换行，该数值已不再影响任何东西。
- 配置、快捷方式缓存与图标缓存三个文件的磁盘格式保持不变，升级后无需重新扫描或重新提取图标。

---

- The interface was rebuilt in the Fluent 2 design language and aligned with the Windows 11 app conventions:
  - Materials by role: Mica for the settings window (a long-lived app window), acrylic for the launcher (a transient surface that hides on blur), each falling back to an opaque colour where the platform declines.
  - Window corners come from DWM (`DWMWA_WINDOW_CORNER_PREFERENCE`), so even the borderless launcher gets native rounding and shadow.
  - Icons are the system's Segoe Fluent Icons (Segoe MDL2 Assets on Windows 10) — the same glyphs File Explorer draws.
  - Type follows the Windows 11 ramp and switches Segoe UI Variable between its optical sizes by size band (Text for body, Display for headings). A font you pick in settings still wins everywhere.
  - The accent comes from your full Windows personalization palette: a darker shade on light backgrounds so white text holds contrast, a lighter one on dark backgrounds with dark text on it.
  - Keyboard focus draws Windows 11's two-ring rectangle.
- Light and dark are now supported and follow the OS by default; Settings → Appearance can pin either one. It replaces the old single-entry theme dropdown.
- The layout was reorganised: search leads the header and typing genuinely filters (non-matches are removed, and a section whose items all disappear takes its heading with it); group boxes became section headings over a wrapping tile grid; the text menu bar moved into a "More" menu at the top right.
- Keyboard reachable throughout: arrows move across the tile grid, Enter launches, Esc clears the search and then hides the launcher.
- The window no longer scales itself down to fit the screen — it caps its size and scrolls instead.
- No fonts are bundled any more, which takes the install from 68.6 MB to about 46.9 MB. The optional Alibaba PuHuiTi / Fusion Pixel / Press Start 2P entries are gone with them: a config still naming one of the three reverts to the default system font on load, while a chosen installed font is untouched.
- The "buttons per column" setting was removed — tiles wrap to the available width, so the number no longer changed anything.
- The on-disk format of config, the shortcut cache and the icon cache is unchanged, so upgrading does not force a re-scan or a re-extraction of icons.
