using System.Reflection;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Who this app says it is: product name, publisher, version and copyright, in one place.
/// <para>
/// Round 5, item 2 created it, because folding 关于 into the 设置 page would otherwise have added a
/// THIRD copy of the version expression. There were already two, with different fallbacks —
/// <c>LauncherViewModel</c>'s splash footer used <c>?? "1.0.0"</c> and <c>UpdatesCard</c>'s deleted
/// 当前版本 card used <c>?? "0.0.0"</c> — and the copyright existed once as a C# interpolation with a
/// live year while the assembly carries a different, never-read, year-less string
/// (<c>TanMenu.Wpf.csproj</c>'s <c>Copyright</c> property). <c>"0.0.0"</c> is the fallback that
/// survived: a missing version means a broken build either way, and 0.0.0 sorts below every real one.
/// </para>
/// <para>
/// NO ASSEMBLY ATTRIBUTE IS READ, deliberately. <c>AssemblyInformationalVersion</c> carries the commit
/// SHA and <c>AssemblyCopyright</c> carries the year-less string, so neither is what the UI wants; more
/// to the point, this project publishes with <c>PublishAot</c>, and
/// <c>Assembly.GetName().Version</c> — the expression that was already shipping twice — needs no
/// attribute lookup, no <c>GetCustomAttribute</c> and no type discovery, so the trimming question never
/// has to be asked.
/// </para>
/// </summary>
internal static class AppIdentity
{
    public const string ProductName = "TanMenu";

    public const string Publisher = "TanSoft";

    /// <summary>The assembly version, three parts. Computed once — it cannot change while the process
    /// lives.</summary>
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Computed at read time, exactly as the old splash footer did — never a baked-in year.
    /// Not localized: the 关于 card shows it verbatim, the same string the overlay rendered.</summary>
    public static string Copyright => $"© {DateTime.Now.Year} {Publisher}. All rights reserved.";
}
