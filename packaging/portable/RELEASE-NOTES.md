## TanMenu 0.9.3

- 主界面恢复为横向分组布局，并根据各主题的真实渲染内容自动调整可视区域；不再出现纵向/横向滚动条或右侧边框被截断。
- 超出主显示器工作区时会等比缩放完整菜单，同时保持单行布局。
- 多显示器环境下，每次显示主界面都会重新锁定到主显示器底部中央；不同 DPI、不同屏幕排列下也不会漂移到副屏。
- 新增永久固定到任务栏流程：未固定时每次显示都会提示，Windows 支持时使用系统确认；Windows 10 不支持应用内固定时会打开开始菜单快捷方式供手动固定。
- 固定后的任务栏入口使用稳定应用标识和绿色版根目录启动器，自动更新后仍然有效。

> 已安装 0.9.2 的绿色版可直接在应用内更新。从 0.9.1 或更早版本升级时，请手动下载 ZIP，退出 TanMenu 后解压到原目录并保留 `Data` 文件夹。

---

- Restored the horizontal grouped launcher and made its native viewport follow each theme's actual rendered content, eliminating scrollbars and clipped right borders.
- Oversized launchers now scale uniformly to remain fully visible while preserving the single-row layout.
- Every reveal is locked to the bottom center of the primary display, including mixed-DPI and irregular multi-monitor layouts.
- Added permanent taskbar pinning with a Windows confirmation when supported and a Windows 10 manual Start-shortcut fallback.
- The pinned entry uses a stable app identity and portable-root launcher, so it survives in-place updates.

Portable 0.9.2 users can update in the app. When upgrading from 0.9.1 or earlier, download the ZIP
manually, exit TanMenu, extract it into the existing directory, and keep the `Data` folder.
