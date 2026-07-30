## TanMenu 0.11.0

- 界面改用 Fluent 2 设计语言重做：亚克力背板、8px 圆角、系统 UI 字体，强调色取自你 Windows 的个性化设置。
- 新增浅色 / 深色，默认跟随系统；在「设置 → 外观」里可以固定为浅色或深色。原来只有一个条目的「主题」下拉已由它取代。
- 布局重排：搜索框前置为主入口，输入即真正过滤（不匹配的项会消失，空的分组连标题一起收起）；分组框改为分区标题 + 自动换行的磁贴；菜单栏文字按钮收进右上角的「更多」菜单。
- 键盘可达：↑↓←→ 在磁贴间移动，Enter 启动，Esc 先清空搜索、再隐藏窗口。
- 窗口不再整体缩放来迁就屏幕，改为限制最大尺寸后滚动。
- 不再内嵌字体，安装体积从 68.6 MB 降到约 46.9 MB。原先可选的「阿里巴巴普惠体 / Fusion Pixel / Press Start 2P」随之移除，配置里若仍是这三者会在载入时改回默认（系统字体）；选择的是已安装的系统字体则不受影响。
- 「每列按钮数」设置项移除：磁贴按可用宽度自动换行，该数值已不再影响任何东西。
- 配置、快捷方式缓存与图标缓存三个文件的磁盘格式保持不变，升级后无需重新扫描或重新提取图标。

---

- The interface was rebuilt in the Fluent 2 design language: an acrylic surface, 8 px corners, the system UI face, and the accent colour from your Windows personalization settings.
- Light and dark are now supported and follow the OS by default; Settings → Appearance can pin either one. It replaces the old single-entry theme dropdown.
- The layout was reorganised: search leads the header and typing genuinely filters (non-matches are removed, and a section whose items all disappear takes its heading with it); group boxes became section headings over a wrapping tile grid; the text menu bar moved into a "More" menu at the top right.
- Keyboard reachable throughout: arrows move across the tile grid, Enter launches, Esc clears the search and then hides the launcher.
- The window no longer scales itself down to fit the screen — it caps its size and scrolls instead.
- No fonts are bundled any more, which takes the install from 68.6 MB to about 46.9 MB. The optional Alibaba PuHuiTi / Fusion Pixel / Press Start 2P entries are gone with them: a config still naming one of the three reverts to the default system font on load, while a chosen installed font is untouched.
- The "buttons per column" setting was removed — tiles wrap to the available width, so the number no longer changed anything.
- The on-disk format of config, the shortcut cache and the icon cache is unchanged, so upgrading does not force a re-scan or a re-extraction of icons.
