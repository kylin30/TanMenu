## TanMenu 0.14.0

### 中文

本版废弃此前尚未发布的 Windows 7 Aero 界面方案，重新按现代 Windows 应用的信息架构设计。

- 所有窗口和控件改用 FluentAvalonia 3 的 Fluent v2 主题与 WinUI 风格组件，不再包含 Aero 主题库、自绘标题栏或自定义控件模板。
- 主窗口改为稳定、可调整大小的响应式窗口，并继续靠近任务栏启动。
- 主窗口改为自适应 NavigationView；“应用”是主页面，“设置”固定在导航底部。搜索、主目录和刷新只属于应用页。
- 应用分类改为横向排列的独立面板，分类内的应用按列自上而下排列；面板标题、数量标记和应用按钮获得更清晰的层级与交互反馈。
- “每列个数”可在设置中调整，默认每列 10 个；窗口会根据导航状态和内容重新计算宽高，避免分类换行或切换页面后尺寸残留。
- 独立的选项和关于窗口已删除。设置填满主窗口，使用单列可滚动卡片，修改后立即保存，不再有确定、取消、应用。
- 阿里巴巴普惠体已随程序内置，不依赖客户机安装字体；应用按钮的悬停音效和点击音效可分别关闭。
- 便携版与 MSIX 均由 .NET NativeAOT 编译，不携带托管运行时文件。
- 修正便携版和 MSIX 打包脚本，移除已删除 Aero 库的许可证打包步骤。

### English

This release retires the unreleased Windows 7 Aero design and rebuilds the interface around the
modern Windows application model.

- Every window and control now uses FluentAvalonia 3's Fluent v2 theme and WinUI-style controls. The
  Aero library, hand-drawn title bar, and custom control templates are gone.
- The launcher is a stable, resizable responsive window that still opens close to the taskbar.
- The main window now uses an adaptive NavigationView. Apps is the main page, Settings stays at the
  bottom of the navigation pane, and search/Root/Refresh belong to the Apps page.
- Launcher categories are distinct horizontal panels. Within each category, app commands fill each
  column from top to bottom; clearer headers, item counts, and interaction feedback improve hierarchy.
- Items per column is configurable and defaults to 10. The window recalculates its size for content
  and navigation state so groups do not wrap and page switches do not leave stale dimensions.
- The separate Options and About windows are removed. Settings fill the main window, use one
  scrollable column of cards, and save immediately without OK, Cancel, or Apply.
- Alibaba PuHuiTi is embedded in the app, and hover/click sounds can be controlled independently.
- Portable and MSIX packages are compiled with .NET NativeAOT and contain no managed runtime payload.
- Portable and MSIX packaging no longer reference the removed Aero library license.
