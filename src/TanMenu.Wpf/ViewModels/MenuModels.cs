using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.ViewModels;

/// <summary>Hand-rolled <see cref="INotifyPropertyChanged"/> base. Deliberately not ReactiveUI or the
/// MVVM toolkit: the launcher needs three notifications and the app ships NativeAOT, where every extra
/// dependency is code size and another reflection surface to audit.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assign and notify only when the value actually changed.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}

/// <summary>A launcher item. Built on the STA worker by <c>MenuService</c>, bound by the launcher window.</summary>
public sealed class MenuItemVm : ObservableObject
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string? TargetPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsDisabled { get; init; }

    // ---- Icon --------------------------------------------------------------------------------
    // MenuService still produces RAW base64 PNG (it caches the bytes and encodes once per build), so
    // the property keeps its name and type; the view binds the decoded <see cref="Icon"/> instead.
    // The slot is NEVER empty — the build always assigns at least the stock fallback — which is what
    // makes the async icon fill reflow-free: the placeholder occupies the same fixed box as the real
    // icon (see the launcher spec §5).
    private string? _iconBase64;
    private IImage? _icon;

    /// <summary>Raw base64 PNG. Mutable so the async icon-fill pass can replace the fallback
    /// placeholder once the structure has rendered.</summary>
    public string? IconBase64
    {
        get => _iconBase64;
        set
        {
            if (string.Equals(_iconBase64, value, StringComparison.Ordinal))
                return;
            _iconBase64 = value;
            _icon = null;
            RaisePropertyChanged(nameof(Icon));
        }
    }

    /// <summary>The decoded icon. Decoded lazily on first read (i.e. on the UI thread during render)
    /// rather than in the setter, because the initial build assigns every icon from the STA worker.</summary>
    public IImage? Icon => _icon ??= Decode(_iconBase64);

    /// <summary>The one definition of "this item matches the search box": a substring of
    /// <see cref="Name"/> only — not the path, not the target, not the group name — compared
    /// ordinally and case-insensitively. No fuzzy matching, no pinyin, no accent folding, no word
    /// boundaries.</summary>
    /// <param name="trimmedFilter">The trimmed filter text; empty means "no filter" and matches
    /// nothing. Callers that want "no filter shows everything" must special-case the empty string
    /// before asking — <see cref="MenuGroupVm.ApplyFilter"/> does.</param>
    /// <remarks>Both the visible-items pass and the Enter-launches-the-first-match lookup route
    /// through here so the two can never drift apart.</remarks>
    public bool Matches(string trimmedFilter) =>
        trimmedFilter.Length > 0 && Name.Contains(trimmedFilter, StringComparison.OrdinalIgnoreCase);

    // Decoded bitmaps are shared by payload: every icon-less item carries the SAME fallback base64,
    // so keying the cache on the string collapses hundreds of identical decodes into one bitmap.
    // Capped rather than unbounded — this is a tray-resident singleton's data and a long session over
    // a churning desktop would otherwise accumulate every icon it ever saw.
    private static readonly ConcurrentDictionary<string, Bitmap> DecodedIcons = new(StringComparer.Ordinal);
    private const int DecodedIconCap = 512;

    private static IImage? Decode(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
            return null;
        if (DecodedIcons.TryGetValue(base64, out var cached))
            return cached;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(base64));
            var bitmap = new Bitmap(stream);
            if (DecodedIcons.Count < DecodedIconCap)
                DecodedIcons.TryAdd(base64, bitmap);
            return bitmap;
        }
        catch
        {
            // A corrupt cache entry must not take the menu down: the item simply renders text-only.
            return null;
        }
    }
}

/// <summary>One column within a category's tile grid, holding at most
/// <see cref="TanMenu.Core.Models.GeneralConfig.ColButtonCount"/> items — a thin wrapper around one
/// chunk from <see cref="MenuLayout.ChunkIntoColumns{T}"/>. It exists purely so the per-column
/// <c>DataTemplate</c> in AppPage.axaml has a concrete, named <c>x:DataType</c> to compile a binding
/// against: Avalonia's compiled-bindings mode (required for the NativeAOT publish —
/// <c>AvaloniaUseCompiledBindingsByDefault</c> in TanMenu.Wpf.csproj) cannot bind against a bare
/// <c>IReadOnlyList&lt;MenuItemVm&gt;</c> the way a reflection-based binding could.</summary>
public sealed class MenuColumnVm
{
    public IReadOnlyList<MenuItemVm> Items { get; }

    public MenuColumnVm(IReadOnlyList<MenuItemVm> items) => Items = items;
}

/// <summary>A folder group of launcher items — one section of the launcher.</summary>
public sealed class MenuGroupVm : ObservableObject
{
    public string Directory { get; init; } = "";
    public string DirectoryName { get; init; } = "";
    public List<MenuItemVm> Items { get; init; } = new();

    /// <summary>The built-in 常用工具 group has an EMPTY Directory, so its title is inert: no folder
    /// glyph, no tooltip, no hover affordance, no click (<c>MenuService.BuildDefaultToolsGroup</c>).</summary>
    public bool IsDirectoryClickable => !string.IsNullOrEmpty(Directory);

    private string? _titleTooltip;

    /// <summary>"打开文件夹" while clickable, null otherwise — a null tip shows no tooltip at all.
    /// Assigned by the view model because the localized text depends on the current language.</summary>
    public string? TitleTooltip { get => _titleTooltip; set => Set(ref _titleTooltip, value); }

    private IReadOnlyList<MenuItemVm> _visibleItems = Array.Empty<MenuItemVm>();

    // GeneralConfig.ColButtonCount's own default (10) — kept live by SetColumnCapacity, which
    // LauncherViewModel.ApplyGroupSettings calls after every build and every settings change, so a
    // freshly constructed group (whose SetColumnCapacity has not been called yet) still chunks
    // consistently with the app-wide default until the real config value is applied a moment later.
    private int _columnCapacity = 10;
    private IReadOnlyList<MenuColumnVm> _columns = Array.Empty<MenuColumnVm>();

    /// <summary>Sentinel for <see cref="_appliedFilter"/>: a value no real filter text can ever equal,
    /// so the very first <see cref="ApplyFilter"/> call — and the first one after
    /// <see cref="InvalidateFilter"/> — always runs instead of short-circuiting as "unchanged".
    /// Deliberately a NUL (U+0000). It cannot arise from ordinary filter text at all, so it needs no
    /// assumption at all about what callers pass; a literal space would be only coincidentally safe,
    /// since <see cref="string.Trim"/> strips whitespace but not NUL, so a caller that forgot to trim
    /// could legitimately pass " " and collide with a space sentinel.
    /// <para>
    /// What Round 3's final fix wave changed here is the SPELLING, not the value. Both sites holding
    /// this sentinel (this one and <see cref="InvalidateFilter"/>) carried a raw NUL byte sitting inside
    /// the string literal in the source, which made this whole file read as binary to both `git diff`
    /// and `grep` — that is how Task 4's own changes to this file went invisible to the review artifact.
    /// (An earlier revision of this remark added that the sentinel "used to be documented as" a space.
    /// It never was: the comment it replaced read, in full, "a value no trimmed filter can equal, so the
    /// first Apply always runs" — checked against the blob at commit 2af2401, not recalled. That clause
    /// is dropped rather than reworded.) It is now
    /// written here as the escape <c>"\u0000"</c> rather than a literal control character in the source,
    /// so the file stays text to every tool that reads it.
    /// </para></summary>
    private const string NoFilterApplied = "\u0000";

    private string _appliedFilter = NoFilterApplied;

    /// <summary>The items this section actually renders: all of them when no filter is active, only
    /// the matches otherwise. The launcher genuinely filters — non-matches are removed and the grid
    /// reflows. (The retro build instead kept every item laid out and only dimmed the non-matches,
    /// because its fixed grid could not afford to re-measure on each keystroke.)</summary>
    public IReadOnlyList<MenuItemVm> VisibleItems => _visibleItems;

    /// <summary><see cref="VisibleItems"/> chunked into columns of at most
    /// <see cref="TanMenu.Core.Models.GeneralConfig.ColButtonCount"/> each — what AppPage.axaml's
    /// per-category tile grid actually binds to. See <see cref="MenuLayout.ChunkIntoColumns{T}"/> for
    /// the pure chunking arithmetic (unit-tested without any UI) and <see cref="SetColumnCapacity"/>
    /// for how the live setting reaches here.</summary>
    public IReadOnlyList<MenuColumnVm> Columns => _columns;

    /// <summary>False when a filter has hidden everything in this section, which is what removes the
    /// whole heading from the layout rather than leaving an empty caption behind.</summary>
    public bool HasVisibleItems => _visibleItems.Count > 0;

    /// <summary>Recompute <see cref="VisibleItems"/>. Called after every rebuild and on every
    /// keystroke; a repeated filter is a no-op so typing does not churn the bindings.</summary>
    /// <param name="trimmedFilter">Trimmed filter text; empty means "show everything".</param>
    public void ApplyFilter(string trimmedFilter)
    {
        if (string.Equals(_appliedFilter, trimmedFilter, StringComparison.Ordinal))
            return;
        _appliedFilter = trimmedFilter;

        var next = trimmedFilter.Length == 0
            ? (IReadOnlyList<MenuItemVm>)Items
            : Items.Where(i => i.Matches(trimmedFilter)).ToList();

        var had = _visibleItems.Count > 0;
        _visibleItems = next;
        RaisePropertyChanged(nameof(VisibleItems));
        if (had != (next.Count > 0))
            RaisePropertyChanged(nameof(HasVisibleItems));

        RecomputeColumns();
    }

    /// <summary>Update the per-column item cap (<see cref="TanMenu.Core.Models.GeneralConfig.ColButtonCount"/>)
    /// and re-chunk immediately if it actually changed. Called by
    /// <c>LauncherViewModel.ApplyGroupSettings</c> after every build and every settings change, so
    /// editing the setting re-chunks — and, via <c>LauncherViewModel.ContentMetricsChanged</c>, re-fits
    /// the window — without waiting for a folder rescan.</summary>
    public void SetColumnCapacity(int perColumn)
    {
        var capacity = Math.Max(1, perColumn); // see MenuLayout.ChunkIntoColumns for why
        if (capacity == _columnCapacity)
            return;
        _columnCapacity = capacity;
        RecomputeColumns();
    }

    private void RecomputeColumns()
    {
        _columns = MenuLayout.ChunkIntoColumns(_visibleItems, _columnCapacity)
            .Select(static chunk => new MenuColumnVm(chunk))
            .ToList();
        RaisePropertyChanged(nameof(Columns));
    }

    /// <summary>Drop the memoized filter so the next <see cref="ApplyFilter"/> recomputes even if the
    /// text is unchanged. Required after <see cref="Items"/> is replaced by a rebuild.</summary>
    public void InvalidateFilter() => _appliedFilter = NoFilterApplied;
}
