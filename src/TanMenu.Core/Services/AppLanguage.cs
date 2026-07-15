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

            ["SettingsTitle"] = ("TanMenu 设置", "TanMenu Settings"),
            ["Ok"] = ("确定", "OK"),
            ["Cancel"] = ("取消", "Cancel"),
            ["Apply"] = ("应用", "Apply"),
            ["Appearance"] = ("外观", "Appearance"),
            ["Behavior"] = ("行为", "Behavior"),
            ["Folders"] = ("文件夹", "Folders"),
            ["CommonTools"] = ("常用工具", "Common Tools"),
            ["Language"] = ("语言：", "Language:"),
            ["Theme"] = ("主题：", "Theme:"),
            ["ThemeFolder"] = ("主题文件夹…", "Theme Folder..."),
            ["ThemeFolderTooltip"] = ("打开存放自定义主题 .css 的文件夹", "Open the folder for custom .css themes"),
            ["Font"] = ("字体：", "Font:"),
            ["ColumnCount"] = ("每列按钮数：", "Buttons per column:"),
            ["ButtonSize"] = ("按钮大小：", "Button size:"),
            ["SizeSmall"] = ("小", "Small"),
            ["SizeMedium"] = ("中", "Medium"),
            ["SizeLarge"] = ("大", "Large"),
            ["DefaultFont"] = ("默认（主题字体）", "Default (theme font)"),
            ["BuiltInFonts"] = ("内置字体", "Built-in fonts"),
            ["SystemFonts"] = ("系统字体", "System fonts"),
            ["AutoClose"] = ("失焦自动隐藏", "Auto-hide when focus is lost"),
            ["TopMost"] = ("窗口置顶", "Always on top"),
            ["ShowInTaskbar"] = ("任务栏显示", "Show in taskbar"),
            ["AutoStart"] = ("开机自启", "Start with Windows"),
            ["AutoStartPortableHelp"] = ("绿色版不会写入注册表，因此不提供开机自启。", "The portable edition does not write to the registry, so startup is unavailable."),
            ["EnableHotkey"] = ("启用全局热键（呼出 / 隐藏）", "Enable global hotkey (show / hide)"),
            ["Hotkey"] = ("热键：", "Hotkey:"),
            ["HotkeyTooltip"] = ("点击后按下组合键来设置（需包含 Ctrl/Alt/Shift/Win）", "Click and press a key combo including Ctrl/Alt/Shift/Win"),
            ["Clear"] = ("清除", "Clear"),
            ["HotkeyHelp"] = ("默认无热键。点击上方输入框并按下组合键来自定义。", "No hotkey by default. Click the box above and press a key combo to customize it."),
            ["None"] = ("(无)", "(None)"),
            ["MainFolder"] = ("主文件夹", "Main Folder"),
            ["MainFolderHelp"] = ("显示该文件夹下的子文件夹（例如设为桌面，桌面上的文件夹就会成为分组）。", "Shows subfolders under this folder. For example, use Desktop to turn desktop folders into groups."),
            ["Open"] = ("打开", "Open"),
            ["Choose"] = ("选择…", "Choose..."),
            ["DataFolder"] = ("数据文件夹", "Data Folder"),
            ["DataFolderHelp"] = ("配置与缓存的存放位置（默认在“文档\\TanMenu”）。更改会立即移动数据并生效（与“应用”按钮无关）。", "Where config and caches are stored. The default is Documents\\TanMenu. Changing it moves data immediately and does not depend on Apply."),
            ["DataFolderPackagedHelp"] = ("Store/MSIX 版本使用 Windows 为应用分配的本地 AppData。可打开、清理、备份或恢复配置，但不能改到任意文件夹。", "The Store/MSIX version uses Windows-managed local app data. You can open, clear, back up, or restore config, but cannot move it to an arbitrary folder."),
            ["DataFolderPortableHelp"] = ("绿色版的配置、缓存、日志与 WebView2 数据固定保存在程序目录的 Data 文件夹中。", "The portable edition keeps config, caches, logs, and WebView2 data in the Data folder beside the app."),
            ["Change"] = ("更改…", "Change..."),
            ["ResetDefault"] = ("重置为默认", "Reset to Default"),
            ["ClearCache"] = ("清理缓存", "Clear Cache"),
            ["Backup"] = ("备份…", "Back Up..."),
            ["Restore"] = ("恢复…", "Restore..."),
            ["DataKindApp"] = ("应用数据", "App data"),
            ["DataKindPortable"] = ("绿色版", "Portable"),
            ["DataKindDefault"] = ("默认", "Default"),
            ["DataKindCustom"] = ("自定义", "Custom"),
            ["CurrentDataPending"] = ("当前：{0} · …", "Current: {0} · ..."),
            ["CurrentDataSize"] = ("当前：{0} · {1}", "Current: {0} · {1}"),
            ["PortableDataCannotChange"] = ("绿色版的数据文件夹固定为程序目录下的 Data，不能在设置中更改。", "The portable edition always uses the Data folder beside the app and cannot relocate it in Settings."),
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
            ["ChangeDataFailed"] = ("更改数据文件夹失败：{0}", "Failed to change data folder: {0}"),
            ["DataSwitchedExisting"] = ("已切换到目标文件夹中的现有数据。", "Switched to the existing data in the target folder."),
            ["DataMoved"] = ("已将当前数据移动到新文件夹。", "Moved current data to the new folder."),
            ["EffectiveNow"] = ("已立即生效。", "The change is effective immediately."),
            ["RootNotSet"] = ("主文件夹未设置或不存在。", "The main folder is not set or does not exist."),
            ["CacheCleared"] = ("缓存已清理，菜单将重新加载。", "Cache cleared. The menu will reload."),
            ["BackupConfig"] = ("备份配置", "Back Up Config"),
            ["BackupQuestion"] = ("有未应用的修改；备份的是已保存的配置（不含这些修改）。\n继续备份？", "There are unapplied changes. The backup will include saved config only, not these changes.\nContinue?"),
            ["JsonConfigFilter"] = ("JSON 配置 (*.json)|*.json", "JSON config (*.json)|*.json"),
            ["AllFilesFilter"] = ("JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*", "JSON config (*.json)|*.json|All files (*.*)|*.*"),
            ["BackupFileName"] = ("TanMenu-config-备份.json", "TanMenu-config-backup.json"),
            ["BackupSaved"] = ("配置已备份到：\n{0}", "Config backed up to:\n{0}"),
            ["BackupFailed"] = ("备份失败：{0}", "Backup failed: {0}"),
            ["RestoreConfig"] = ("从备份恢复配置", "Restore Config from Backup"),
            ["RestoreConfirm"] = ("将用所选备份覆盖当前配置（未应用的修改会丢失）。\n确定？", "The selected backup will overwrite current config. Unapplied changes will be lost.\nContinue?"),
            ["RestoreSucceeded"] = ("配置已恢复并生效。", "Config restored and applied."),
            ["RestoreInvalid"] = ("恢复失败：所选文件不是有效的 TanMenu 配置。", "Restore failed: the selected file is not a valid TanMenu config."),

            ["MenuOpenRoot"] = ("打开主目录", "Open Main Folder"),
            ["MenuOptions"] = ("选项", "Options"),
            ["MenuRefresh"] = ("刷新", "Refresh"),
            ["MenuAbout"] = ("关于", "About"),
            ["MenuExit"] = ("退出", "Exit"),
            ["SearchPlaceholder"] = ("搜索快捷方式…", "Search shortcuts..."),
            ["EmptyTitle"] = ("还没有任何快捷方式", "No shortcuts yet"),
            ["EmptyHelp"] = ("在「选项 → 文件夹」里设置主文件夹，或开启「常用工具」分类。", "Set a main folder in Options -> Folders, or enable the Common Tools group."),
            ["OpenOptions"] = ("打开选项…", "Open Options..."),
            ["LaunchFailed"] = ("无法启动：{0}", "Could not launch: {0}"),
            ["RefreshFailed"] = ("刷新失败", "Refresh failed"),
            ["ClickToDismiss"] = ("点击任意处关闭", "Click anywhere to close"),
            ["OpenFolderTooltip"] = ("打开文件夹", "Open folder"),

            ["TrayShow"] = ("显示", "Show"),
            ["TraySettings"] = ("设置", "Settings"),
            ["TrayExit"] = ("退出", "Exit"),
            ["OpenSettingsFailed"] = ("无法打开设置窗口。", "Could not open the settings window."),
            ["StartupFailed"] = ("TanMenu 启动失败：\n{0}", "TanMenu failed to start:\n{0}"),
            ["WebView2Title"] = ("缺少 WebView2 运行时", "WebView2 Runtime Missing"),
            ["WebView2Missing"] = ("运行 TanMenu 需要 Microsoft Edge WebView2 运行时。\n\n请从下面的地址下载安装后重试：\nhttps://developer.microsoft.com/microsoft-edge/webview2/", "TanMenu requires the Microsoft Edge WebView2 Runtime.\n\nInstall it from the URL below, then try again:\nhttps://developer.microsoft.com/microsoft-edge/webview2/"),
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

    public static string LocalizeToolName(string command, string? currentName, string? language)
    {
        var key = Path.GetFileName(command);
        if (!BuiltInToolNames.TryGetValue(key, out var names))
            return currentName ?? "";

        if (string.IsNullOrWhiteSpace(currentName) ||
            string.Equals(currentName, names.Zh, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentName, names.En, StringComparison.OrdinalIgnoreCase))
            return Resolve(language) == ZhHans ? names.Zh : names.En;

        return currentName!;
    }
}
