using System.Globalization;

namespace TanMenu.Core.Services;

public static class AppLanguage
{
    public const string Auto = "Auto";
    public const string ZhHans = "zh-Hans";
    public const string EnUs = "en-US";

    private static readonly Dictionary<string, (string Zh, string En)> Texts =
        new(StringComparer.Ordinal)
        {
            ["LanguageAuto"] = ("自动（跟随系统）", "Auto (system language)"),
            ["LanguageZhHans"] = ("中文（简体）", "Chinese (Simplified)"),
            ["LanguageEnUs"] = ("英语（美国）", "English (United States)"),

            ["Ok"] = ("确定", "OK"),
            ["Cancel"] = ("取消", "Cancel"),
            // The settings surface's third commit button, added by Round 8, Task 1's delayed commit.
            // The English word is Apply, not Save: PUBLISHED, win-property-win — "Right-align the
            // buttons and use this order (from left to right): OK, Cancel, and Apply." The Chinese 应用
            // is the same word the launcher's 应用 PAGE was labelled with until Round 8, Task 3 deleted
            // the navigation pane and the "NavApps" key with it — a collision that is Windows' own,
            // since Apply and Apps are both 应用 in the Chinese Windows UI. The keys stayed separate for
            // that reason and only one of them is left.
            ["Apply"] = ("应用", "Apply"),
            // The SAME button, with its access-key marker (Round 8, Task 2). PUBLISHED,
            // https://learn.microsoft.com/en-us/windows/win32/uxguide/win-property-win — of the three
            // commit buttons only Apply gets one: "Assign 'A' as the access key." 确定 and 取消 get
            // none, because PUBLISHED, inter-keyboard, "Enter and Esc are used for their access keys."
            //
            // A SECOND KEY RATHER THAN AN UNDERSCORE SPLICED IN BY CODE, because where the marker goes
            // is a localisation decision and not an arithmetic one. English underlines the letter in
            // the word; Chinese cannot (the letter is not in 应用) and Windows' own Chinese UI appends
            // the Latin letter in parentheses instead. Both halves therefore have to be written out,
            // and the plain "Apply" above stays for anything that wants the label without the marker.
            // "_" is Avalonia's marker character (Avalonia.Controls.Primitives.AccessText, decompiled:
            // the first "_" not followed by another "_" marks the next rune) and it is stripped before
            // the text is drawn, so neither string ever renders an underscore.
            ["ApplyWithAccessKey"] = ("应用(_A)", "_Apply"),
            // The Options dialog's own window title (Round 8, Task 2). PUBLISHED,
            // https://learn.microsoft.com/en-us/windows/win32/uxguide/win-property-win — "Options
            // dialog box titles — Use 'Options.' Use title-style capitalization. Don't use ending
            // punctuation." So it is "Options", not "TanMenu Settings" and not "TanMenu 设置".
            //
            // A KEY OF ITS OWN, not a reuse of the then-"NavSettings" ("设置"/"Settings"), because the
            // two differ in BOTH halves: the English is fixed by the sentence above, and the Chinese
            // title of a Windows property window is 选项 while the thing the user clicks to reach it
            // is 设置. Round 8, Task 3 deleted NavSettings with the navigation pane and minted
            // "MenuOptions" = ("设置", "Options") below for the menu bar's own item — same English word,
            // the other Chinese one — which is exactly why the two cannot be folded together. The tray's
            // "TraySettings" is a third and stays separate for the same reason.
            ["Options"] = ("选项", "Options"),
            // 取消 and the title-bar × ask this, identically, when the draft is dirty (Round 8, Task 2).
            // A NAMED DEVIATION: PUBLISHED, win-property-win — "Don't confirm if users discard their
            // changes." It is deviated from on the instruction for this round and on one defensible
            // ground of its own: two of this dialog's settings are HIGH-EFFORT to re-enter — a captured
            // global hotkey (BehaviorCard.OnHotkeyKeyDown) and a picked root folder
            // (FoldersCard.OnPickRootClick) — and each costs the user a real interaction to redo.
            // 取消 and × share the one string because PUBLISHED, same article, "Make sure the Close
            // button on the title bar has the same effect as Cancel or Close" is verbatim and is the
            // rule this deviation must not also break.
            ["DiscardChangesConfirm"] = ("有尚未应用的更改，关闭将丢弃它们。\n确定？", "There are changes you have not applied yet. Closing will discard them.\nContinue?"),
            ["Appearance"] = ("外观", "Appearance"),
            ["Behavior"] = ("行为", "Behavior"),
            ["Folders"] = ("文件夹", "Folders"),
            ["CommonTools"] = ("常用工具", "Common Tools"),
            // No trailing colon: these are settings-ROW titles — an icon, then a title over a
            // subtitle, with the control on its own trailing edge — not the fixed-width
            // "label: control" pairs the pre-Fluent-2 dialog used. Only SettingsWindow (deleted in
            // Task 13) ever paired these with an adjacent control on the SAME line, which is the one
            // layout a colon suited. The row shape is Round 5, item 3's spec and survived Round 7's
            // Aero conversion intact; the sentence used to call it "Windows 11 settings-card titles",
            // which stopped describing the container at Round 7, Task 7.
            ["Language"] = ("语言", "Language"),
            // "Theme" / "ThemeFolder" / "ThemeFolderTooltip" all went with the theme system. The
            // Fluent 2 rebuild has one look rendered in two variants, so the row offers light/dark.
            // NOTE the key is AppearanceMode, not Appearance — "Appearance" is already the tab header.
            ["AppearanceMode"] = ("外观", "Appearance"),
            ["AppearanceSystem"] = ("跟随系统", "Use system setting"),
            ["AppearanceLight"] = ("浅色", "Light"),
            ["AppearanceDark"] = ("深色", "Dark"),
            ["Font"] = ("字体", "Font"),
            // "ButtonSize"/"SizeSmall"/"SizeMedium"/"SizeLarge"/"SizeCardSub" went with the 按钮大小
            // setting itself (Round 3, Task 4), which left every tile at the width that setting had
            // called "小" (168 DIP). Round 4, item 3 removed that fixed width too — a tile is as wide
            // as its own icon-plus-label content — so no string here names a tile size at all any
            // more. See GeneralConfig.ButtonSize's own remarks.
            ["ColButtonCount"] = ("每列个数", "Items per column"),
            // THREE 外观 SUBTITLES WERE DELETED IN ROUND 8, TASK 6, not orphaned: LanguageCardSub,
            // AppearanceCardSub and FontCardSub. They were written in Round 7 Task 8 for the Windows
            // 11 row card, whose anatomy REQUIRED a second line under every title, and each of them
            // restated the label above it — "应用界面显示所使用的语言" under 语言. Task 6 replaced that
            // anatomy with Windows 7's label-and-control grid, which has no such slot, so the strings
            // had no consumer and no place to go back to.
            //
            // THIS ONE SURVIVED, and the difference is the test that kept it: it describes a
            // BEHAVIOUR the label cannot ("超出后另起一列"), rather than restating 每列个数. It is the
            // static line below the spinner on AppearanceCard.
            ["ColButtonCountCardSub"] = ("设置“应用”页面每一列最多显示的快捷方式数量，超出后另起一列", "Set how many shortcuts each column shows on the Apps page before wrapping to a new one"),
            // "BuiltInFonts" went with the four embedded TTFs — every selectable family is installed
            // now, so there is no second group to caption.
            ["DefaultFont"] = ("默认（系统字体）", "Default (system font)"),
            ["SystemFonts"] = ("系统字体", "System fonts"),
            ["AutoClose"] = ("失焦自动隐藏", "Auto-hide when focus is lost"),
            ["TopMost"] = ("窗口置顶", "Always on top"),
            ["PinToTaskbar"] = ("固定到任务栏", "Pin to taskbar"),
            ["PinToTaskbarManual"] = ("打开固定位置", "Open pin location"),
            ["PinnedToTaskbar"] = ("已固定到任务栏", "Pinned to taskbar"),
            // The in-page InfoBar's message (AppPage.RefreshPinNoticeAsync). Its buttons are 固定 /
            // 不再提示 plus a close glyph — there is no 确定, because this stopped being a modal
            // MessageBox in 0.12 — and the window no longer hides on blur unless the user opts in, so
            // neither claim may appear here. Kept to two short sentences: an InfoBar sits above the
            // categories and a wall of text pushes the launcher's own content off screen.
            ["TaskbarPinRequiredPrompt"] = ("TanMenu 尚未固定到任务栏。固定后，点击任务栏图标即可显示主界面。\n点击“固定”开始：系统支持时请在 Windows 提示中确认，否则将打开 TanMenu 快捷方式，请右键选择“固定到任务栏”。", "TanMenu is not pinned to the taskbar yet. Once pinned, click its taskbar icon to show the launcher.\nSelect Pin to start: approve the Windows confirmation when it appears, otherwise TanMenu opens its shortcut so you can right-click it and choose Pin to taskbar."),
            ["PinToTaskbarHelp"] = ("固定后，点击任务栏图标即可显示主界面。系统支持时由 Windows 确认，否则打开快捷方式供手动固定。", "Once pinned, click the taskbar icon to show TanMenu. Windows confirms when supported; otherwise TanMenu opens its shortcut for manual pinning."),
            ["PinnedToTaskbarHelp"] = ("TanMenu 已永久固定。点击任务栏图标可随时显示主界面。", "TanMenu is permanently pinned. Click its taskbar icon whenever you want to show the launcher."),
            ["PinToTaskbarWorking"] = ("正在请求 Windows 固定 TanMenu…", "Asking Windows to pin TanMenu…"),
            // 右侧按钮 / "the button on the right", not 上方 / "above", and the direction survived the
            // row grammar that first made it true. Round 5, item 3 put PinTaskbarBtn in the card's
            // trailing column, to the right of the text these strings land in. Round 8, Task 6
            // replaced the card with a two-column DLU grid: 固定到任务栏 is the label in column 0 and
            // the button is in column 1, i.e. still to its right, with this status line spanning
            // underneath. What changed is that "right" is now measured from the row's LABEL rather
            // than from its subtitle; what did not change is where the user has to look.
            ["PinToTaskbarNotAllowed"] = ("Windows 当前不允许应用发起固定。点击右侧按钮打开 TanMenu 快捷方式，再右键选择“固定到任务栏”。", "Windows is not allowing an app-initiated pin. Use the button on the right to open the TanMenu shortcut, then right-click it and choose Pin to taskbar."),
            ["PinToTaskbarUnsupported"] = ("当前 Windows（包括部分 Windows 10 环境）不支持应用内固定。点击右侧按钮打开 TanMenu 快捷方式，再右键选择“固定到任务栏”。", "This Windows environment, including some Windows 10 systems, does not support in-app pinning. Use the button on the right to open the TanMenu shortcut, then right-click it and choose Pin to taskbar."),
            // NO HARD LINE BREAK, unlike every other {0}-carrying error string in this file. This one is
            // the third member of the set above — ApplyTaskbarPinState writes it into the 固定到任务栏
            // row's status line, which renders "\n" as a real second line at EVERY window width. The
            // CONSTRAINT OUTLIVED ITS FIRST REASON: under Round 5's card grammar a break took the row's
            // header past the two lines invariant I1 centred its icon and control on; under Round 8 Task
            // 6's DLU grid there is no icon and no two-line header, and what a break costs instead is
            // that 启用全局热键 and everything below it move down by a whole line, on a dialog whose
            // Height is a literal. The other seven strings that can land in this line are plain sentences
            // which only wrap when the surface is narrow; this one used to break unconditionally, in a
            // state that is both reachable and persistent. The error is parenthesised rather than
            // separated by punctuation because {0} is an arbitrary Exception.Message that may or may not
            // end in a full stop of its own. It is still unbounded, so a long enough message wraps — but
            // it wraps, the same way a long 数据文件夹 subtitle does, instead of breaking by construction.
            ["PinToTaskbarFailed"] = ("固定失败（{0}）。请右键开始菜单中的 TanMenu → 固定到任务栏。", "Pinning failed ({0}). Right-click TanMenu in Start and choose Pin to taskbar."),
            ["PinToTaskbarManualOpened"] = ("已打开 TanMenu 快捷方式。请右键它并选择“固定到任务栏”；完成后，下次显示主界面会自动识别。", "The TanMenu shortcut is open. Right-click it and choose Pin to taskbar; TanMenu will detect it the next time the launcher is shown."),
            ["PinToTaskbarManualOpenFailed"] = ("无法打开 TanMenu 快捷方式位置。请从开始菜单找到 TanMenu，右键选择“固定到任务栏”。", "Could not open the TanMenu shortcut location. Find TanMenu in Start, right-click it, and choose Pin to taskbar."),
            ["PinNow"] = ("固定", "Pin"),
            ["PinDontAskAgain"] = ("不再提示", "Don't ask again"),
            ["Dismiss"] = ("关闭", "Dismiss"),
            ["AutoStart"] = ("开机自启", "Start with Windows"),
            ["AutoStartPortableHelp"] = ("绿色版不会写入注册表，因此不提供开机自启。", "The portable edition does not write to the registry, so startup is unavailable."),
            ["EnableHotkey"] = ("启用全局热键（呼出 / 隐藏）", "Enable global hotkey (show / hide)"),
            ["Hotkey"] = ("热键", "Hotkey"),
            ["HotkeyTooltip"] = ("点击后按下组合键来设置（需包含 Ctrl/Alt/Shift/Win）", "Click and press a key combo including Ctrl/Alt/Shift/Win"),
            ["Clear"] = ("清除", "Clear"),
            // 右侧输入框 / "the box on the right" for the same reason as PinToTaskbarNotAllowed above:
            // HotkeyBox is in column 1 of the 热键 row, to the right of that row's label, and this help
            // line spans underneath the pair. It said 上方 / "above" until Round 5 moved the box.
            ["HotkeyHelp"] = ("默认无热键。点击右侧输入框并按下组合键来自定义。", "No hotkey by default. Click the box on the right and press a key combo to customize it."),
            ["None"] = ("(无)", "(None)"),
            // THE FOUR 行为 SUBTITLES WERE DELETED IN ROUND 8, TASK 6 — AutoCloseCardSub,
            // TopMostCardSub, AutoStartCardSub, HotkeyEnabledCardSub — for the same reason as the
            // three 外观 ones above, and with one extra: all four rows are CHECK BOXES, and Task 6
            // moved each label ONTO its box (PUBLISHED, ctrl-check-boxes). A restating subtitle under
            // a self-labelling control is the label printed twice. AutoStartPortableHelp, three lines
            // up, is what a 行为 row's second line looks like when it is NOT a restatement: it says
            // why the box above it is disabled, and it survives.
            ["MainFolder"] = ("主文件夹", "Main Folder"),
            // Round 8, Task 6: the 文件夹 tab is four group boxes, and 位置 labels the path line inside
            // the two folder ones. It is deliberately generic — one key for both boxes, because the
            // box's own head already says WHICH folder.
            //
            // IT ENDS IN A COLON AND THE FOUR 外观 LABELS DO NOT, which is a distinction rather than an
            // inconsistency: this label is followed by another run of TEXT (the path), 5 relative
            // pixels away, and without the colon the two read as one sentence — seen in Task 6's own
            // first capture of the 更新 tab, where "更新状态" ran straight into "应用内更新未启用。".
            // A label followed by a CONTROL has the control's own frame as its boundary and needs no
            // punctuation to supply one. UpdateStatusTitle is the only other label of this kind and it
            // carries a colon for the same reason.
            ["Location"] = ("位置：", "Location:"),
            ["MainFolderHelp"] = ("显示该文件夹下的子文件夹（例如设为桌面，桌面上的文件夹就会成为分组）。", "Shows subfolders under this folder. For example, use Desktop to turn desktop folders into groups."),
            ["Open"] = ("打开", "Open"),
            ["Choose"] = ("选择…", "Choose..."),
            ["DataFolder"] = ("数据文件夹", "Data Folder"),
            ["DataFolderHelp"] = ("配置与缓存的存放位置（默认在“文档\\TanMenu”）。更改会立即移动数据并生效（与“应用”按钮无关）。", "Where config and caches are stored. The default is Documents\\TanMenu. Changing it moves data immediately and does not depend on Apply."),
            ["DataFolderPackagedHelp"] = ("Store/MSIX 版本使用 Windows 为应用分配的本地 AppData。可打开、清理、备份或恢复配置，但不能改到任意文件夹。", "The Store/MSIX version uses Windows-managed local app data. You can open, clear, back up, or restore config, but cannot move it to an arbitrary folder."),
            ["DataFolderPortableHelp"] = ("绿色版的配置、缓存与日志固定保存在绿色版根目录的 Data 文件夹中，软件更新不会覆盖它。", "The portable edition keeps config, caches, and logs in the Data folder at the portable app root, where updates do not overwrite it."),
            ["Change"] = ("更改…", "Change..."),
            ["ResetDefault"] = ("重置为默认", "Reset to Default"),
            ["ClearCache"] = ("清理缓存", "Clear Cache"),
            ["Backup"] = ("备份…", "Back Up..."),
            ["Restore"] = ("恢复…", "Restore..."),
            // ROUND 8, TASK 6 SPLIT 维护 IN TWO. Round 7 Task 10 had given 清理缓存/备份/恢复 one
            // labelled card ("Maintenance" + "MaintenanceCardSub", both deleted here) because the
            // pre-0.12 dialog just floated the three buttons at the bottom of its 文件夹 tab with no
            // heading at all. One heading over three buttons was still one heading too few: clearing
            // the menu cache throws away DERIVED data and can be undone by doing nothing, while
            // 备份/恢复 read and overwrite config.json itself. They are two sets, so they are two group
            // boxes — which is the case ctrl-group-boxes sanctions a box for ("Is there more than one
            // control in the group?"), and it is why 文件夹 is the one tab that keeps its boxes.
            ["Cache"] = ("缓存", "Cache"),
            ["CacheHelp"] = ("清理后，菜单会重新解析快捷方式并重新提取图标。", "After clearing, TanMenu re-resolves every shortcut and re-extracts its icon."),
            ["BackupRestore"] = ("备份", "Backup"),
            ["BackupHelp"] = ("把当前配置保存为文件，或用备份文件覆盖当前配置。", "Save the current config to a file, or overwrite the current config from a backup file."),
            ["DataKindApp"] = ("应用数据", "App data"),
            ["DataKindPortable"] = ("绿色版", "Portable"),
            ["DataKindDefault"] = ("默认", "Default"),
            ["DataKindCustom"] = ("自定义", "Custom"),
            ["CurrentDataPending"] = ("当前：{0} · …", "Current: {0} · ..."),
            ["CurrentDataSize"] = ("当前：{0} · {1}", "Current: {0} · {1}"),
            ["PortableDataCannotChange"] = ("绿色版的数据文件夹固定为绿色版根目录下的 Data，不能在设置中更改。", "The portable edition always uses the Data folder at the portable app root and cannot relocate it in Settings."),
            ["Updates"] = ("更新", "Updates"),
            // Consumer moved, key unchanged. It labelled the 更新 group's 当前版本 card until Round 5
            // part B deleted that card (an information card with no control, sitting in an action group,
            // about to print the version a second time one card apart); part C's 关于 card
            // (Views/Settings/AboutCard.axaml) reads it now, as the first of that card's two value lines.
            ["CurrentVersion"] = ("当前版本：{0}", "Current version: {0}"),
            ["UpdatePortableHelp"] = ("绿色版会自动检查 GitHub Releases；发现新版本后会在主界面提示，只有确认后才会下载和安装。", "The portable edition checks GitHub Releases automatically. New versions appear in the launcher and are downloaded and installed only after confirmation."),
            ["UpdateStoreManaged"] = ("Microsoft Store 版本由 Microsoft Store 管理更新。", "Updates for the Microsoft Store edition are managed by Microsoft Store."),
            ["UpdateUnavailable"] = ("当前运行方式不支持应用内更新。请使用 Microsoft Store 版，或从 GitHub Release 下载绿色版。", "This build cannot update itself. Use the Microsoft Store edition or download the portable edition from GitHub Releases."),
            // The 更新 row's LABEL, and the sentence beside it is its value. Round 5, item 3 minted this
            // string because the row had no title at all — the dynamic status sentence itself was the
            // row's title, and "正在等待检查更新。" is not a heading — and demoted the sentence to the
            // row's subtitle. Round 8, Task 6 finished the correction: on a two-column DLU grid the
            // sentence is a VALUE in column 1 opposite this label in column 0, which is what it always
            // was in substance.
            // The colon is load-bearing here for the reason Location's remark records: this label is
            // followed by a run of TEXT rather than by a control, and without it the row read as one
            // sentence ("更新状态 应用内更新未启用。") in Task 6's first capture of this tab.
            ["UpdateStatusTitle"] = ("更新状态：", "Update status:"),
            ["UpdateStoreManagedStatus"] = ("无需在这里操作，请在 Microsoft Store 中检查更新。", "No action is needed here. Check for updates in Microsoft Store."),
            ["UpdateDisabledStatus"] = ("应用内更新未启用。", "In-app updates are not enabled."),
            ["UpdateIdle"] = ("正在等待检查更新。", "Ready to check for updates."),
            ["UpdateChecking"] = ("正在检查更新…", "Checking for updates…"),
            ["UpdateUpToDate"] = ("已经是最新版本。", "TanMenu is up to date."),
            ["UpdateAvailable"] = ("发现新版本 {0}。", "Version {0} is available."),
            ["UpdateDownloading"] = ("正在下载版本 {0}… {1}%", "Downloading version {0}… {1}%"),
            ["UpdateReady"] = ("版本 {0} 已下载，重启后即可完成更新。", "Version {0} is ready and will be installed after restart."),
            ["UpdateFailed"] = ("更新失败：{0}", "Update failed: {0}"),
            ["CheckForUpdates"] = ("检查更新", "Check for updates"),
            ["DownloadUpdate"] = ("下载更新", "Download update"),
            ["RestartToUpdate"] = ("立即重启更新", "Restart and update"),
            ["UpdateTitle"] = ("TanMenu 更新", "TanMenu Update"),
            ["UpdateNoticeAvailable"] = ("发现新版本 {0}，点击下载更新", "Version {0} is available — click to download"),
            ["UpdateNoticeReady"] = ("新版本 {0} 已下载，点击重启完成更新", "Version {0} is ready — click to restart and update"),
            ["UpdateDownloadPrompt"] = ("发现新版本 {0}。\n\n是否现在下载更新？", "Version {0} is available.\n\nDownload it now?"),
            ["UpdateRestartPrompt"] = ("新版本 {0} 已下载。是否立即重启并完成更新？", "Version {0} has been downloaded. Restart now to finish updating?"),
            ["UnknownError"] = ("未知错误", "Unknown error"),
            ["ShowCommonTools"] = ("显示“常用工具”分类", "Show the Common Tools group"),
            ["CommonToolsHint"] = ("勾选要在该分类中显示的工具：", "Choose the tools to show in this group:"),
            ["ToolCommand"] = ("启动命令：{0}", "Launch command: {0}"),
            ["ToolCheckbox"] = ("{0}   （{1}）", "{0}   ({1})"),

            ["SelectRootFolder"] = ("选择主文件夹", "Choose Main Folder"),
            ["SelectDataFolder"] = ("选择数据文件夹", "Choose Data Folder"),
            ["SaveFailed"] = ("保存设置失败：{0}", "Failed to save settings: {0}"),
            ["PackagedDataCannotChange"] = ("Store/MSIX 版本的数据文件夹由 Windows 管理，不能更改。", "The Store/MSIX data folder is managed by Windows and cannot be changed."),
            ["PackagedDataCannotReset"] = ("Store/MSIX 版本的数据文件夹由 Windows 管理，不能重置。", "The Store/MSIX data folder is managed by Windows and cannot be reset."),
            ["DataAlreadyDefault"] = ("数据已在默认位置。", "Data is already in the default location."),
            ["DefaultDataExists"] = ("默认位置已存在配置数据：\n{0}\n\n“是”→ 改用该位置的现有数据；\n“否”→ 用当前数据覆盖它；\n“取消”→ 不操作。", "The default location already contains config data:\n{0}\n\nYes: use the existing data there.\nNo: overwrite it with current data.\nCancel: do nothing."),
            ["ResetToDefault"] = ("重置为默认", "Reset to Default"),
            ["UnableCleanDefaultConfig"] = ("无法清理默认位置的现有配置：{0}", "Could not clean existing config at the default location: {0}"),
            ["MoveDataConfirm"] = ("将把当前数据移回默认位置：\n{0}\n\n确定？", "Move current data back to the default location:\n{0}\n\nContinue?"),
            // 更改数据文件夹 has no pre-action confirmation in the old TabItem-based dialog (it picks a
            // folder and relocates immediately, only informing after the fact) — the 设置 page (Task 10)
            // adds one deliberately, since 更改/重置/恢复 are the three actions on this page that move or
            // overwrite real user data rather than just a setting.
            ["ChangeDataConfirm"] = ("将把数据文件夹更改到：\n{0}\n\n配置和缓存会移动到新位置，且立即生效。确定？", "The data folder will change to:\n{0}\n\nConfig and caches will move to the new location, effective immediately. Continue?"),
            ["ChangeDataFailed"] = ("更改数据文件夹失败：{0}", "Failed to change data folder: {0}"),
            ["DataSwitchedExisting"] = ("已切换到目标文件夹中的现有数据。", "Switched to the existing data in the target folder."),
            ["DataMoved"] = ("已将当前数据移动到新文件夹。", "Moved current data to the new folder."),
            ["EffectiveNow"] = ("已立即生效。", "The change is effective immediately."),
            ["RootNotSet"] = ("主文件夹未设置或不存在。", "The main folder is not set or does not exist."),
            ["CacheCleared"] = ("缓存已清理，菜单将重新加载。", "Cache cleared. The menu will reload."),
            ["BackupConfig"] = ("备份配置", "Back Up Config"),
            ["JsonConfigFilter"] = ("JSON 配置 (*.json)|*.json", "JSON config (*.json)|*.json"),
            ["AllFilesFilter"] = ("JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*", "JSON config (*.json)|*.json|All files (*.*)|*.*"),
            ["BackupFileName"] = ("TanMenu-config-备份.json", "TanMenu-config-backup.json"),
            // Round 8, Task 2 closes the one gap Task 1 shipped knowingly and recorded in its report.
            // 备份 serialises ConfigService.Config — what is COMMITTED — so a backup taken while 应用 is
            // still enabled silently omits every pending edit. Task 1 left it unguarded because the
            // design's command list keeps 备份 immediate and specifies no warning, and Task 1 had no
            // prompt machinery of its own. Task 2 does: the Options dialog prompts on 取消 / × when the
            // draft is dirty, and this is the same idea at the other boundary where a dirty draft is
            // invisible. The delivery is FoldersCard's own ConfirmAsync (OkCancel), which is what its
            // three other data-touching commands already use — not the dialog's YesNo — because the
            // question here is "go ahead?", not "discard?".
            //
            // It STATES the fact and offers to continue rather than blocking: a backup of the committed
            // config is a legitimate thing to want, and the failure this prevents is learning WHICH
            // config you backed up only when you restore it. 备份 itself stays immediate; nothing about
            // the command changes.
            ["BackupPendingChanges"] = ("有尚未应用的更改，备份只包含已保存的设置。\n仍要继续？", "There are changes you have not applied yet. The backup will contain only the settings already saved.\nContinue anyway?"),
            ["BackupSaved"] = ("配置已备份到：\n{0}", "Config backed up to:\n{0}"),
            ["BackupFailed"] = ("备份失败：{0}", "Backup failed: {0}"),
            ["RestoreConfig"] = ("从备份恢复配置", "Restore Config from Backup"),
            // This key's wording warns that restoring replaces the ENTIRE current config (RootFolder,
            // all of General) with the backup's contents and cannot be undone. It deliberately does NOT
            // warn about "unapplied changes" the way the pre-0.12 dialog's did, even though Round 8,
            // Task 1 brought staged edits back: 恢复 RE-SEEDS the draft when it completes
            // (FoldersCard.OnRestoreConfigClick), so a pending edit is dropped rather than silently
            // written over the restored file — which is a consequence of the restore the sentence above
            // already covers, not a separate hazard needing its own sentence.
            ["RestoreConfirm"] = ("将用所选备份覆盖当前的全部配置，此操作无法撤销。\n确定？", "The selected backup will overwrite your entire current config. This cannot be undone.\nContinue?"),
            ["RestoreSucceeded"] = ("配置已恢复并生效。", "Config restored and applied."),
            ["RestoreInvalid"] = ("恢复失败：所选文件不是有效的 TanMenu 配置。", "Restore failed: the selected file is not a valid TanMenu config."),

            // MenuOpenRoot/MenuRefresh/MenuExit are three of the classic menu band's five items'
            // ACCESSIBLE NAMES (MainWindow.ApplyMenuText). They have had three readers in three rounds
            // and the strings never changed: the title strip's ⋯ overflow MenuFlyout until Round 3
            // Task 2, the navigation pane's command rows until Round 8 Task 3, and the menu band since.
            // The name is finally accurate again — these really are menu items. MenuMore ("更多"), the
            // overflow button's own tooltip/AutomationProperties.Name, had no other reader and was
            // removed with that button.
            //
            // There was a fourth, MenuAbout, until Round 5 item 2 moved 关于 off the rail and into the
            // 设置 page. Its text was right and its NAME was not — a Menu* key labelling a settings
            // group head is a lie this very comment would have had to carry — so it was renamed to
            // "About" and re-homed below with the other 关于 string rather than deleted and re-minted.
            // ROUND 8, TASK 3 BROUGHT 关于 BACK TO A MENU and did NOT re-mint a Menu* key for it: the
            // string is still shared with the About group head, so re-naming it would have made the
            // same kind of lie in the other direction. Only its access-key twin below is Menu*-named.
            ["MenuOpenRoot"] = ("打开主目录", "Open Main Folder"),
            ["MenuRefresh"] = ("刷新", "Refresh"),
            ["MenuExit"] = ("退出", "Exit"),
            // 设置's own menu-bar string, minted by Round 8, Task 3. THE THIRD OF THREE 设置-ish keys
            // and none of them can be folded into another: "Options" above is the DIALOG's title
            // (选项 / Options — PUBLISHED, win-property-win, "Options dialog box titles — Use
            // 'Options.'"), "TraySettings" below is the tray entry (设置 / Settings), and this is the
            // menu item — 设置 in Chinese, because that is what the user clicks, and Options in
            // English, because PUBLISHED, cmd-menus: "Settings — Don't use as a menu label. Use
            // Options instead." It replaces "NavSettings", which was ("设置", "Settings") and was
            // deleted with the navigation pane in the same task; the Chinese half is re-minted here
            // unchanged and the English half is the one that had to differ.
            ["MenuOptions"] = ("设置", "Options"),

            // ══ The same five, with their access-key markers ══════════════════════════════════════
            // Every menu item gets an access key, no exceptions — PUBLISHED,
            // https://learn.microsoft.com/en-us/windows/win32/uxguide/inter-keyboard. O = Options,
            // x = Exit and A = About are that article's own standard table; M for 打开主目录 and R for
            // 刷新 are app-specific (it assigns neither), with M taken on the article's stated
            // preference for wide letters "such as w, m".
            //
            // A SECOND KEY PER ITEM RATHER THAN AN UNDERSCORE SPLICED IN BY CODE, exactly as
            // "ApplyWithAccessKey" above: where the marker goes is a localisation decision. English
            // underlines a letter inside the word; Chinese cannot (the Latin letter is not in the
            // word) and Windows' own Chinese UI appends it in parentheses instead. "_" is Avalonia's
            // marker character and AccessText strips it before drawing, so neither string ever renders
            // an underscore — but a UIA name taken from the raw header string WOULD keep it, which is
            // why MainWindow.ApplyMenuText writes the plain key above as AutomationProperties.Name and
            // this one as the Header. That pairing was MEASURED in Round 8, Task 2 on the Options
            // dialog's 应用 button, whose name read literally "应用(_A)" until it was given one.
            ["MenuOptionsWithAccessKey"] = ("设置(_O)", "_Options"),
            ["MenuOpenRootWithAccessKey"] = ("打开主目录(_M)", "Open _Main Folder"),
            ["MenuRefreshWithAccessKey"] = ("刷新(_R)", "_Refresh"),
            ["MenuExitWithAccessKey"] = ("退出(_X)", "E_xit"),
            ["MenuAboutWithAccessKey"] = ("关于(_A)", "_About"),
            // The 设置 page's last group and its one card (Views/Settings/AboutCard.axaml). "About" is
            // the group head; AboutPublisher is the card's subtitle. The overlay this replaced showed an
            // untranslated English "by TanSoft" (Controls/SplashPanel.axaml still does, inside a branded
            // splash card where that is fine) — a settings-row subtitle is not the place for one, so the
            // line is localized here.
            ["About"] = ("关于", "About"),
            ["AboutPublisher"] = ("由 TanSoft 出品", "by TanSoft"),
            // "NavApps" ("应用"/"Apps") and "NavSettings" ("设置"/"Settings") were here until Round 8,
            // Task 3 deleted the navigation pane. They labelled its two page rows, MainWindow's
            // ApplyRailText was their only reader, and that method is ApplyMenuText now — a band of
            // five commands with no 应用 row at all, because there is no page to navigate to. 设置's
            // Chinese text is re-minted above as "MenuOptions" so the English half can be Options
            // rather than Settings; "应用" survives in "Apply" above, which is a different word that
            // Chinese Windows happens to spell the same way.
            ["NoMatchTitle"] = ("未找到匹配项", "No matches"),
            ["NoMatchHelp"] = ("没有名称包含该关键词的快捷方式。", "No shortcut's name contains that text."),
            ["ClearFilter"] = ("清除搜索", "Clear search"),
            ["SearchPlaceholder"] = ("搜索快捷方式…", "Search shortcuts..."),
            ["EmptyTitle"] = ("还没有任何快捷方式", "No shortcuts yet"),
            // "选项 → 文件夹" named the old overflow-menu entry the nav rail's 设置 page replaced (§2 of
            // the shell redesign spec: the rail is the entry point now, and the overflow menu lost 选项).
            ["EmptyHelp"] = ("在「设置 → 文件夹」里设置主文件夹，或开启「常用工具」分类。", "Set a main folder in Settings -> Folders, or enable the Common Tools group."),
            ["OpenOptions"] = ("打开设置…", "Open Settings..."),
            ["LaunchFailed"] = ("无法启动：{0}", "Could not launch: {0}"),
            ["RefreshFailed"] = ("刷新失败", "Refresh failed"),
            // ClickToDismiss ("点击任意处关闭") was here. It labelled the 关于 overlay's dismiss hint, its
            // only consumer, and Round 5 item 2 deleted that overlay along with SplashPanel's ShowHint /
            // HintText and LauncherViewModel.ClickToDismissText.
            ["OpenFolderTooltip"] = ("打开文件夹", "Open folder"),

            ["TrayShow"] = ("显示", "Show"),
            ["TraySettings"] = ("设置", "Settings"),
            ["TrayExit"] = ("退出", "Exit"),
            ["StartupFailed"] = ("TanMenu 启动失败：\n{0}", "TanMenu failed to start:\n{0}"),
            ["HotkeyRegistrationFailed"] = ("全局热键「{0}」注册失败，可能已被其它程序占用。\n请在设置里改用其它组合键。", "Global hotkey \"{0}\" could not be registered. It may already be used by another app.\nChoose another shortcut in Settings."),
            ["DataFolderInsideItself"] = ("不能把数据文件夹移动到当前数据文件夹的子目录中。", "The data folder cannot be moved into one of its own subfolders."),
        };

    private static readonly Dictionary<string, (string Zh, string En)> BuiltInToolNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["calc.exe"] = ("计算器", "Calculator"),
            ["notepad.exe"] = ("记事本", "Notepad"),
            ["taskmgr.exe"] = ("任务管理器", "Task Manager"),
            ["control.exe"] = ("控制面板", "Control Panel"),
            ["mspaint.exe"] = ("画图", "Paint"),
        };

    public static string Resolve(string? language, CultureInfo? culture = null)
    {
        if (string.Equals(language, ZhHans, StringComparison.OrdinalIgnoreCase))
            return ZhHans;
        if (string.Equals(language, EnUs, StringComparison.OrdinalIgnoreCase))
            return EnUs;

        var name = (culture ?? CultureInfo.CurrentUICulture).Name;
        return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? ZhHans : EnUs;
    }

    public static bool IsValidSetting(string? language) =>
        string.Equals(language, Auto, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, ZhHans, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, EnUs, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeSetting(string? language) =>
        string.Equals(language, ZhHans, StringComparison.OrdinalIgnoreCase) ? ZhHans :
        string.Equals(language, EnUs, StringComparison.OrdinalIgnoreCase) ? EnUs :
        Auto;

    public static string Text(string key, string? language)
    {
        if (!Texts.TryGetValue(key, out var value))
            return key;
        return Resolve(language) == ZhHans ? value.Zh : value.En;
    }

    public static string Format(string key, string? language, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Text(key, language), args);

    public static string LanguageLabel(string setting, string? displayLanguage) =>
        NormalizeSetting(setting) switch
        {
            ZhHans => Text("LanguageZhHans", displayLanguage),
            EnUs => Text("LanguageEnUs", displayLanguage),
            _ => Text("LanguageAuto", displayLanguage),
        };

    /// <summary>
    /// <paramref name="command"/> is nullable even though <see cref="Models.DefaultTool.Command"/> is
    /// not: <c>DefaultTools</c> is documented as user-editable via <c>config.json</c>, and the
    /// source-generated JSON context does not enforce non-null on deserialization, so a hand-edited
    /// <c>"command": null</c> reaches this method as a real null despite the C# type. Returning early
    /// avoids <c>Path.GetFileName</c> producing a null <c>key</c>, which would otherwise throw
    /// <see cref="ArgumentNullException"/> out of the <see cref="Dictionary{TKey,TValue}"/> lookup below
    /// (reference-type keys are not allowed to be null) — reachable from <c>ReloadFromConfig</c>, which
    /// runs from <c>OptionsWindow</c>'s constructor and on every <c>SettingsChanged</c> broadcast, and
    /// Avalonia has no global unhandled-exception hook to catch it.
    /// </summary>
    public static string LocalizeToolName(string? command, string? currentName, string? language)
    {
        var key = string.IsNullOrEmpty(command) ? null : Path.GetFileName(command);
        if (key is null || !BuiltInToolNames.TryGetValue(key, out var names))
            return currentName ?? "";

        if (string.IsNullOrWhiteSpace(currentName) ||
            string.Equals(currentName, names.Zh, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentName, names.En, StringComparison.OrdinalIgnoreCase))
            return Resolve(language) == ZhHans ? names.Zh : names.En;

        return currentName!;
    }
}
