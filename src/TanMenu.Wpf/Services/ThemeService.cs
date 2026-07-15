using System.IO;
using TanMenu.Core.Infrastructure;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Discovers and loads user-authored CSS themes from <c>&lt;dataRoot&gt;\themes\*.css</c>.
/// A theme id is <c>"user:&lt;filename-without-ext&gt;"</c>. wwwroot is served to the WebView
/// virtually (no loose files on disk), so a user theme can't be reached by a runtime &lt;link&gt; —
/// instead <see cref="GetCss"/> returns the file's text and <c>RetroWindow</c> injects it inline
/// as a &lt;style&gt; block. Built-in themes (WinXP/Win7/Windows11) are unaffected.
/// </summary>
public sealed class ThemeService
{
    public const string UserPrefix = "user:";
    private static readonly char[] InvalidNameChars = { '/', '\\', ':' };

    private readonly IAppDataPaths _paths;
    public ThemeService(IAppDataPaths paths) => _paths = paths;

    /// <summary>Where users drop their <c>*.css</c> theme files, under the live data root.</summary>
    public string ThemesDirectory => Path.Combine(_paths.LocalFolder, "themes");

    public static bool IsUserTheme(string? id) =>
        id is not null && id.StartsWith(UserPrefix, StringComparison.Ordinal);

    /// <summary>Create the themes folder and seed bilingual guides/templates on first run. Idempotent and
    /// never throws — themes are optional and must not break startup over an IO hiccup.</summary>
    public void EnsureSeeded()
    {
        try
        {
            Directory.CreateDirectory(ThemesDirectory);
            var readmeZh = Path.Combine(ThemesDirectory, "如何制作主题.txt");
            if (!File.Exists(readmeZh)) File.WriteAllText(readmeZh, ReadmeTextZh);
            var readmeEn = Path.Combine(ThemesDirectory, "How to make themes.txt");
            if (!File.Exists(readmeEn)) File.WriteAllText(readmeEn, ReadmeTextEn);
            var templateZh = Path.Combine(ThemesDirectory, "_模板.css");
            if (!File.Exists(templateZh)) File.WriteAllText(templateZh, TemplateCssZh);
            var templateEn = Path.Combine(ThemesDirectory, "_template.css");
            if (!File.Exists(templateEn)) File.WriteAllText(templateEn, TemplateCssEn);
        }
        catch { /* optional */ }
    }

    /// <summary>User themes for the settings dropdown: <c>(Key="user:&lt;name&gt;", Label="&lt;name&gt;")</c>.
    /// Files whose name starts with <c>_</c> (the template) are hidden.</summary>
    public IReadOnlyList<(string Key, string Label)> List()
    {
        try
        {
            if (!Directory.Exists(ThemesDirectory)) return Array.Empty<(string, string)>();
            return Directory.EnumerateFiles(ThemesDirectory, "*.css")
                .Select(p => Path.GetFileNameWithoutExtension(p)!)
                .Where(n => n.Length > 0 && n[0] != '_')
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => (UserPrefix + n, n))
                .ToList();
        }
        catch { return Array.Empty<(string, string)>(); }
    }

    /// <summary>The CSS text for a <c>"user:&lt;name&gt;"</c> id, or null when it isn't a user theme
    /// or the file is missing/unreadable. The name comes from config, so reject path traversal first.</summary>
    public string? GetCss(string? id)
    {
        if (!IsUserTheme(id)) return null;
        var name = id!.Substring(UserPrefix.Length);
        if (name.Length == 0 || name.IndexOfAny(InvalidNameChars) >= 0 || name.Contains("..")) return null;
        try
        {
            var path = Path.Combine(ThemesDirectory, name + ".css");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    private const string ReadmeTextZh =
        """
        TanMenu 自定义主题
        ==================

        把一个 .css 文件放进本文件夹（文件名不要以 _ 开头），重启 TanMenu，
        在 选项 → 外观 → 主题 下拉框里就能选到它（显示为文件名）。

        最快的做法：复制 _模板.css 改一份——里面列出了所有可改的部件。
        主题就是普通 CSS，给下面这些选择器改颜色 / 字体 / 边框即可。

        可定制的部件：
          .body .window                整个窗口（背景、字体、边框）
          .title-bar                   标题栏
          .title-bar-text              标题文字（含图标）
          .title-bar-controls button   最小化 / 关闭按钮
          .window-body                 内容区
          .tm-menubar / .tm-menuitem   顶部菜单条
          .tm-search-input             搜索框
          fieldset / legend            分组框（每个文件夹一组）
          button                       启动按钮
          .button-body / .button-text / .button-icon   按钮内部（图标 + 文字）

        说明：
        - 字体可引用 App 自带字体（绝对路径 /fonts/...）或系统已安装字体名。
        - 暂不支持主题自带图片文件，如需图片请用 data: 内嵌。
        """;

    private const string ReadmeTextEn =
        """
        TanMenu Custom Themes
        =====================

        Put a .css file in this folder. Its file name must not start with _.
        Restart TanMenu, then select it from Options -> Appearance -> Theme.
        The theme is shown by file name.

        The quickest path is to copy _template.css and edit the copy.
        A theme is plain CSS. Change colors, fonts, spacing, and borders for
        the selectors below.

        Customizable parts:
          .body .window                Whole window: background, font, border
          .title-bar                   Title bar
          .title-bar-text              Title text, including the icon
          .title-bar-controls button   Minimize / close buttons
          .window-body                 Content area
          .tm-menubar / .tm-menuitem   Top menu bar
          .tm-search-input             Search box
          fieldset / legend            Group boxes, one per folder
          button                       Launch buttons
          .button-body / .button-text / .button-icon   Button internals

        Notes:
        - Fonts can reference bundled app fonts such as /fonts/... or installed system fonts.
        - Theme-owned image files are not supported yet. Use embedded data: URLs if needed.
        """;

    private const string TemplateCssZh =
        """
        /* ===== TanMenu 主题模板：复制成 "你的主题名.css" 再改 ===== */

        /* 整个窗口：背景 / 文字色 / 字体 / 边框 */
        .body .window {
            background: #2b2b3a;
            color: #e8e8f0;
            font-family: "Segoe UI", system-ui, sans-serif;
            border: 1px solid #11111a;
            border-radius: 6px;
            box-shadow: 0 6px 24px rgba(0, 0, 0, .4);
        }

        /* 标题栏 */
        .title-bar { background: linear-gradient(#3a3a55, #2b2b3a); color: #fff; padding: 4px 6px; }
        .title-bar-text { font-weight: 600; display: flex; align-items: center; }

        /* 标题栏右侧按钮（最小化 / 关闭） */
        .title-bar-controls button {
            background: #4a4a66; color: #fff; border: none;
            width: 20px; height: 18px; margin-left: 2px; border-radius: 3px;
        }
        .title-bar-controls button:hover { background: #d04646; }

        /* 内容区 */
        .window-body { background: #2b2b3a; padding: 6px; }

        /* 顶部菜单条 */
        .tm-menubar { display: flex; gap: 2px; align-items: center; }
        .tm-menuitem { padding: 2px 8px; cursor: pointer; border-radius: 3px; }
        .tm-menuitem:hover { background: #4a4a66; }

        /* 搜索框 */
        .tm-search-input {
            margin-left: auto; background: #1f1f2e; color: #e8e8f0;
            border: 1px solid #44445e; border-radius: 4px; padding: 2px 6px;
        }

        /* 分组框（每个文件夹一组） */
        fieldset { border: 1px solid #44445e; border-radius: 6px; margin: 4px; padding: 6px; }
        legend { padding: 0 4px; color: #b8b8d0; }

        /* 启动按钮（图标 + 文字） */
        button {
            background: #3a3a55; color: #e8e8f0;
            border: 1px solid #11111a; border-radius: 5px; padding: 4px; cursor: pointer;
        }
        button:hover { background: #4a4a66; }
        .button-body { display: flex; align-items: center; gap: 6px; }
        .button-text { font-size: 12px; }
        """;

    private const string TemplateCssEn =
        """
        /* ===== TanMenu theme template: copy this as "Your Theme.css" and edit it ===== */

        /* Whole window: background / text color / font / border */
        .body .window {
            background: #2b2b3a;
            color: #e8e8f0;
            font-family: "Segoe UI", system-ui, sans-serif;
            border: 1px solid #11111a;
            border-radius: 6px;
            box-shadow: 0 6px 24px rgba(0, 0, 0, .4);
        }

        /* Title bar */
        .title-bar { background: linear-gradient(#3a3a55, #2b2b3a); color: #fff; padding: 4px 6px; }
        .title-bar-text { font-weight: 600; display: flex; align-items: center; }

        /* Title-bar buttons: minimize / close */
        .title-bar-controls button {
            background: #4a4a66; color: #fff; border: none;
            width: 20px; height: 18px; margin-left: 2px; border-radius: 3px;
        }
        .title-bar-controls button:hover { background: #d04646; }

        /* Content area */
        .window-body { background: #2b2b3a; padding: 6px; }

        /* Top menu bar */
        .tm-menubar { display: flex; gap: 2px; align-items: center; }
        .tm-menuitem { padding: 2px 8px; cursor: pointer; border-radius: 3px; }
        .tm-menuitem:hover { background: #4a4a66; }

        /* Search box */
        .tm-search-input {
            margin-left: auto; background: #1f1f2e; color: #e8e8f0;
            border: 1px solid #44445e; border-radius: 4px; padding: 2px 6px;
        }

        /* Group boxes, one per folder */
        fieldset { border: 1px solid #44445e; border-radius: 6px; margin: 4px; padding: 6px; }
        legend { padding: 0 4px; color: #b8b8d0; }

        /* Launch buttons: icon + text */
        button {
            background: #3a3a55; color: #e8e8f0;
            border: 1px solid #11111a; border-radius: 5px; padding: 4px; cursor: pointer;
        }
        button:hover { background: #4a4a66; }
        .button-body { display: flex; align-items: center; gap: 6px; }
        .button-text { font-size: 12px; }
        """;
}
