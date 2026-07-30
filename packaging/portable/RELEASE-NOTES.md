## TanMenu 0.10.0

- 界面整体重写：不再用网页渲染，改为 Avalonia 原生绘制，并以 NativeAOT 编译发布。
- 不再需要 Microsoft Edge WebView2 运行时；运行时也不再有 WebView2 宿主与 Chromium 子进程，常驻进程只剩 1 个。
- 安装体积从 564 个文件 / 248 MB 降到 11 个文件 / 68.6 MB。
- 主题精简为一套：原来可切换的 WinXP / Win7 / Windows11 / Pixel 主题包与用户自定义 `user:<名称>.css` 主题全部移除，改为一套原生绘制的 Windows 2000 经典外观。旧配置里的主题值会在读取时自动归一，其它设置不受影响。
- 配置、快捷方式缓存与图标缓存三个文件的磁盘格式保持不变，升级后无需重新扫描或重新提取图标。

> 从旧版本升级后，数据目录里可能残留 `WebView2` 和 `themes` 两个子目录（分别属于已移除的 WebView 与自定义主题功能），可以手动删除。

---

- The interface was rewritten: the launcher no longer renders through a web view. It is drawn with native Avalonia controls and published with NativeAOT.
- The Microsoft Edge WebView2 Runtime is no longer required, and there is no WebView2 host or Chromium child process any more — the app now rests as a single process.
- The installed payload dropped from 564 files / 248 MB to 11 files / 68.6 MB.
- There is now exactly one theme: the switchable WinXP / Win7 / Windows11 / Pixel theme packs and user-authored `user:<name>.css` themes were all removed in favour of a natively drawn Windows 2000 classic look. An existing theme setting is normalized on load; every other setting is untouched.
- The on-disk format of config, the shortcut cache and the icon cache is unchanged, so upgrading does not force a re-scan or a re-extraction of icons.

After upgrading from an older release the data folder may still contain `WebView2` and `themes` subfolders, left by the removed web view and the removed custom-theme feature. Both can be deleted by hand.
