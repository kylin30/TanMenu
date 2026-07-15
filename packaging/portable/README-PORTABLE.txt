TanMenu Portable / 绿色版
=========================

中文
----
1. 将整个文件夹解压到一个可写位置，例如 D:\Apps\TanMenu。
2. 运行绿色版根目录的 TanMenu.exe。
3. 配置、主题、缓存、日志和 WebView2 用户数据都保存在绿色版根目录的 Data 文件夹。
4. 程序会自动从 GitHub Releases 检查并下载更新；在“设置 > 更新”中确认重启后安装。
5. portable.flag 是绿色模式标记，请勿删除。
6. 绿色版不会写入开机启动注册表，因此设置中的“开机自启”不可用。
7. 卸载时退出 TanMenu 并删除整个文件夹即可。

从 0.9.1 升级：先退出 TanMenu，将新版 ZIP 解压到原绿色版目录，并保留原有 Data 文件夹。

TanMenu 需要 Microsoft Edge WebView2 Runtime。Windows 11 通常已经预装；如果启动时提示
缺少运行时，请从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装 Evergreen Runtime。

English
-------
1. Extract the complete folder to a writable location, for example D:\Apps\TanMenu.
2. Run TanMenu.exe from the portable root.
3. Config, themes, caches, logs, and WebView2 user data stay in the Data folder at the portable root.
4. TanMenu automatically checks GitHub Releases and downloads updates. Confirm the restart under
   Settings > Updates to install one.
5. portable.flag enables portable mode. Do not remove it.
6. The portable edition does not write a Windows startup registry entry, so autostart is unavailable.
7. To uninstall, exit TanMenu and delete the extracted folder.

Upgrading from 0.9.1: exit TanMenu, extract the new ZIP into the existing portable directory, and
keep the existing Data folder.

Microsoft Edge WebView2 Runtime is required. It is normally preinstalled on Windows 11. If TanMenu
reports that it is missing, install the Evergreen Runtime from the URL above.
