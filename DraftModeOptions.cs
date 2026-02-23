using MiraAPI.GameOptions;
using MiraAPI.GameOptions.Attributes;
using MiraAPI.Utilities;

namespace DraftModeTOUM;

public sealed class DraftModeOptions : AbstractOptionGroup
{
    public override string GroupName => "Draft Mode";
    public override uint GroupPriority => 100;

    [ModdedToggleOption("Use Circle Style (Off = Cards)")]
    public bool UseCircleStyle { get; set; } = false;

    [ModdedToggleOption("Enable Draft Mode")]
    public bool EnableDraft { get; set; } = true;

    [ModdedToggleOption("Lock Lobby On Draft Start")]
    public bool LockLobbyOnDraftStart { get; set; } = true;

    [ModdedToggleOption("Auto-Start After Draft")]
    public bool AutoStartAfterDraft { get; set; } = true;

    [ModdedToggleOption("Show Draft Recap")]
    public bool ShowRecap { get; set; } = true;

    [ModdedToggleOption("Use Role Chances For Weighting")]
    public bool UseRoleChances { get; set; } = true;

    [ModdedToggleOption("Show Random Option")]
    public bool ShowRandomOption { get; set; } = true;

    [ModdedToggleOption("Show Background Overlay")]
    public bool ShowBackground { get; set; } = true;

    [ModdedNumberOption("Offered Roles Per Turn", 1f, 9f, 1f, MiraNumberSuffixes.None, "0")]
    public float OfferedRolesCount { get; set; } = 3f;

    [ModdedNumberOption("Turn Duration", 5f, 60f, 1f, MiraNumberSuffixes.Seconds, "0")]
    public float TurnDurationSeconds { get; set; } = 10f;
}
