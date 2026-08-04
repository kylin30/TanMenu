namespace TanMenu.Wpf.Views.Settings;

/// <summary>Contract every settings card group implements. <c>OptionsWindow</c> discovers its card
/// groups structurally (iterating its <c>TabControl</c>'s items and taking each tab's content that
/// implements this), rather than through a per-group method call in <c>ApplyText</c> that a new group
/// could forget to add. The discoverer was <c>SettingsPage</c>, over its own children, until Round 8,
/// Task 2 replaced the page with the dialog; the contract itself did not change.</summary>
internal interface ISettingsCard
{
    /// <summary>Populate every control in this group from the shared <see cref="SettingsDraft"/> —
    /// since Round 8, Task 1 that is the working clone, not the live <c>ConfigService.Config</c>, so a
    /// reload re-shows PENDING edits rather than only committed ones. Must not write back — callers rely
    /// on the group's own reentrancy guard (e.g. a <c>_loading</c> flag) to make this safe to call at any
    /// time, including mid-session after a language change and immediately after a commit.</summary>
    void ReloadFromConfig();
}
