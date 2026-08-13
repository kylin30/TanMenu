## TanMenu 0.14.2

### 中文

本版修复重复运行 TanMenu 时，已有实例无法可靠显示主页面的问题。

- 重复运行时可以唤醒已隐藏的主窗口，并切回“应用”主页面，而不是停留在设置页。
- 使用不依赖窗口可见性的命名单实例激活事件，提高托盘隐藏状态下的唤醒可靠性。
- 重复启动进程会向已有实例转交 Windows 前台激活权限，改善从任务栏或快捷方式启动时的聚焦表现。
- 保留旧版窗口消息广播作为兼容回退，便于从较早版本平滑更新。

### English

This release fixes unreliable activation of the existing TanMenu instance after launching the app again.

- A repeated launch now wakes a hidden main window and returns it to the Apps page instead of leaving it on Settings.
- A named single-instance activation event works independently of window visibility for reliable tray-state wake-up.
- The new process grants Windows foreground activation permission to the existing instance for more reliable focus.
- The legacy window-message broadcast remains as a compatibility fallback for smooth upgrades from older builds.
