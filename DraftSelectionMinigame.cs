using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using MiraAPI.Patches.Stubs;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using Reactor.Utilities;
using Reactor.Utilities.Attributes;
using TMPro;
using TownOfUs.Assets;
using UnityEngine;
using UnityEngine.Events;

namespace DraftModeTOUM;

[RegisterInIl2Cpp]
public sealed class DraftSelectionMinigame : Minigame
{
    // Unity-visible fields
    public Transform?      RolesHolder;
    public GameObject?     RolePrefab;
    public TextMeshPro?    StatusText;
    public TextMeshPro?    RoleName;
    public SpriteRenderer? RoleIcon;
    public TextMeshPro?    RoleTeam;
    public GameObject?     RedRing;
    public GameObject?     WarpRing;
    public TextMeshPro?    TurnListText;

    // Static card-tracking (same pattern as AmbassadorSelectionMinigame)
    public static int CurrentCard { get; set; }
    public static int RoleCount   { get; set; }

    private readonly Color _bgColor = new Color32(24, 0, 0, 215);

    // Private managed state — IL2CPP never sees private fields directly
    private List<DraftRoleCard>? _cards;
    private Action<int>?         _onPick;

    public DraftSelectionMinigame(IntPtr cppPtr) : base(cppPtr) { }

    private void Awake()
    {
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Awake() called.");
        if (Instance) Instance.Close();

        RolesHolder = transform.FindChild("Roles");
        RolePrefab  = transform.FindChild("RoleCardHolder").gameObject;

        var status = transform.FindChild("Status");
        StatusText = status.gameObject.GetComponent<TextMeshPro>();
        RoleName   = status.FindChild("RoleName").gameObject.GetComponent<TextMeshPro>();
        RoleTeam   = status.FindChild("RoleTeam").gameObject.GetComponent<TextMeshPro>();
        RoleIcon   = status.FindChild("RoleImage").gameObject.GetComponent<SpriteRenderer>();
        RedRing    = status.FindChild("RoleRing").gameObject;
        WarpRing   = status.FindChild("RingWarp").gameObject;

        var font    = HudManager.Instance.TaskPanel.taskText.font;
        var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

        StatusText.font = font; StatusText.fontMaterial = fontMat;
        StatusText.text = "Draft Pick";
        StatusText.gameObject.SetActive(false);

        RoleName.font = font; RoleName.fontMaterial = fontMat;
        RoleName.text = " ";  RoleName.gameObject.SetActive(false);

        RoleTeam.font = font; RoleTeam.fontMaterial = fontMat;
        RoleTeam.text = " ";  RoleTeam.gameObject.SetActive(false);

        RoleIcon.sprite = TouRoleIcons.RandomAny.LoadAsset();
        RoleIcon.gameObject.SetActive(false);
        RedRing.SetActive(false);
        WarpRing.SetActive(false);

        // Turn-order panel (left side)
        var listGo = new GameObject("DraftTurnList");
        listGo.transform.SetParent(transform, false);
        listGo.transform.localPosition = new Vector3(-4.2f, 1.8f, -1f);

        TurnListText = listGo.AddComponent<TextMeshPro>();
        TurnListText.font               = font;
        TurnListText.fontMaterial       = fontMat;
        TurnListText.fontSize           = 1.5f;
        TurnListText.alignment          = TextAlignmentOptions.TopLeft;
        TurnListText.enableWordWrapping = false;
        TurnListText.text               = "";
        TurnListText.gameObject.SetActive(false);

        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Awake() completed.");
    }

    // Mirrors AmbassadorSelectionMinigame.Create() exactly
    public static DraftSelectionMinigame Create()
    {
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Create() called.");
        var go = Instantiate(TouAssets.AltRoleSelectionGame.LoadAsset(), HudManager.Instance.transform);
        // DestroyImmediate requires the component object, not zero arguments
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Minigame>());
        go.SetActive(false);
        var result = go.AddComponent<DraftSelectionMinigame>();
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Create() complete.");
        return result;
    }

    [HideFromIl2Cpp]
    public void Open(List<DraftRoleCard> cards, Action<int> onPick)
    {
        DraftModePlugin.Logger.LogInfo($"[DraftSelectionMinigame] Open() called with {cards.Count} cards.");
        _cards    = cards;
        _onPick   = onPick;
        RoleCount = cards.Count + 1; // +1 for the Random card
        CurrentCard = 0;
        Coroutines.Start(CoOpen(this));
    }

    private static IEnumerator CoOpen(DraftSelectionMinigame minigame)
    {
        while (ExileController.Instance != null)
            yield return new WaitForSeconds(0.65f);
        minigame.gameObject.SetActive(true);
        minigame.Begin();
    }

    public override void Close()
    {
        HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(_bgColor, Color.clear));
        CurrentCard = -1;
        RoleCount   = -1;
        MinigameStubs.Close(this);
    }

    [HideFromIl2Cpp]
    public void RefreshTurnList()
    {
        if (TurnListText == null) return;
        TurnListText.text = BuildTurnListText();
    }

    [HideFromIl2Cpp]
    private static string BuildTurnListText()
    {
        var sb = new System.Text.StringBuilder();

        int mySlot = Managers.DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
        if (mySlot > 0)
            sb.AppendLine($"<color=#00FFFF><b>You are Pick #{mySlot}</b></color>\n");

        sb.AppendLine("<b>── Draft Order ──</b>");

        foreach (int slot in Managers.DraftManager.TurnOrder)
        {
            var state = Managers.DraftManager.GetStateForSlot(slot);
            if (state == null) continue;

            bool   isMe  = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
            string me    = isMe ? " ◀" : "";
            string label, color;

            if (state.HasPicked)         { label = "(Picked)";         color = "#888888"; }
            else if (state.IsPickingNow) { label = "<b>(Picking)</b>"; color = "#FFD700"; }
            else                         { label = "(Waiting)";        color = "#AAAAAA"; }

            sb.AppendLine($"<color={color}>Pick {slot}... {label}{me}</color>");
        }
        return sb.ToString();
    }

    private void Begin()
    {
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Begin() called.");
        HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(Color.clear, _bgColor));

        StatusText!.gameObject.SetActive(true);
        RoleName!.gameObject.SetActive(true);
        RoleTeam!.gameObject.SetActive(true);
        RoleIcon!.gameObject.SetActive(true);
        RedRing!.SetActive(true);
        WarpRing!.SetActive(true);
        // Scale the icon manually — SetSizeLimit is a TOU-Mira internal, use localScale instead
        RoleIcon.transform.localScale = Vector3.one * 0.35f;

        TurnListText!.gameObject.SetActive(true);
        RefreshTurnList();

        if (_cards != null)
        {
            foreach (var card in _cards)
            {
                var btn           = CreateCard(card.RoleName, card.TeamName, card.Icon, card.Color);
                int capturedIndex = card.Index;
                btn.OnClick.RemoveAllListeners();
                btn.OnClick.AddListener(new Action(() =>
                {
                    _onPick?.Invoke(capturedIndex);
                    Close();
                }));
            }
        }

        // Random card is already included in _cards by DraftUiManager.BuildCards — do NOT add another one here

        Coroutines.Start(CoAnimateCards());
        TransType = TransitionType.None;
        MinigameStubs.Begin(this, null);
        DraftModePlugin.Logger.LogInfo("[DraftSelectionMinigame] Begin() complete.");
    }

    private PassiveButton CreateCard(string roleName, string teamName, Sprite? icon, Color color)
    {
        var newRoleObj    = Instantiate(RolePrefab, RolesHolder);
        var actualCard    = newRoleObj!.transform.GetChild(0);
        var roleText      = actualCard.GetChild(0).gameObject.GetComponent<TextMeshPro>();
        var roleImage     = actualCard.GetChild(1).gameObject.GetComponent<SpriteRenderer>();
        var teamText      = actualCard.GetChild(2).gameObject.GetComponent<TextMeshPro>();
        var selection     = actualCard.GetChild(3).gameObject;
        var passiveButton = actualCard.GetComponent<PassiveButton>();
        var rollover      = actualCard.GetComponent<ButtonRolloverHandler>();

        selection.SetActive(false);

        passiveButton.OnMouseOver.AddListener(new Action(() =>
        {
            selection.SetActive(true);
            RoleName!.text = roleName;
            RoleTeam!.text = teamName;
            if (icon != null) RoleIcon!.sprite = icon;
            RoleIcon!.transform.localScale = Vector3.one * 0.35f;
        }));
        passiveButton.OnMouseOut.AddListener(new Action(() => selection.SetActive(false)));

        float angle = (2 * Mathf.PI / RoleCount) * CurrentCard;
        float x     = 1.9f * Mathf.Cos(angle);
        float y     = 0.1f + 1.9f * Mathf.Sin(angle);

        newRoleObj.transform.localPosition = new Vector3(x, y, -1f);
        newRoleObj.name = roleName + " DraftSelection";

        roleText.text    = roleName;
        teamText.text    = teamName;
        roleImage.sprite = icon ?? TouRoleIcons.RandomAny.LoadAsset();
        // Scale image manually instead of SetSizeLimit
        roleImage.transform.localScale = Vector3.one * 0.4f;

        rollover.OverColor = color;
        roleText.color     = color;
        teamText.color     = color;

        CurrentCard++;
        newRoleObj.gameObject.SetActive(true);
        return passiveButton;
    }

    [HideFromIl2Cpp]
    private IEnumerator CoAnimateCards()
    {
        if (RolesHolder == null) yield break;

        // Simple pop-in animation — replaces MiscUtils.BetterBloop (TOU-Mira internal)
        foreach (var o in RolesHolder.transform)
        {
            var card = o.Cast<Transform>();
            if (card == null) continue;
            var child = card.GetChild(0);
            Coroutines.Start(CoPopIn(child));
            yield return new WaitForSeconds(0.01f);
        }

        CurrentCard = -1;
        RoleCount   = -1;
    }

    // Simple scale pop-in replacing BetterBloop
    private static IEnumerator CoPopIn(Transform t)
    {
        float targetScale = 0.5f;
        float duration    = 0.12f;
        t.localScale      = Vector3.zero;

        for (float timer = 0f; timer < duration; timer += Time.deltaTime)
        {
            float progress  = timer / duration;
            // Overshoot slightly then settle
            float scale     = Mathf.LerpUnclamped(0f, targetScale, EaseOutBack(progress));
            t.localScale    = Vector3.one * scale;
            yield return null;
        }

        t.localScale = Vector3.one * targetScale;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}

// Plain managed data class — NOT registered in IL2CPP
public sealed class DraftRoleCard
{
    public string  RoleName { get; }
    public string  TeamName { get; }
    public Sprite? Icon     { get; }
    public Color   Color    { get; }
    public int     Index    { get; }

    public DraftRoleCard(string roleName, string teamName, Sprite? icon, Color color, int index)
    {
        RoleName = roleName;
        TeamName = teamName;
        Icon     = icon;
        Color    = color;
        Index    = index;
    }
}
