## TanMenu 0.14.1

### 中文

本版继续完善 Windows 11 Fluent 主界面的观感与布局一致性。

- 主窗口不再显示在任务栏，应用页和设置页隐藏可见滚动条；内容区域保持透明，让 Mica 材质贯穿主面板。
- 搜索框移入标题栏且只在应用页显示；主目录和刷新改为导航按钮。
- 应用分类面板统一高度、标题区尺寸、内外边距和右侧留白，提示条与内容面板遵循统一的 16 DIP 间距。
- 应用按钮恢复 Fluent 默认样式并增加轻微悬停动效；按钮更加紧凑，同一列按该列最宽按钮统一宽度。
- 展开/收起、应用、主目录、刷新使用同一个导航列表和模板，保证按钮、图标与交互区域严格对齐。
- 修正关闭的提示条仍占用布局空间以及统一间距资源引起的启动异常。

### English

This release further polishes the Windows 11 Fluent launcher and makes its layout consistently aligned.

- The main window no longer appears on the taskbar, visible page scrollbars are hidden, and transparent
  content surfaces allow the Mica backdrop to continue through the main panel.
- Search now lives in the title bar and appears only on the Apps page; Root and Refresh are navigation actions.
- Category cards now share consistent height, header sizing, inner/outer insets, and right padding. Notices
  and content use a unified 16-DIP spacing rule.
- App buttons keep the default Fluent style with a subtle hover animation, use a more compact size, and
  match the widest button within each individual column.
- Expand/collapse, Apps, Root, and Refresh now use one navigation list and one item template, keeping their
  button surfaces, icons, and hit targets aligned.
- Closed notices no longer reserve layout space, and a startup crash caused by a mistyped spacing resource
  has been fixed.
