namespace TanMenu.Wpf.Views.Settings;

/// <summary>Contract every settings card group implements. SettingsPage discovers its card groups
/// structurally (iterating its own child controls that implement this), rather than through a
/// per-group method call in ApplyStaticText that a new group could forget to add.</summary>
internal interface ISettingsCard
{
    /// <summary>Populate every control in this group from the live config. Must not write back —
    /// callers rely on the group's own reentrancy guard (e.g. a <c>_loading</c> flag) to make this
    /// safe to call at any time, including mid-session after a language change.</summary>
    void ReloadFromConfig();
}
