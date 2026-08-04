using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using TanMenu.Wpf.Dialogs;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 文件夹 card group: the main folder (whose subfolders become launcher groups), the data folder
/// (config + caches + logs), and a maintenance row (清理缓存 / 备份… / 恢复…).
/// <para>
/// 主文件夹 is the group's one PROPERTY, and since Round 8, Task 1 it is buffered like every other:
/// picking a folder writes the draft and 确定 / 应用 commits it. Everything else here is a COMMAND —
/// 打开, 更改数据文件夹, 重置为默认, 清理缓存, 备份, 恢复 — and commands stay immediate, because
/// <c>win-property-win</c>'s delayed-commit rule is about property CHANGES. They keep their
/// confirm-before-acting dialogs for the same reason they always had them: they move or overwrite the
/// user's real data, not just a setting.
/// </para>
/// <para>
/// The three that rewrite <c>config.json</c> underneath the surface — 更改数据文件夹, 重置为默认, 恢复 —
/// RE-SEED the draft when they complete (<see cref="SettingsDraft.Reseed"/>), or a later 确定 would copy
/// a clone taken before the relocation straight back over the file they just wrote.
/// </para>
/// <para>
/// 备份 is the fourth command the draft is visible to, and it is the only one that READS the config
/// rather than replacing it — so there is nothing to re-seed, and Round 8, Task 2 gave it a WARNING
/// instead: with a dirty draft it says the backup will contain only what is saved, and lets the user
/// go ahead or go and press 应用 first. See <see cref="OnBackupConfigClick"/> for the full reasoning.
/// </para>
/// </summary>
/// <remarks>
/// No <c>_loading</c> guard, unlike every other card group: every control here is either read-only
/// display text or a Button, and a Click (unlike SelectionChanged/IsCheckedChanged) is never re-entered
/// by <see cref="ReloadFromConfig"/> repopulating a value — there is nothing for a guard to protect
/// against. Same reasoning as <c>BehaviorCard.OnPinTaskbarClick</c>; do not "fix" this by adding one.
/// </remarks>
public partial class FoldersCard : UserControl, ISettingsCard
{
    private readonly SettingsDraft _draft;
    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly IWindowHost _host;
    private readonly ILaunchService _launch;
    private readonly IShortcutResolver _resolver;
    private readonly MenuService _menu;
    private readonly IAppDataPaths _paths;
    private readonly bool _isPackaged;
    private readonly bool _isPortable;

    /// <summary>The data folder's on-disk size as of the last <see cref="RefreshDataInfoAsync"/> run;
    /// -1 until the first measurement completes. <see cref="ReloadFromConfig"/> only ever re-labels this
    /// cached value (see <see cref="ApplyDataInfoLabel"/>) — re-measuring on every SettingsChanged (which
    /// every 确定/应用 re-fires) would walk the data folder far more often
    /// than its size could plausibly change.</summary>
    private long _dataSizeBytes = -1;

    /// <summary>Guards <see cref="RefreshDataInfoAsync"/> against stacking: a fast detach/re-attach
    /// (e.g. quick nav-rail clicks) could start a second walk before an in-flight one returns.</summary>
    private bool _dataInfoRefreshing;

    private string UiLanguage => _draft.Working.General.Language;
    private string L(string key, params object?[] args) => AppLanguage.Format(key, UiLanguage, args);

    public FoldersCard()
    {
        InitializeComponent();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _events = App.Services.GetRequiredService<AppEvents>();
        _host = App.Services.GetRequiredService<IWindowHost>();
        _launch = App.Services.GetRequiredService<ILaunchService>();
        _resolver = App.Services.GetRequiredService<IShortcutResolver>();
        _menu = App.Services.GetRequiredService<MenuService>();
        _paths = App.Services.GetRequiredService<IAppDataPaths>();
        _isPackaged = PackageRuntime.HasPackageIdentity;
        _isPortable = App.IsPortable;

        // The data-folder size query itself is kicked off from OnAttachedToVisualTree, not here — a
        // constructor-only query would go stale for the rest of the session (see its remarks).
    }

    /// <summary>The data folder can change size — or even be moved — entirely outside this app (files
    /// dropped in via Explorer, another running instance relocating it), so the size this card shows is
    /// only trustworthy at the moment it was measured. Hooking the ATTACH rather than a window's
    /// <c>Opened</c> is what has survived three different containers unchanged: <c>SettingsWindow</c>
    /// had an <c>Opened</c> per visit, the cached <c>SettingsPage</c> had none and was detached and
    /// re-attached by every navigation between 应用 and 设置, and Round 8, Task 2's
    /// <c>OptionsWindow</c> is built fresh per open with its <c>TabControl</c> realising this card's tab
    /// when it is first selected. Re-measure here, on every re-attach including the first. Same pattern
    /// as <c>BehaviorCard.RefreshTaskbarPinStateAsync</c>.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RunSafely(RefreshDataInfoAsync);
    }

    /// <summary>Populate every control from the draft's working clone. Cheap and side-effect-free: it
    /// never walks the data folder (see <see cref="ApplyDataInfoLabel"/>) and never shows a dialog, so it
    /// is always safe to call from <c>OptionsWindow.ApplyText</c>'s SettingsChanged handler.</summary>
    public void ReloadFromConfig()
    {
        var lang = UiLanguage;

        GroupHead.Text = AppLanguage.Text("Folders", lang);

        MainFolderTitle.Text = AppLanguage.Text("MainFolder", lang);
        MainFolderSub.Text = AppLanguage.Text("MainFolderHelp", lang);
        OpenRootBtn.Content = AppLanguage.Text("Open", lang);
        PickRootBtn.Content = AppLanguage.Text("Choose", lang);
        RootPathText.Text = _draft.Working.RootFolder;
        ToolTip.SetTip(RootPathText, _draft.Working.RootFolder); // full path on hover; the label itself ellipsizes

        DataFolderTitle.Text = AppLanguage.Text("DataFolder", lang);
        DataFolderSub.Text = _isPortable
            ? AppLanguage.Text("DataFolderPortableHelp", lang)
            : _isPackaged
                ? AppLanguage.Text("DataFolderPackagedHelp", lang)
                : AppLanguage.Text("DataFolderHelp", lang);
        OpenDataBtn.Content = AppLanguage.Text("Open", lang);
        ChangeDataBtn.Content = AppLanguage.Text("Change", lang);
        ChangeDataBtn.IsEnabled = !_isPortable && !_isPackaged;
        ResetDataBtn.Content = AppLanguage.Text("ResetDefault", lang);
        ApplyDataInfoLabel(); // re-label from the last measurement; ResetDataBtn.IsEnabled is set inside

        MaintenanceTitle.Text = AppLanguage.Text("Maintenance", lang);
        MaintenanceSub.Text = AppLanguage.Text("MaintenanceCardSub", lang);
        ClearCacheBtn.Content = AppLanguage.Text("ClearCache", lang);
        BackupBtn.Content = AppLanguage.Text("Backup", lang);
        RestoreBtn.Content = AppLanguage.Text("Restore", lang);
    }

    /// <summary>Re-render the data-folder path/kind/size lines from <see cref="_dataSizeBytes"/> (the
    /// last measurement <see cref="RefreshDataInfoAsync"/> took) without touching the disk.</summary>
    private void ApplyDataInfoLabel()
    {
        var lang = UiLanguage;
        var root = _paths.LocalFolder;
        var isDefault = !_isPackaged && !_isPortable && DataLocation.IsDefaultLocation(root);
        var kind = _isPortable
            ? AppLanguage.Text("DataKindPortable", lang)
            : _isPackaged
                ? AppLanguage.Text("DataKindApp", lang)
                : isDefault ? AppLanguage.Text("DataKindDefault", lang) : AppLanguage.Text("DataKindCustom", lang);

        ResetDataBtn.IsEnabled = !_isPackaged && !_isPortable && !isDefault;
        DataPathText.Text = root;
        ToolTip.SetTip(DataPathText, root); // full path on hover; the label itself ellipsizes
        DataInfoText.Text = _dataSizeBytes < 0
            ? AppLanguage.Format("CurrentDataPending", lang, kind)
            : AppLanguage.Format("CurrentDataSize", lang, kind, FormatSize(_dataSizeBytes));
    }

    /// <summary>Re-measure the data folder's on-disk size and re-apply the path/kind/size lines. The
    /// cached walk is dropped unconditionally (see <see cref="DataLocation.GetFolderSizeCached"/>) rather
    /// than trusted from a previous visit — see <see cref="OnAttachedToVisualTree"/>'s remarks.</summary>
    private async Task RefreshDataInfoAsync()
    {
        if (_dataInfoRefreshing)
            return; // a query from a very-close attach/re-attach (or an explicit caller) is already in flight

        _dataInfoRefreshing = true;
        try
        {
            var root = _paths.LocalFolder;
            DataLocation.InvalidateFolderSizeCache();
            var bytes = await Task.Run(() => DataLocation.GetFolderSizeCached(root));

            if (!this.IsAttachedToVisualTree())
                return; // detached while the walk was running; the next re-attach measures again

            _dataSizeBytes = bytes;
            ApplyDataInfoLabel();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "刷新数据文件夹大小失败");
        }
        finally
        {
            _dataInfoRefreshing = false;
        }
    }

    /// <summary>Re-read the data-folder size after a delay (fire-and-forget). Swallows faults — incl. a
    /// write to a since-closed page — so they never surface as an unobserved task fault.</summary>
    private async Task RefreshSizeLabelAfterAsync(int delayMs)
    {
        try
        {
            await Task.Delay(delayMs);
            await RefreshDataInfoAsync();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "延迟刷新数据大小标签失败"); }
    }

    // ---- 主文件夹 ----

    // Opens what the ROW SHOWS, which under delayed commit is the draft's folder rather than the
    // committed one — 打开 is a command against the value in front of the user, and a folder that has
    // been picked but not yet applied is still the one they just chose.
    private void OnOpenRootClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        var folder = _draft.Working.RootFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await ShowInfoAsync(L("RootNotSet"));
            return;
        }
        _launch.OpenFolder(folder);
    });

    private void OnPickRootClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        var options = new FolderPickerOpenOptions { Title = L("SelectRootFolder"), AllowMultiple = false };
        var current = _draft.Working.RootFolder;
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not null)
                options.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(current);
        }

        var picked = await PickFolderAsync(options);
        if (picked is null)
            return;

        // Buffered like every other property on this surface: AppPage rebuilds its categories when
        // SettingsChanged fires, and that is now 确定 / 应用, not this click.
        _draft.Working.RootFolder = picked;
        RootPathText.Text = picked; // the picker has closed; show the pending choice straight away
        ToolTip.SetTip(RootPathText, picked);
        _draft.Revalidate();
    });

    // ---- 数据文件夹 ----

    private void OnOpenDataClick(object? sender, RoutedEventArgs e) => _launch.OpenFolder(_paths.LocalFolder);

    private void OnChangeDataClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        // Defence in depth: this button is already disabled in portable/packaged builds (ReloadFromConfig).
        if (_isPortable)
        {
            await ShowInfoAsync(L("PortableDataCannotChange"));
            return;
        }

        if (_isPackaged)
        {
            await ShowInfoAsync(L("PackagedDataCannotChange"));
            return;
        }

        var picked = await PickFolderAsync(new FolderPickerOpenOptions
        {
            Title = L("SelectDataFolder"),
            AllowMultiple = false,
        });
        if (picked is null)
            return;

        // 更改数据文件夹 moves/adopts the user's real data — confirm before acting, like 重置为默认 and 恢复.
        if (!await ConfirmAsync(L("ChangeDataConfirm", picked), MessageBoxIcon.Warning))
            return;

        await RelocateDataTo(picked);
    });

    private void OnResetDataClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (_isPortable)
        {
            await ShowInfoAsync(L("PortableDataCannotChange"));
            return;
        }

        if (_isPackaged)
        {
            await ShowInfoAsync(L("PackagedDataCannotReset"));
            return;
        }

        if (DataLocation.IsDefaultLocation(_paths.LocalFolder))
        {
            await ShowInfoAsync(L("DataAlreadyDefault"));
            return;
        }

        var def = DataLocation.DefaultRoot;
        if (File.Exists(Path.Combine(def, "config.json")))
        {
            // The default location already has a (leftover) config — don't silently adopt it; let the
            // user choose between using it and overwriting it with the current data.
            var r = await ShowDialogAsync(L("DefaultDataExists", def), L("ResetToDefault"),
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == MessageBoxResult.Cancel)
                return;
            if (r == MessageBoxResult.No)
            {
                // Overwrite: remove the leftover config so RelocateDataTo MOVES the current data in.
                try { File.Delete(Path.Combine(def, "config.json")); }
                catch (Exception ex)
                {
                    await ShowErrorAsync(L("UnableCleanDefaultConfig", ex.Message));
                    return;
                }
            }
            await RelocateDataTo(def);
            return;
        }

        if (!await ConfirmAsync(L("MoveDataConfirm", def), MessageBoxIcon.Question, title: L("ResetToDefault")))
            return;
        await RelocateDataTo(def);
    });

    /// <summary>Move/adopt the data folder at <paramref name="folder"/>, re-point the live paths,
    /// reload + rebind, and notify the launcher. Shared by 更改… and 重置为默认 — both already confirmed
    /// with the user before calling this.</summary>
    private async Task RelocateDataTo(string folder)
    {
        bool changed;
        bool usedExisting;
        try
        {
            changed = DataLocation.Relocate(folder, _paths.LocalFolder, out usedExisting, UiLanguage);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("ChangeDataFailed", ex.Message));
            return;
        }

        if (!changed)
            return; // same folder

        // Re-point the live data paths, reload config from the new location, rebind, and notify.
        if (_paths is MutableAppDataPaths mutablePaths)
            mutablePaths.SetRoot(folder);
        DataLocation.InvalidateFolderSizeCache(); // data root changed → drop the cached size for the old root
        await _config.LoadAsync();

        // The config that was just loaded is a DIFFERENT file from the one the draft cloned, so the
        // draft has to be re-taken before anything reads it again — otherwise a later 确定 would copy
        // the pre-relocation clone straight back over the config in the new location.
        _draft.Reseed();
        _events.RaiseSettingsChanged(); // reload every card, incl. this one, from the re-seeded draft

        await RefreshDataInfoAsync(); // measure the NEW root's actual size now, not just re-label the old one

        var msg = usedExisting ? L("DataSwitchedExisting") : L("DataMoved");
        await ShowInfoAsync(msg + "\n\n" + L("EffectiveNow"));
    }

    // ---- 维护 ----

    private void OnClearCacheClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        _resolver.ClearCache();
        _menu.ClearIconCache();
        _events.RaiseSettingsChanged(); // rebuild the menu: re-resolve links + re-extract icons

        // The link-cache file was just deleted, so the on-disk size shrank — measure and show it now,
        // then again after the delay below once the rebuild's ~800ms-debounced timer has written it back.
        await RefreshDataInfoAsync();
        _ = RefreshSizeLabelAfterAsync(1800);

        await ShowInfoAsync(L("CacheCleared"));
    });

    private void OnBackupConfigClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        // 备份 writes ConfigService.Config — what is COMMITTED, not what the draft is holding. Round 8,
        // Task 1 re-opened the staged/unsaved gap (the comment here used to say there was none, which
        // was accurate for the immediate-apply page it was written for and stopped being so), left it
        // unguarded because it had no prompt machinery, and handed it to Task 2 in its report.
        //
        // ROUND 8, TASK 2 CLOSES IT, and closes it with the mechanism §7 of the design already
        // specifies for the other boundary a dirty draft is invisible at: 取消 and the title-bar ×
        // prompt with a MessageBox.YesNo when draft.IsDirty, bracketed by SuppressHide. This is that
        // same prompt, at this boundary. It is a WARNING and not a block: a backup of the committed
        // config is a legitimate thing to want, and the point is that the user should not learn which
        // one they took by restoring it later. The command itself stays IMMEDIATE — the design's
        // command list keeps it so — and nothing here writes the draft.
        //
        // The other two rows around it need no such warning and neither is an omission: 恢复 and
        // 更改数据文件夹 REPLACE the config underneath the surface, so they re-seed the draft when they
        // complete, which is a stronger answer than a warning. 备份 only reads, so there is nothing to
        // re-seed and nothing to undo — only something to say.
        // The COMMITTED language, not this card's own L() — which reads the draft. Everything a card
        // DISPLAYS comes from the draft and should; a prompt ABOUT the draft appears over a window
        // whose labels are still in the committed language (ApplyText only re-labels on a commit), and
        // MessageBox resolves its own 确定/取消 from ConfigService regardless. Reading the draft here
        // would produce a half-English, half-Chinese box the moment the pending edit IS the language.
        if (_draft.IsDirty &&
            !await ConfirmAsync(
                AppLanguage.Text("BackupPendingChanges", _config.Config.General.Language),
                MessageBoxIcon.Warning))
            return;

        var path = await PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = L("BackupConfig"),
            SuggestedFileName = L("BackupFileName"),
            DefaultExtension = "json",
            FileTypeChoices = ParseFilter(L("JsonConfigFilter")),
        });
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            await _config.BackupToAsync(path);
            await ShowInfoAsync(L("BackupSaved", path));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("BackupFailed", ex.Message));
        }
    });

    private void OnRestoreConfigClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        var path = await PickOpenFileAsync(new FilePickerOpenOptions
        {
            Title = L("RestoreConfig"),
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(L("AllFilesFilter")),
        });
        if (string.IsNullOrEmpty(path))
            return;

        // 恢复 overwrites the live config wholesale — confirm before acting, like 更改数据文件夹 and 重置为默认.
        // The dialog's title is the literal product name, not the picker's title (ported verbatim).
        if (!await ConfirmAsync(L("RestoreConfirm"), MessageBoxIcon.Warning))
            return;

        if (await _config.RestoreFromAsync(path))
        {
            _draft.Reseed(); // the live config was replaced wholesale; the pre-restore clone is stale
            _events.RaiseSettingsChanged(); // reload every card, incl. this one, from the restored config
            await ShowInfoAsync(L("RestoreSucceeded"));
        }
        else
        {
            // An IO failure and an invalid file deliberately produce the same message.
            await ShowErrorAsync(L("RestoreInvalid"));
        }
    });

    // ---- Shared plumbing ----

    /// <summary>Run a folder picker and return the chosen local path, or null if cancelled / the chosen
    /// item has no filesystem path (a shell location such as "This PC"). Brackets the picker with
    /// SuppressHide — it takes focus, and a user with 失焦自动隐藏 on would otherwise lose the window.</summary>
    private async Task<string?> PickFolderAsync(FolderPickerOpenOptions options)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        _host.SuppressHide(true);
        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
            if (folders.Count == 0)
                return null;
            var path = folders[0].TryGetLocalPath();
            return string.IsNullOrEmpty(path) ? null : path;
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    /// <summary>Run a save-file picker and return the chosen local path, or null if cancelled. Brackets
    /// with SuppressHide for the same reason as <see cref="PickFolderAsync"/>.</summary>
    private async Task<string?> PickSaveFileAsync(FilePickerSaveOptions options)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        _host.SuppressHide(true);
        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
            return file?.TryGetLocalPath();
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    /// <summary>Run an open-file picker and return the chosen local path, or null if cancelled. Brackets
    /// with SuppressHide for the same reason as <see cref="PickFolderAsync"/>.</summary>
    private async Task<string?> PickOpenFileAsync(FilePickerOpenOptions options)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        _host.SuppressHide(true);
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    /// <summary>Turn a legacy Win32 filter resource ("名称 (*.json)|*.json|所有文件 (*.*)|*.*") into
    /// Avalonia's structured picker types, so the localized filter strings keep working verbatim.</summary>
    private static List<FilePickerFileType> ParseFilter(string filter)
    {
        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new FilePickerFileType(parts[i].Trim())
            {
                Patterns = parts[i + 1].Split(';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
        }
        return types;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024):0.#} MB";
    }

    /// <summary>Show a modal message box, bracketed with SuppressHide — it takes focus, and a user with
    /// 失焦自动隐藏 on would otherwise lose the window out from under the dialog. Every info/error/confirm
    /// helper below routes through this one method so the bracket can never be forgotten at a call site.</summary>
    private async Task<MessageBoxResult> ShowDialogAsync(string message, string title,
        MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        _host.SuppressHide(true);
        try
        {
            return await MessageBox.ShowAsync(TopLevel.GetTopLevel(this) as Window, message, title, buttons, icon);
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    private Task<MessageBoxResult> ShowInfoAsync(string message) =>
        ShowDialogAsync(message, "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Info);

    private Task<MessageBoxResult> ShowErrorAsync(string message) =>
        ShowDialogAsync(message, "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Error);

    private async Task<bool> ConfirmAsync(string message, MessageBoxIcon icon, string? title = null) =>
        await ShowDialogAsync(message, title ?? "TanMenu", MessageBoxButtons.OkCancel, icon) == MessageBoxResult.Ok;

    /// <summary>Run an async click handler without letting a fault escape the event — same rationale as
    /// <c>SettingsWindow.RunSafely</c> / <c>BehaviorCard.RunSafely</c>: Avalonia has no WPF-style global
    /// DispatcherUnhandledException hook, so an escaping async-void fault would tear the process down,
    /// and logging is the floor.</summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "文件夹设置操作失败"); }
    }
}
