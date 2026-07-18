using System.Collections.Concurrent;
using System.IO;
using TanMenu.Core.Models;
using TanMenu.Core.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Bridges Core's <see cref="MenuDataService"/> (which yields <c>IconKey</c> paths) to the
/// retro Blazor UI's view models (which bind <c>IconBase64</c>), encoding icons via
/// <see cref="IIconProvider"/>. Base64 lives in the UI layer only; Core stays icon-format-agnostic.
///
/// The folder scan + .lnk resolution + icon extraction run on ONE long-lived STA worker thread
/// (CShellLink and SHGetFileInfo are apartment-threaded shell COM), so a warm apartment is reused
/// across summons instead of paying thread + COM-apartment spin-up per build. <see cref="BuildMenuAsync"/>
/// is gated by a cheap folder/tools fingerprint so an unchanged re-summon does no rescan at all; icons
/// that miss the cache are filled asynchronously after the structure renders (<see cref="ExtractIconsAsync"/>).
/// </summary>
public sealed class MenuService : IDisposable
{
    private readonly MenuDataService _data;
    private readonly IIconProvider _icons;

    /// <summary>The standard fallback icon (base64 PNG) for items with no extractable icon — the
    /// Windows stock application icon, or the bundled flat-file icon if that can't be obtained.</summary>
    private readonly Lazy<string> _fallbackIcon;

    /// <summary>Last-resort bundled fallback icon (flat "generic file" 48×48 PNG, base64), used only
    /// when the Windows stock application icon (the primary no-icon fallback, see <see cref="_fallbackIcon"/>)
    /// can't be obtained.</summary>
    private const string DefaultIconBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAAFiUAABYlAUlSJPAAAACrSURBVGhD7dM7DsMgFERRL5s1sRLWRJNUbkYkT9jv40T3StMCp+A4iIh+ptba6+70zNT0MVen56Z1PmDOeXmlCA/AGKMO4QUoQ3gCShDegHREBCAVEQVIQ3gAvi0cAcAYAKu/BPTebw3ATiuA5wBYATBWAtBPuTsAO60AngNgBcBYCUA/5e4A7LQCeA6AFQBjJQD9lJ+mj10NgNV5QfT0Xrf0oqjpvUREz+0N2/Xp7Z0GKaEAAAAASUVORK5CYII=";

    /// <summary>Default display name of the built-in common-tools group.</summary>
    public const string DefaultToolsGroupName = "常用工具";

    public MenuService(MenuDataService data, IIconProvider icons)
    {
        _data = data;
        _icons = icons;
        _fallbackIcon = new Lazy<string>(() =>
        {
            var bytes = _icons.GetDefaultAppIconPngBytes();
            return bytes is { Length: > 0 } ? Convert.ToBase64String(bytes) : DefaultIconBase64;
        });
    }

    // ---- Icon cache ------------------------------------------------------------------------------
    // Cache the raw PNG BYTES (not the base64 string): base64 inflates the bytes ~33% AND is held as
    // UTF-16, so storing bytes uses ~2.6× less resident memory for this tray-resident singleton. Keyed
    // by icon path, validated by (mtime, size) so an unchanged file is never re-extracted; many
    // shortcuts that target the same exe share one entry. Encoding to base64 happens on the (infrequent,
    // fingerprint-gated) build, not on every summon.
    private readonly ConcurrentDictionary<string, (DateTime Mtime, long Size, byte[] Png)> _iconCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Bumped by ClearIconCache and folded into the build fingerprint. "Clear cache" doesn't touch any
    // folder/tools mtime, so without this the unchanged-folders gate would skip the rebuild and the
    // cleared caches would never be re-populated (icons/links wouldn't visibly refresh).
    private int _cacheGeneration;

    // Count of builds enqueued-but-not-yet-started. A running icon fill checks this between batches and
    // yields (re-queuing its remaining icons) so a settings-change/refresh isn't stuck behind a long icon
    // walk on the single STA worker. Incremented on the UI thread before enqueue, decremented when the
    // build job starts on the STA worker.
    private int _pendingBuilds;

    /// <summary>Drop ALL derived caches so the next build re-extracts/re-resolves everything (settings
    /// "clear cache"; the .lnk resolver cache is cleared separately via IShortcutResolver). Bumps a
    /// generation counter fed into the build fingerprint so the clear actually forces a rebuild instead
    /// of being short-circuited by the unchanged-folders gate.</summary>
    public void ClearIconCache()
    {
        _iconCache.Clear();
        _commandPathCache.Clear();   // re-resolve command→path so a newly-installed tool resolves next build
        _bundledIconCache.Clear();   // re-read bundled tool icons
        Interlocked.Increment(ref _cacheGeneration);
    }

    /// <summary>Base64 for an icon path IF it's cached and unchanged; null if it must be extracted.
    /// Empty/static keys (folders with no key, unresolved items) return the stock fallback directly.</summary>
    private string? CachedIconBase64(string? iconKey, DateTime mtime, long size)
    {
        if (string.IsNullOrEmpty(iconKey))
            return _fallbackIcon.Value;
        if (_iconCache.TryGetValue(iconKey, out var hit) && hit.Mtime == mtime && hit.Size == size)
            return Convert.ToBase64String(hit.Png);
        return null;
    }

    /// <summary>Extract + cache + base64-encode an icon (called on the STA worker during the async fill).
    /// Only SUCCESSFUL extractions are cached — a transient failure returns the fallback uncached so the
    /// next build retries, rather than pinning the fallback under this (mtime,size) key (directories /
    /// bare commands key on (default,0), which never changes).</summary>
    private string ExtractIconBase64(string iconKey, DateTime mtime, long size)
    {
        var bytes = _icons.GetIconPngBytes(iconKey);
        if (bytes is not { Length: > 0 })
            return _fallbackIcon.Value;
        _iconCache[iconKey] = (mtime, size, bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Assign <paramref name="vm"/>'s icon from cache; on a miss set the fallback placeholder
    /// and queue the item for async extraction. Records the (non-empty) icon key in
    /// <paramref name="usedKeys"/> so the build can evict cache entries the current menu no longer uses.</summary>
    private void AssignIcon(MenuItemVm vm, string? iconKey, DateTime mtime, long size,
        List<PendingIcon> pending, HashSet<string> usedKeys)
    {
        if (!string.IsNullOrEmpty(iconKey))
            usedKeys.Add(iconKey);
        var cached = CachedIconBase64(iconKey, mtime, size);
        if (cached != null)
        {
            vm.IconBase64 = cached;
            return;
        }
        vm.IconBase64 = _fallbackIcon.Value;                     // placeholder until the fill pass lands
        pending.Add(new PendingIcon(vm, iconKey!, mtime, size)); // iconKey is non-null on a cache miss
    }

    // ---- Bundled tool icons (Store-app aliases) --------------------------------------------------
    // Store-app aliases (e.g. mspaint.exe on Win11) resolve to a 0-byte App Execution Alias stub with no
    // embedded icon, so the shell only yields a generic icon. For these known commands we ship a real
    // icon under wwwroot/tool-icons (copied next to the exe by the csproj, like the sounds/fonts).
    private static readonly Dictionary<string, string> _bundledToolIcons =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mspaint.exe"] = "mspaint.png",
            ["calc.exe"] = "calc.png",
        };

    private readonly ConcurrentDictionary<string, string?> _bundledIconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Base64 of the bundled icon shipped for <paramref name="command"/>, or null if none is
    /// shipped (or the file is missing) — in which case the caller falls back to live extraction.</summary>
    private string? BundledToolIcon(string command)
    {
        if (string.IsNullOrWhiteSpace(command) ||
            !_bundledToolIcons.TryGetValue(Path.GetFileName(command), out var file))
            return null;
        return _bundledIconCache.GetOrAdd(file, f =>
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "tool-icons", f);
                return File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;
            }
            catch { return null; }
        });
    }

    // ---- Command resolution ----------------------------------------------------------------------
    // Command→resolved-path is stable for the session (PATH rarely changes), so cache it: a re-summon
    // then does zero System32/PATH probing for unchanged tools instead of a File.Exists per PATH entry.
    // Instance field (not static) so its lifetime tracks this singleton and ClearIconCache empties it
    // along with the other derived caches.
    private readonly ConcurrentDictionary<string, string> _commandPathCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve a bare exe/command to a full path (System32, then PATH); returns the command
    /// unchanged if not found (LaunchService then lets the shell resolve it via App Paths/PATH).</summary>
    private string ResolveCommandPath(string command)
    {
        if (_commandPathCache.TryGetValue(command, out var cached))
            return cached;
        var resolved = ResolveCommandPathUncached(command);
        // Cache only a SUCCESSFUL resolution (a different, found path). A not-found result returns the
        // bare command unchanged; caching THAT would pin the fallback icon + unresolved FullPath even
        // after the tool is later installed, because ordinary rebuilds never clear _commandPathCache
        // (only 清理缓存 does). Leaving misses uncached lets a later rebuild re-probe and pick them up.
        if (!string.Equals(resolved, command, StringComparison.OrdinalIgnoreCase))
            _commandPathCache[command] = resolved;
        return resolved;
    }

    private static string ResolveCommandPathUncached(string command)
    {
        try
        {
            if (Path.IsPathRooted(command))
                return command;

            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), command);
            if (File.Exists(sys))
                return sys;

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var candidate = Path.Combine(dir, command);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch { /* fall through */ }
        return command;
    }

    // ---- Build -----------------------------------------------------------------------------------

    /// <summary>One launcher item still needing its real icon extracted (its VM currently shows the
    /// fallback placeholder).</summary>
    public readonly record struct PendingIcon(MenuItemVm Item, string IconKey, DateTime Mtime, long Size);

    /// <summary>Result of a menu build. <see cref="Changed"/> is false when the fingerprint matched the
    /// caller's last one — the caller then keeps its current groups untouched (no rescan happened).</summary>
    public sealed record MenuBuildResult(bool Changed, List<MenuGroupVm> Groups, int Fingerprint, List<PendingIcon> Pending);

    /// <summary>Build the menu (built-in tools group + one group per immediate subfolder of the root) on
    /// the STA worker. Skips the whole rescan and returns <c>Changed=false</c> when the folder/tools
    /// fingerprint equals <paramref name="lastFingerprint"/> (pass null to force a rebuild). Cached icons
    /// are filled inline; cache misses get the fallback placeholder and are returned in
    /// <see cref="MenuBuildResult.Pending"/> for <see cref="ExtractIconsAsync"/>.</summary>
    public Task<MenuBuildResult> BuildMenuAsync(string? rootFolder, GeneralConfig general, int? lastFingerprint)
    {
        // Snapshot the config bits the STA thread needs so it never reads config state the UI thread
        // might swap concurrently (CommitAsync replaces config.General wholesale).
        var showTools = general.ShowDefaultTools;
        var tools = general.DefaultTools is { } dt ? new List<DefaultTool>(dt) : new List<DefaultTool>();
        var language = general.Language;

        // Signal any in-flight icon fill to yield so this build isn't stuck behind it on the STA worker.
        Interlocked.Increment(ref _pendingBuilds);

        // Pair the increment with EXACTLY ONE decrement, whether the work delegate runs (it decrements at
        // its first line) or never enqueues (compensated after RunStaAsync). task.IsFaulted alone can't
        // tell a failed enqueue from a work-body throw that already decremented, so this Exchange flag
        // makes the decrement idempotent across the STA worker and this UI thread — preventing a permanent
        // negative skew that would disable the fill-yield gate for the rest of the process.
        int decremented = 0;
        var task = RunStaAsync(() =>
        {
            if (Interlocked.Exchange(ref decremented, 1) == 0)
                Interlocked.Decrement(ref _pendingBuilds);

            var fingerprint = ComputeFingerprint(rootFolder, showTools, tools, language);
            if (lastFingerprint is int last && last == fingerprint)
                return new MenuBuildResult(false, new List<MenuGroupVm>(), fingerprint, new List<PendingIcon>());

            var pending = new List<PendingIcon>();
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = new List<MenuGroupVm>();

            if (showTools)
            {
                var toolGroup = BuildDefaultToolsGroup(tools, pending, usedKeys, language);
                if (toolGroup.Items.Count > 0)
                    groups.Add(toolGroup);
            }

            var contents = _data.GetDirectoryContents(SafeGetDirectories(rootFolder));
            var folderGroups = new List<MenuGroupVm>();
            foreach (var c in contents)
            {
                var items = new List<MenuItemVm>(c.Items.Count);
                foreach (var it in c.Items)
                {
                    var vm = new MenuItemVm
                    {
                        Name = it.Name,
                        FullPath = it.FullPath,
                        TargetPath = it.TargetPath,
                        IsDirectory = it.IsDirectory,
                        IsDisabled = it.IsDisabled,
                    };
                    AssignIcon(vm, it.IconKey, it.IconMtime, it.IconSize, pending, usedKeys);
                    items.Add(vm);
                }
                folderGroups.Add(new MenuGroupVm { Directory = c.Directory, DirectoryName = c.DirectoryName, Items = items });
            }

            // Categories (folder groups) sorted by display name — culture-aware so Chinese folder names
            // order the way the OS file explorer would. The built-in 常用工具 group (added above) stays first.
            folderGroups.Sort((a, b) =>
                string.Compare(a.DirectoryName, b.DirectoryName, StringComparison.CurrentCultureIgnoreCase));
            groups.AddRange(folderGroups);

            // Evict icon-cache entries the current menu no longer references (deleted/renamed shortcuts
            // and their targets), so a long tray session over a churning desktop doesn't grow _iconCache
            // without bound. Pending (cache-miss) keys are in usedKeys too, so the async fill still
            // populates them afterwards on this same STA worker (it runs strictly after this build).
            // No count guard: usedKeys also counts not-yet-cached pending misses, so a Count comparison
            // could skip eviction while a stale entry still exists. The loop is cheap (a few hundred keys).
            foreach (var key in _iconCache.Keys) // ConcurrentDictionary.Keys is a snapshot — safe to mutate during
                if (!usedKeys.Contains(key))
                    _iconCache.TryRemove(key, out _);

            return new MenuBuildResult(true, groups, fingerprint, pending);
        });

        // If the job failed to enqueue (Add after CompleteAdding at shutdown, or Thread.Start threw), the
        // work delegate never ran and never decremented — compensate here, but only if the delegate
        // hasn't already done it (the Exchange guard prevents a double-decrement when the job DID run and
        // then threw, which also faults the task).
        if (task.IsFaulted && Interlocked.Exchange(ref decremented, 1) == 0)
            Interlocked.Decrement(ref _pendingBuilds);
        return task;
    }

    /// <summary>Extract the real icons for the pending items on the STA worker, invoking
    /// <paramref name="onBatch"/> as batches complete so the UI can light them up progressively. The
    /// callback is responsible for marshaling to its own render thread.</summary>
    public Task ExtractIconsAsync(IReadOnlyList<PendingIcon> pending,
        Action<IReadOnlyList<(MenuItemVm Item, string Base64)>> onBatch, Func<bool> isStale)
        => ExtractIconsFromAsync(pending, 0, onBatch, isStale);

    private Task ExtractIconsFromAsync(IReadOnlyList<PendingIcon> pending, int start,
        Action<IReadOnlyList<(MenuItemVm Item, string Base64)>> onBatch, Func<bool> isStale)
    {
        return RunStaAsync<object?>(() =>
        {
            const int batchSize = 12;
            var batch = new List<(MenuItemVm, string)>(batchSize);
            int i = start;
            while (i < pending.Count)
            {
                // A newer build's fill has superseded this one — stop. Its remaining icons are for a menu
                // no longer shown, so continuing would be wasted STA shell-COM work AND would re-insert
                // _iconCache keys the newer build's eviction already removed (bounded over-retention).
                if (isStale())
                    return null;
                var p = pending[i++];
                var b64 = ExtractIconBase64(p.IconKey, p.Mtime, p.Size);
                batch.Add((p.Item, b64));
                if (batch.Count >= batchSize)
                {
                    onBatch(batch);
                    batch = new List<(MenuItemVm, string)>(batchSize);
                    // Yield to a waiting build (settings-change / refresh) rather than blocking it behind a
                    // long icon walk; re-queue the remaining icons to run AFTER the build so none are lost.
                    if (Volatile.Read(ref _pendingBuilds) > 0)
                        break;
                }
            }
            if (batch.Count > 0)
                onBatch(batch);
            if (i < pending.Count && !isStale())
                // Observe the re-queued continuation's faults locally: the OUTER task (the one
                // StartIconFill observes) completes successfully here, so its observer can't reach this
                // re-queued tail. If the tail's enqueue races Dispose's CompleteAdding, RunStaAsync faults
                // its task — swallow it so it never surfaces as a process-wide UnobservedTaskException.
                _ = ExtractIconsFromAsync(pending, i, onBatch, isStale)
                    .ContinueWith(static t => { _ = t.Exception; },
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return null;
        });
    }

    /// <summary>Build the built-in "常用工具" group from the configured tools (only the shown ones), on
    /// the STA worker. Bundled icons are used directly; other tools extract their resolved exe's icon
    /// (queued into <paramref name="pending"/> if uncached).</summary>
    private MenuGroupVm BuildDefaultToolsGroup(IEnumerable<DefaultTool> tools, List<PendingIcon> pending,
        HashSet<string> usedKeys, string? language)
    {
        var items = new List<MenuItemVm>();
        foreach (var t in tools)
        {
            if (!t.Show || string.IsNullOrWhiteSpace(t.Command))
                continue;

            var resolved = ResolveCommandPath(t.Command);
            var exists = File.Exists(resolved);
            var vm = new MenuItemVm
            {
                Name = AppLanguage.LocalizeToolName(t.Command, t.Name, language),
                // Prefer the resolved full path; the shell still resolves a bare command otherwise.
                FullPath = exists ? resolved : t.Command,
                IsDirectory = false,
                IsDisabled = false,
            };

            // A bundled icon (for Store-app aliases like mspaint.exe) wins over the generic icon the
            // alias stub would yield; everything else extracts its icon normally.
            var bundled = BundledToolIcon(t.Command);
            if (bundled != null)
            {
                vm.IconBase64 = bundled;
            }
            else if (exists)
            {
                DateTime mtime = default;
                long size = 0;
                try { var fi = new FileInfo(resolved); if (fi.Exists) { mtime = fi.LastWriteTimeUtc; size = fi.Length; } }
                catch { /* keep zero → stable key */ }
                AssignIcon(vm, resolved, mtime, size, pending, usedKeys);
            }
            else
            {
                vm.IconBase64 = _fallbackIcon.Value; // unresolved bare command → stock icon
            }
            items.Add(vm);
        }
        // Directory left empty → the group title isn't a clickable "open folder".
        return new MenuGroupVm { Directory = "", DirectoryName = AppLanguage.Text("CommonTools", language), Items = items };
    }

    /// <summary>Cheap change fingerprint: the root path + each immediate subfolder's name and
    /// last-write-time (adding/removing/renaming a shortcut bumps the containing folder's mtime; adding
    /// or removing a group bumps the root), plus the tools-group config. Equal fingerprint ⇒ the menu
    /// can't have changed, so the build is skipped entirely.</summary>
    private int ComputeFingerprint(string? rootFolder, bool showTools, List<DefaultTool> tools, string? language)
    {
        var hc = new HashCode();
        hc.Add(Volatile.Read(ref _cacheGeneration)); // a cache-clear bumps this → fingerprint changes → rebuild
        hc.Add(rootFolder ?? "");
        try
        {
            if (!string.IsNullOrWhiteSpace(rootFolder) && Directory.Exists(rootFolder))
            {
                hc.Add(Directory.GetLastWriteTimeUtc(rootFolder));
                var subs = Directory.GetDirectories(rootFolder);
                Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
                foreach (var sub in subs)
                {
                    hc.Add(sub);
                    try
                    {
                        hc.Add(Directory.GetLastWriteTimeUtc(sub));
                        // Also fold each file's name + last-write-time + size. A folder's own mtime only
                        // changes on add/remove/rename; editing a .lnk IN PLACE (Properties → change
                        // target rewrites the .lnk) bumps the FILE's mtime/size but not the folder's, so
                        // without this an in-place target change would be gated out and keep launching the
                        // old target. (A target exe overwritten in place under the SAME .lnk still needs a
                        // manual 刷新 — catching that would mean resolving every .lnk on each summon.) One
                        // metadata-only enumeration per group folder — far cheaper than the build it gates.
                        // Sort the files before folding: HashCode.Add is order-sensitive, and
                        // DirectoryInfo.GetFiles() has no documented ordering guarantee (stable on NTFS,
                        // but not on FAT/exFAT/SMB/ReFS roots). Without this, identical content could hash
                        // differently across summons and spuriously defeat the unchanged-folders gate.
                        // Mirrors the Array.Sort on the subfolders above.
                        var files = new DirectoryInfo(sub).GetFiles();
                        Array.Sort(files, static (x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
                        foreach (var fi in files)
                        {
                            hc.Add(fi.Name);
                            hc.Add(fi.LastWriteTimeUtc);
                            hc.Add(fi.Length);
                        }
                    }
                    catch { hc.Add(0); }
                }
            }
        }
        catch { /* scan failed → fingerprint reflects what we got; a later success differs and rebuilds */ }

        hc.Add(showTools);
        hc.Add(AppLanguage.Resolve(language));
        foreach (var t in tools)
        {
            hc.Add(t.Name);
            hc.Add(t.Command);
            hc.Add(t.Show);
        }
        return hc.ToHashCode();
    }

    private static string[] SafeGetDirectories(string? rootFolder)
    {
        try
        {
            return string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder)
                ? Array.Empty<string>()
                : Directory.GetDirectories(rootFolder);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ---- STA worker ------------------------------------------------------------------------------
    // One long-lived STA thread + job queue, created on first use. Reusing the apartment keeps shell
    // COM (CShellLink, SHGetFileInfo) warm across summons instead of paying thread + COM-apartment
    // spin-up/teardown on every build. Continuations resume off this worker (RunContinuationsAsynchronously)
    // so the Blazor StateHasChanged runs on the UI sync context, not here.
    private readonly BlockingCollection<Action> _staJobs = new();
    private Thread? _staThread;
    private readonly object _staInit = new();
    private volatile bool _disposed; // set on Dispose so a late RunStaAsync can't start a worker on the disposed queue

    private void EnsureStaThread()
    {
        if (_staThread != null || _disposed)
            return;
        lock (_staInit)
        {
            if (_staThread != null || _disposed)
                return;
            var t = new Thread(StaLoop) { IsBackground = true, Name = "TanMenu-MenuBuild" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            _staThread = t;
        }
    }

    private void StaLoop()
    {
        // GetConsumingEnumerable blocks until a job arrives and ends when the collection is completed.
        // The outer try guards the ENUMERATOR itself: if Dispose() disposes _staJobs while this loop is
        // still consuming (shutdown race), MoveNext can throw ObjectDisposedException — swallow it so the
        // background thread exits quietly instead of escalating to an unhandled-exception process crash.
        try
        {
            foreach (var job in _staJobs.GetConsumingEnumerable())
            {
                try { job(); }
                catch { /* each job already routes its own exception to its TaskCompletionSource */ }
            }
        }
        catch { /* enumerator disposed/completed during shutdown — exit the worker cleanly */ }
    }

    private Task<T> RunStaAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            EnsureStaThread();
            _staJobs.Add(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
        }
        catch (Exception ex)
        {
            // e.g. Add after Dispose() called CompleteAdding — surface as a faulted task rather than
            // throwing synchronously into the caller's await.
            tcs.SetException(ex);
        }
        return tcs.Task;
    }

    public void Dispose()
    {
        // Stop accepting jobs and let StaLoop drain + exit. Only dispose the collection AFTER the worker
        // has actually left its GetConsumingEnumerable loop (Join returned true) — disposing it while the
        // worker is still consuming races the enumerator (ObjectDisposedException). If the worker is stuck
        // past the timeout, skip the dispose; it's a background thread that dies with the process anyway.
        _disposed = true; // block a late EnsureStaThread from starting a worker on the (about-to-be) disposed queue
        try
        {
            _staJobs.CompleteAdding();
            if (_staThread == null || _staThread.Join(TimeSpan.FromSeconds(2)))
                _staJobs.Dispose();
        }
        catch { /* best-effort on shutdown */ }
    }
}
