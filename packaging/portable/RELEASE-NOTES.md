## TanMenu 0.12.0

- 主界面改成常规应用窗口，采用任务管理器式布局：左侧导航（应用 / 设置），右侧内容。原先失焦即隐的弹出面板不再是主界面。
- 左侧导航可收缩与展开，默认收缩，选择会记住；收缩时每项都有工具提示。原先标题栏「更多」菜单里的 打开主目录 / 刷新 / 关于 / 退出 移到导航栏底部。
- 搜索框移到标题栏中央，改用 Windows 11 的文本框聚焦样式：保留 1px 常态边框，聚焦时在底边加 2px 强调色下划线，而不是整圈强调色描边。
- 分类恢复横向排列，每个分类的快捷方式纵向填满一列再换下一列。
- 「每列个数」重新成为可设置项，默认 10（0.11.0 曾移除它）。这直接决定窗口高度：一个 47 项的分类，改动前窗口高达 1392 DIP（占满整个工作区），现在是 598 DIP。
- 「按钮大小」设置移除，磁贴统一使用原来的「小」尺寸。旧配置里的该字段仍能正常载入，不会报错。
- 快捷方式改为按钮样式：细边框、圆角、完整的悬停 / 按下 / 焦点状态。应用内容区改为实色表面，Mica 只透过导航栏和标题栏。
- 窗口每次启动都按当前内容重新计算大小；窗口位置仍然记忆。会话中手动调整过大小的，本次会话内不再自动改回。
- 设置改为主界面内的一页，Windows 11 卡片布局，每项改动即时生效并立即写盘；独立的设置窗口已删除。
- 「固定到任务栏」不再弹窗提示，改为主界面内的内嵌提示；应用已固定时该按钮显示为禁用态，而不是可点击的强调色按钮。
- 修复：设置页最底部约 47px 高的内容无法滚动到。
- 配置、快捷方式缓存与图标缓存三个文件的磁盘格式保持不变，升级后无需重新扫描或重新提取图标。

---

- The main window is now an ordinary app window laid out like Task Manager: navigation on the left (Apps / Settings), content on the right. The hide-on-blur popup panel is no longer the main surface.
- The navigation rail collapses and expands, starts collapsed, and remembers the choice; every entry keeps a tooltip while collapsed. Open home folder / Refresh / About / Exit moved out of the title bar's "More" menu and into the bottom of the rail.
- Search moved to the centre of the title bar and adopts the Windows 11 text-box focus treatment: it keeps its 1 px resting border and gains a 2 px accent underline on the bottom edge, rather than an accent ring around the whole field.
- Categories are laid out side by side again, each filling one column top to bottom before wrapping to the next.
- Items per column is a setting again, defaulting to 10 (0.11.0 had removed it). It drives the window height directly: a 47-item category previously produced a 1392 DIP window — the full work area — and now produces 598 DIP.
- The button-size setting is gone; tiles are always what that setting used to call "small". The field still deserializes from older configs without error.
- Shortcuts render as buttons: hairline border, rounded corners, and real hover / pressed / focus states. The app content area is a solid surface, with Mica showing through only the rail and the title bar.
- The window re-fits itself to its content on every launch; its position is still remembered. A manual resize during a session sticks for the rest of that session.
- Settings is a page inside the main window using Windows 11 cards, and every change applies and persists immediately. The separate settings window has been deleted.
- Pinning to the taskbar no longer prompts in a dialog — the prompt is inline in the main window — and the button renders as disabled once the app is already pinned, rather than as a clickable accent button.
- Fixed: roughly 47 px of content at the bottom of the settings page could never be scrolled into view.
- The on-disk formats of the config, shortcut cache and icon cache are unchanged, so upgrading needs no rescan and no icon re-extraction.
