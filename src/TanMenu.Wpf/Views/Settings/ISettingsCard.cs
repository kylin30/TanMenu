namespace TanMenu.Wpf.Views.Settings;

/// <summary>Contract every settings card group implements. SettingsPage discovers its card groups
/// structurally (iterating its own child controls that implement this), rather than through a
/// per-group method call in ApplyStaticText that a new group could forget to add.</summary>
internal interface ISettingsCard
{
    /// <summary>Populate every control in this group from the shared <see cref="SettingsDraft"/> —
    /// since Round 8, Task 1 that is the working clone, not the live <c>ConfigService.Config</c>, so a
    /// reload re-shows PENDING edits rather than only committed ones. Must not write back — callers rely
    /// on the group's own reentrancy guard (e.g. a <c>_loading</c> flag) to make this safe to call at any
    /// time, including mid-session after a language change and immediately after a commit.</summary>
    void ReloadFromConfig();
}
