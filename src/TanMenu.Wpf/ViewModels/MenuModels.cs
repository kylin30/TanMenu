using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;

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

    // ---- Filter emphasis ---------------------------------------------------------------------
    // Filtering NEVER hides or moves an item (launcher spec §2.2) — a design that collapsed
    // non-matches would resize the popup on every keystroke. Only these two flags change.
    private bool _isDimmed;
    private bool _isHit;

    /// <summary>Non-matching while a filter is active — faded, still laid out.</summary>
    public bool IsDimmed { get => _isDimmed; set => Set(ref _isDimmed, value); }

    /// <summary>Name matches the active filter — gets the gold ring. Deliberately does NOT consider
    /// <see cref="IsDisabled"/>: a disabled match is still highlighted, it just can't be launched
    /// (the old <c>ItemClass</c> behaved the same way; do not "fix" it accidentally).</summary>
    public bool IsHit { get => _isHit; set => Set(ref _isHit, value); }

    /// <summary>The one definition of "this item matches the search box": a substring of
    /// <see cref="Name"/> only — not the path, not the target, not the group name — compared
    /// ordinally and case-insensitively. No fuzzy matching, no pinyin, no accent folding, no word
    /// boundaries.</summary>
    /// <param name="trimmedFilter">The trimmed filter text; empty means "no filter" and matches
    /// nothing (an empty filter dims and highlights nothing at all).</param>
    /// <remarks>Both the highlight pass and the Enter-launches-the-first-match lookup route through
    /// here so the two can never drift apart.</remarks>
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

/// <summary>One rendered column of a group. Chunking is SEQUENTIAL (items 1..N in column 1, N+1..2N in
/// column 2), not a balanced split, so the last column may be short.</summary>
public sealed class MenuColumnVm
{
    public MenuColumnVm(IReadOnlyList<MenuItemVm> items) => Items = items;

    public IReadOnlyList<MenuItemVm> Items { get; }
}

/// <summary>A folder group of launcher items.</summary>
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

    private int _chunkSize = -1;
    private IReadOnlyList<MenuColumnVm> _columns = Array.Empty<MenuColumnVm>();

    /// <summary>The items chunked into columns of <c>General.ColButtonCount</c>.</summary>
    public IReadOnlyList<MenuColumnVm> Columns => _columns;

    /// <summary>(Re)chunk into columns. Called after every reload — including one where the folder
    /// scan was skipped — because a settings-only change to <c>ColButtonCount</c> still re-shapes the
    /// grid. Values &lt;= 0 clamp to 1: <c>Chunk(0)</c> throws.</summary>
    public void Rechunk(int colButtonCount)
    {
        var size = Math.Max(1, colButtonCount);
        if (size == _chunkSize)
            return;
        _chunkSize = size;

        var columns = new List<MenuColumnVm>();
        foreach (var chunk in Items.Chunk(size))
            columns.Add(new MenuColumnVm(chunk));
        _columns = columns;
        RaisePropertyChanged(nameof(Columns));
    }
}
