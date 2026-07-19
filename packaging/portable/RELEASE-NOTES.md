## TanMenu 0.9.6

- 加快长时间闲置后的任务栏呼出：第二实例会更早通知已运行窗口，主界面复用上次尺寸立即显示，不再等待 WebView2 重新测量。
- 新增持久化图标缓存。首次提取后写入数据目录，后续冷启动直接复用；目标文件变化时会自动失效并刷新。
- 更新改为完全由用户确认：后台仅检查新版本，主界面显示更新提示；点击确认后才下载，下载完成后再次确认才重启安装。
- GitHub 标签发版现在同时保留 Microsoft Store MSIX 与 SHA-256 校验文件，供后续手动提交 Partner Center。
- 修复全新 GitHub 构建环境缺少 win-x64 运行时包时，Microsoft Store MSIX 构建失败的问题。

> 首次运行新版时会生成图标缓存；从下一次冷启动开始即可直接命中。

---

- Made taskbar recall faster after long idle periods: a second instance now signals the running window earlier, and the launcher reuses its last measured size instead of waiting for WebView2.
- Added a persistent icon cache. Icons are stored after the first extraction, reused on later cold starts, and invalidated automatically when target files change.
- Made updates fully user-controlled: background startup work only checks for a new version, the launcher shows a notice, and download/restart each require explicit confirmation.
- Tag releases now retain a Microsoft Store MSIX and SHA-256 checksum for manual Partner Center submission.
- Fixed Microsoft Store MSIX builds on clean GitHub runners where win-x64 runtime packs were not already cached.

The first run of this version creates the icon cache; subsequent cold starts can use it immediately.
