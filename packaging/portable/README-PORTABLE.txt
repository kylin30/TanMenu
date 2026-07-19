TanMenu Portable / 绿色版
=========================

中文
----
1. 将整个文件夹解压到一个可写位置，例如 D:\Apps\TanMenu。
2. 运行绿色版根目录的 TanMenu.exe。
3. 配置、主题、缓存、日志和 WebView2 用户数据都保存在绿色版根目录的 Data 文件夹。
4. 程序会自动检查 GitHub Releases；发现新版本后在主界面提示，点击并确认后才会下载和安装。
5. portable.flag 是绿色模式标记，请勿删除。
6. 绿色版不会写入开机启动注册表，因此设置中的“开机自启”不可用。
7. 如需任务栏一键呼出：打开“设置 > 行为 > 固定到任务栏”。系统支持时在 Windows 提示中确认；
   Windows 10 若打开了快捷方式位置，请右键 TanMenu 并选择“固定到任务栏”。
8. 固定后请勿移动绿色版目录。卸载时先取消任务栏固定、删除开始菜单中的 TanMenu 快捷方式，
   再退出 TanMenu 并删除整个文件夹。

从 0.9.2 起可在应用内自动更新。从 0.9.1 或更早版本升级时，请先退出 TanMenu，将新版 ZIP
解压到原绿色版目录，并保留原有 Data 文件夹。

支持 Windows 10 版本 2004（内部版本 19041）及以上的 x64 系统。
TanMenu 需要 Microsoft Edge WebView2 Runtime。Windows 11 通常已经预装；如果启动时提示
缺少运行时，请从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装 Evergreen Runtime。

English
-------
1. Extract the complete folder to a writable location, for example D:\Apps\TanMenu.
2. Run TanMenu.exe from the portable root.
3. Config, themes, caches, logs, and WebView2 user data stay in the Data folder at the portable root.
4. TanMenu automatically checks GitHub Releases and shows new versions in the launcher. Click the
   notice and confirm before TanMenu downloads or installs an update.
5. portable.flag enables portable mode. Do not remove it.
6. The portable edition does not write a Windows startup registry entry, so autostart is unavailable.
7. For one-click taskbar recall, open Settings > Behavior > Pin to taskbar. Approve the Windows prompt
   when available; if Windows 10 opens the shortcut location, right-click TanMenu and choose Pin to taskbar.
8. Keep the portable folder in place while pinned. To uninstall, unpin TanMenu, remove its Start-menu
   shortcut, exit the app, and delete the extracted folder.

In-app updates are available from 0.9.2 onward. When upgrading from 0.9.1 or earlier, exit TanMenu,
extract the new ZIP into the existing portable directory, and keep the existing Data folder.

Windows 10 version 2004 (build 19041) or later on x64 is supported.
Microsoft Edge WebView2 Runtime is required. It is normally preinstalled on Windows 11. If TanMenu
reports that it is missing, install the Evergreen Runtime from the URL above.
