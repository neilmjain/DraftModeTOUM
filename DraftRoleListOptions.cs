using MiraAPI.GameOptions;
using MiraAPI.GameOptions.Attributes;
using MiraAPI.GameOptions.OptionTypes;
using MiraAPI.Utilities;

namespace DraftModeTOUM;

// ─────────────────────────────────────────────────────────────────────────────
// Draft Mode faction option groups.
//
// Each faction is its own AbstractOptionGroup — MiraAPI renders each as an
// independently collapsible header in the Game options menu, exactly like
// TOU's Crewmate/Impostor/Neutral Modifier groups.
//
// Seat-chance sliders are hidden via Visible = () => Max >= N, so if Max is 0
// nothing shows; if Max is 2, only Seat 1 and Seat 2 appear. This matches the
// TOU "Amount / Chance" pattern used in CrewmateModifierOptions etc.
// ─────────────────────────────────────────────────────────────────────────────

// Shorthand so every `new` call isn't enormous.
// Full 11-param signature:
//   title, default, min, max, increment, zeroBehavior, negativeBehavior, suffix, formatString?, halfIncrements, includeInPreset
file static class Chance
{
    public static ModdedNumberOption Of(string title, float def) =>
        new(title, def, 0f, 100f, 10f, "#", "#", MiraNumberSuffixes.Percent, "0", true, true);
}

// ══════════════════════════════════════════════════════════════════════════════
// IMPOSTOR
// ══════════════════════════════════════════════════════════════════════════════
public sealed class DraftImpOptions : AbstractOptionGroup
{
    public override string GroupName     => "Draft — Impostors";
    public override uint   GroupPriority => 101;

    [ModdedNumberOption("Max Impostors", 0f, 5f, 1f, MiraNumberSuffixes.None, "0")]
    public float MaxImpostors { get; set; } = 2f;

    public ModdedNumberOption ImpChance1 { get; } = Chance.Of("Imp Seat 1 Chance", 100f);
    public ModdedNumberOption ImpChance2 { get; } = Chance.Of("Imp Seat 2 Chance", 100f);
    public ModdedNumberOption ImpChance3 { get; } = Chance.Of("Imp Seat 3 Chance",  50f);
    public ModdedNumberOption ImpChance4 { get; } = Chance.Of("Imp Seat 4 Chance",  25f);
    public ModdedNumberOption ImpChance5 { get; } = Chance.Of("Imp Seat 5 Chance",  10f);

    public DraftImpOptions()
    {
        ImpChance1.Visible = () => OptionGroupSingleton<DraftImpOptions>.Instance.MaxImpostors >= 1f;
        ImpChance2.Visible = () => OptionGroupSingleton<DraftImpOptions>.Instance.MaxImpostors >= 2f;
        ImpChance3.Visible = () => OptionGroupSingleton<DraftImpOptions>.Instance.MaxImpostors >= 3f;
        ImpChance4.Visible = () => OptionGroupSingleton<DraftImpOptions>.Instance.MaxImpostors >= 4f;
        ImpChance5.Visible = () => OptionGroupSingleton<DraftImpOptions>.Instance.MaxImpostors >= 5f;
    }

    public float GetChance(int seat) => seat switch
    {
        1 => ImpChance1.Value,
        2 => ImpChance2.Value,
        3 => ImpChance3.Value,
        4 => ImpChance4.Value,
        _ => ImpChance5.Value,
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// NEUTRAL KILLING
// ══════════════════════════════════════════════════════════════════════════════
public sealed class DraftNeutKillOptions : AbstractOptionGroup
{
    public override string GroupName     => "Draft — Neutral Killing";
    public override uint   GroupPriority => 102;

    [ModdedNumberOption("Max Neutral Killings", 0f, 10f, 1f, MiraNumberSuffixes.None, "0")]
    public float MaxNeutralKillings { get; set; } = 1f;

    public ModdedNumberOption NKChance1  { get; } = Chance.Of("NK Seat 1 Chance",  100f);
    public ModdedNumberOption NKChance2  { get; } = Chance.Of("NK Seat 2 Chance",   50f);
    public ModdedNumberOption NKChance3  { get; } = Chance.Of("NK Seat 3 Chance",   25f);
    public ModdedNumberOption NKChance4  { get; } = Chance.Of("NK Seat 4 Chance",   10f);
    public ModdedNumberOption NKChance5  { get; } = Chance.Of("NK Seat 5 Chance",   10f);
    public ModdedNumberOption NKChance6  { get; } = Chance.Of("NK Seat 6 Chance",   10f);
    public ModdedNumberOption NKChance7  { get; } = Chance.Of("NK Seat 7 Chance",   10f);
    public ModdedNumberOption NKChance8  { get; } = Chance.Of("NK Seat 8 Chance",   10f);
    public ModdedNumberOption NKChance9  { get; } = Chance.Of("NK Seat 9 Chance",   10f);
    public ModdedNumberOption NKChance10 { get; } = Chance.Of("NK Seat 10 Chance",  10f);

    public DraftNeutKillOptions()
    {
        NKChance1.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 1f;
        NKChance2.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 2f;
        NKChance3.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 3f;
        NKChance4.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 4f;
        NKChance5.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 5f;
        NKChance6.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 6f;
        NKChance7.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 7f;
        NKChance8.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 8f;
        NKChance9.Visible  = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 9f;
        NKChance10.Visible = () => OptionGroupSingleton<DraftNeutKillOptions>.Instance.MaxNeutralKillings >= 10f;
    }

    public float GetChance(int seat) => seat switch
    {
        1  => NKChance1.Value,
        2  => NKChance2.Value,
        3  => NKChance3.Value,
        4  => NKChance4.Value,
        5  => NKChance5.Value,
        6  => NKChance6.Value,
        7  => NKChance7.Value,
        8  => NKChance8.Value,
        9  => NKChance9.Value,
        _  => NKChance10.Value,
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// NEUTRAL BENIGN
// ══════════════════════════════════════════════════════════════════════════════
public sealed class DraftNeutBenignOptions : AbstractOptionGroup
{
    public override string GroupName     => "Draft — Neutral Benign";
    public override uint   GroupPriority => 103;

    [ModdedNumberOption("Max Neutral Benign", 0f, 10f, 1f, MiraNumberSuffixes.None, "0")]
    public float MaxNeutralBenign { get; set; } = 1f;

    public ModdedNumberOption NBChance1  { get; } = Chance.Of("Benign Seat 1 Chance",  100f);
    public ModdedNumberOption NBChance2  { get; } = Chance.Of("Benign Seat 2 Chance",   50f);
    public ModdedNumberOption NBChance3  { get; } = Chance.Of("Benign Seat 3 Chance",   25f);
    public ModdedNumberOption NBChance4  { get; } = Chance.Of("Benign Seat 4 Chance",   10f);
    public ModdedNumberOption NBChance5  { get; } = Chance.Of("Benign Seat 5 Chance",   10f);
    public ModdedNumberOption NBChance6  { get; } = Chance.Of("Benign Seat 6 Chance",   10f);
    public ModdedNumberOption NBChance7  { get; } = Chance.Of("Benign Seat 7 Chance",   10f);
    public ModdedNumberOption NBChance8  { get; } = Chance.Of("Benign Seat 8 Chance",   10f);
    public ModdedNumberOption NBChance9  { get; } = Chance.Of("Benign Seat 9 Chance",   10f);
    public ModdedNumberOption NBChance10 { get; } = Chance.Of("Benign Seat 10 Chance",  10f);

    public DraftNeutBenignOptions()
    {
        NBChance1.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 1f;
        NBChance2.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 2f;
        NBChance3.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 3f;
        NBChance4.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 4f;
        NBChance5.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 5f;
        NBChance6.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 6f;
        NBChance7.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 7f;
        NBChance8.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 8f;
        NBChance9.Visible  = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 9f;
        NBChance10.Visible = () => OptionGroupSingleton<DraftNeutBenignOptions>.Instance.MaxNeutralBenign >= 10f;
    }

    public float GetChance(int seat) => seat switch
    {
        1  => NBChance1.Value,
        2  => NBChance2.Value,
        3  => NBChance3.Value,
        4  => NBChance4.Value,
        5  => NBChance5.Value,
        6  => NBChance6.Value,
        7  => NBChance7.Value,
        8  => NBChance8.Value,
        9  => NBChance9.Value,
        _  => NBChance10.Value,
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// NEUTRAL EVIL
// ══════════════════════════════════════════════════════════════════════════════
public sealed class DraftNeutEvilOptions : AbstractOptionGroup
{
    public override string GroupName     => "Draft — Neutral Evil";
    public override uint   GroupPriority => 104;

    [ModdedNumberOption("Max Neutral Evil", 0f, 10f, 1f, MiraNumberSuffixes.None, "0")]
    public float MaxNeutralEvil { get; set; } = 1f;

    public ModdedNumberOption NEChance1  { get; } = Chance.Of("Evil Seat 1 Chance",  100f);
    public ModdedNumberOption NEChance2  { get; } = Chance.Of("Evil Seat 2 Chance",   50f);
    public ModdedNumberOption NEChance3  { get; } = Chance.Of("Evil Seat 3 Chance",   25f);
    public ModdedNumberOption NEChance4  { get; } = Chance.Of("Evil Seat 4 Chance",   10f);
    public ModdedNumberOption NEChance5  { get; } = Chance.Of("Evil Seat 5 Chance",   10f);
    public ModdedNumberOption NEChance6  { get; } = Chance.Of("Evil Seat 6 Chance",   10f);
    public ModdedNumberOption NEChance7  { get; } = Chance.Of("Evil Seat 7 Chance",   10f);
    public ModdedNumberOption NEChance8  { get; } = Chance.Of("Evil Seat 8 Chance",   10f);
    public ModdedNumberOption NEChance9  { get; } = Chance.Of("Evil Seat 9 Chance",   10f);
    public ModdedNumberOption NEChance10 { get; } = Chance.Of("Evil Seat 10 Chance",  10f);

    public DraftNeutEvilOptions()
    {
        NEChance1.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 1f;
        NEChance2.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 2f;
        NEChance3.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 3f;
        NEChance4.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 4f;
        NEChance5.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 5f;
        NEChance6.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 6f;
        NEChance7.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 7f;
        NEChance8.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 8f;
        NEChance9.Visible  = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 9f;
        NEChance10.Visible = () => OptionGroupSingleton<DraftNeutEvilOptions>.Instance.MaxNeutralEvil >= 10f;
    }

    public float GetChance(int seat) => seat switch
    {
        1  => NEChance1.Value,
        2  => NEChance2.Value,
        3  => NEChance3.Value,
        4  => NEChance4.Value,
        5  => NEChance5.Value,
        6  => NEChance6.Value,
        7  => NEChance7.Value,
        8  => NEChance8.Value,
        9  => NEChance9.Value,
        _  => NEChance10.Value,
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// NEUTRAL OUTLIER
// ══════════════════════════════════════════════════════════════════════════════
public sealed class DraftNeutOutlierOptions : AbstractOptionGroup
{
    public override string GroupName     => "Draft — Neutral Outlier";
    public override uint   GroupPriority => 105;

    [ModdedNumberOption("Max Neutral Outlier", 0f, 10f, 1f, MiraNumberSuffixes.None, "0")]
    public float MaxNeutralOutlier { get; set; } = 1f;

    public ModdedNumberOption NOChance1  { get; } = Chance.Of("Outlier Seat 1 Chance",  100f);
    public ModdedNumberOption NOChance2  { get; } = Chance.Of("Outlier Seat 2 Chance",   50f);
    public ModdedNumberOption NOChance3  { get; } = Chance.Of("Outlier Seat 3 Chance",   25f);
    public ModdedNumberOption NOChance4  { get; } = Chance.Of("Outlier Seat 4 Chance",   10f);
    public ModdedNumberOption NOChance5  { get; } = Chance.Of("Outlier Seat 5 Chance",   10f);
    public ModdedNumberOption NOChance6  { get; } = Chance.Of("Outlier Seat 6 Chance",   10f);
    public ModdedNumberOption NOChance7  { get; } = Chance.Of("Outlier Seat 7 Chance",   10f);
    public ModdedNumberOption NOChance8  { get; } = Chance.Of("Outlier Seat 8 Chance",   10f);
    public ModdedNumberOption NOChance9  { get; } = Chance.Of("Outlier Seat 9 Chance",   10f);
    public ModdedNumberOption NOChance10 { get; } = Chance.Of("Outlier Seat 10 Chance",  10f);

    public DraftNeutOutlierOptions()
    {
        NOChance1.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 1f;
        NOChance2.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 2f;
        NOChance3.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 3f;
        NOChance4.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 4f;
        NOChance5.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 5f;
        NOChance6.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 6f;
        NOChance7.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 7f;
        NOChance8.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 8f;
        NOChance9.Visible  = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 9f;
        NOChance10.Visible = () => OptionGroupSingleton<DraftNeutOutlierOptions>.Instance.MaxNeutralOutlier >= 10f;
    }

    public float GetChance(int seat) => seat switch
    {
        1  => NOChance1.Value,
        2  => NOChance2.Value,
        3  => NOChance3.Value,
        4  => NOChance4.Value,
        5  => NOChance5.Value,
        6  => NOChance6.Value,
        7  => NOChance7.Value,
        8  => NOChance8.Value,
        9  => NOChance9.Value,
        _  => NOChance10.Value,
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// CREWMATE  — sub-category spawn weight sliders
// ══════════════════════════════════════════════════════════════════════════════
public sealed class DraftCrewOptions : AbstractOptionGroup
{
    public override string GroupName     => "Draft — Crewmate";
    public override uint   GroupPriority => 106;

    public ModdedNumberOption CrewInvestigativeChance { get; } = Chance.Of("Crew Investigative Chance", 70f);
    public ModdedNumberOption CrewKillingChance       { get; } = Chance.Of("Crew Killing Chance",       70f);
    public ModdedNumberOption CrewPowerChance         { get; } = Chance.Of("Crew Power Chance",         70f);
    public ModdedNumberOption CrewProtectiveChance    { get; } = Chance.Of("Crew Protective Chance",    70f);
    public ModdedNumberOption CrewSupportChance       { get; } = Chance.Of("Crew Support Chance",       70f);
}
