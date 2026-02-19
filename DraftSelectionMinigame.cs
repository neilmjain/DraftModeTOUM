using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using MiraAPI.Patches.Stubs;
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
    public Transform?     RolesHolder;
    public GameObject?    RolePrefab;
    public TextMeshPro?   StatusText;
    public TextMeshPro?   RoleName;
    public SpriteRenderer? RoleIcon;
    public TextMeshPro?   RoleTeam;
    public GameObject?    RedRing;
    public GameObject?    WarpRing;
    public TextMeshPro?   TurnListText;   // left panel: turn order + my slot

    private readonly Color _bgColor = new Color32(24, 0, 0, 215);
    private Action<int>?  _onPick;
    private List<DraftRoleCard> _cards = new();
    private int _currentCardIndex;

    public DraftSelectionMinigame(IntPtr cppPtr) : base(cppPtr) { }

    private void Awake()
    {
        if (Instance) Instance.Close();

        RolesHolder = transform.FindChild("Roles");
        RolePrefab  = transform.FindChild("RoleCardHolder").gameObject;

        var status  = transform.FindChild("Status");
        StatusText  = status.gameObject.GetComponent<TextMeshPro>();
        RoleName    = status.FindChild("RoleName").gameObject.GetComponent<TextMeshPro>();
        RoleTeam    = status.FindChild("RoleTeam").gameObject.GetComponent<TextMeshPro>();
        RoleIcon    = status.FindChild("RoleImage").gameObject.GetComponent<SpriteRenderer>();
        RedRing     = status.FindChild("RoleRing").gameObject;
        WarpRing    = status.FindChild("RingWarp").gameObject;

        var font    = HudManager.Instance.TaskPanel.taskText.font;
        var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

        StatusText.font = font; StatusText.fontMaterial = fontMat;
        StatusText.text = "Draft Pick";
        StatusText.gameObject.SetActive(false);

        RoleName.font = font; RoleName.fontMaterial = fontMat;
        RoleName.text = " "; RoleName.gameObject.SetActive(false);

        RoleTeam.font = font; RoleTeam.fontMaterial = fontMat;
        RoleTeam.text = " "; RoleTeam.gameObject.SetActive(false);

        RoleIcon.sprite = TouRoleIcons.RandomAny.LoadAsset();
        RoleIcon.gameObject.SetActive(false);
        RedRing.SetActive(false);
        WarpRing.SetActive(false);

        // ── Left-side panel ────────────────────────────────────────────────
        var listGo = new GameObject("DraftTurnList");
        listGo.transform.SetParent(transform, false);
        listGo.transform.localPosition = new Vector3(-4.2f, 1.8f, -1f);

        TurnListText = listGo.AddComponent<TextMeshPro>();
        TurnListText.font              = font;
        TurnListText.fontMaterial      = fontMat;
        TurnListText.fontSize          = 1.5f;
        TurnListText.alignment         = TextAlignmentOptions.TopLeft;
        TurnListText.enableWordWrapping = false;
        TurnListText.text              = "";
        TurnListText.gameObject.SetActive(false);
    }

    public static DraftSelectionMinigame Create()
    {
        var go = Instantiate(TouAssets.AltRoleSelectionGame.LoadAsset(), HudManager.Instance.transform);
        var existing = go.GetComponent<Minigame>();
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
        go.SetActive(false);
        return go.AddComponent<DraftSelectionMinigame>();
    }

    [HideFromIl2Cpp]
    public void Open(List<DraftRoleCard> cards, Action<int> onPick)
    {
        _cards = cards;
        _onPick = onPick;
        _currentCardIndex = 0;
        ClearCards();
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
        MinigameStubs.Close(this);
    }

    [HideFromIl2Cpp]
    public void RefreshTurnList()
    {
        if (TurnListText == null) return;
        TurnListText.text = BuildTurnListText();
    }

    private static string BuildTurnListText()
    {
        var sb = new System.Text.StringBuilder();

        // My slot number at the top — prominently
        int mySlot = Managers.DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
        if (mySlot > 0)
            sb.AppendLine($"<color=#00FFFF><b>You are Player #{mySlot}</b></color>\n");

        sb.AppendLine("<b>── Draft Order ──</b>");

        var order   = Managers.DraftManager.TurnOrder;
        int current = Managers.DraftManager.CurrentTurn;

        for (int i = 0; i < order.Count; i++)
        {
            int slot  = order[i];
            var state = Managers.DraftManager.GetStateForSlot(slot);
            if (state == null) continue;

            bool isMe = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
            string me = isMe ? " ◀" : "";

            string label, color;
            if (state.HasPicked)        { label = "(Picked)";           color = "#888888"; }
            else if (state.IsPickingNow){ label = "<b>(Picking)</b>";   color = "#FFD700"; }
            else                        { label = "(Waiting)";          color = "#AAAAAA"; }

            sb.AppendLine($"<color={color}>Player {slot}... {label}{me}</color>");
        }

        return sb.ToString();
    }

    private void Begin()
    {
        HudManager.Instance.StartCoroutine(HudManager.Instance.CoFadeFullScreen(Color.clear, _bgColor));

        StatusText!.gameObject.SetActive(true);
        RoleName!.gameObject.SetActive(true);
        RoleTeam!.gameObject.SetActive(true);
        RoleIcon!.gameObject.SetActive(true);
        RedRing!.SetActive(true);
        WarpRing!.SetActive(true);

        RoleIcon.transform.localScale = Vector3.one * 0.35f;

        TurnListText!.gameObject.SetActive(true);
        RefreshTurnList();

        foreach (var card in _cards)
            CreateCard(card);

        TransType = TransitionType.None;
        Begin(null);
    }

    private void ClearCards()
    {
        if (RolesHolder == null) return;
        foreach (var child in RolesHolder.transform)
        {
            var t = child.Cast<Transform>();
            if (t != null) Destroy(t.gameObject);
        }
    }

    private PassiveButton CreateCard(DraftRoleCard card)
    {
        var newRoleObj     = Instantiate(RolePrefab, RolesHolder);
        var actualCard     = newRoleObj!.transform.GetChild(0);
        var roleText       = actualCard.GetChild(0).gameObject.GetComponent<TextMeshPro>();
        var roleImage      = actualCard.GetChild(1).gameObject.GetComponent<SpriteRenderer>();
        var teamText       = actualCard.GetChild(2).gameObject.GetComponent<TextMeshPro>();
        var selection      = actualCard.GetChild(3).gameObject;
        var passiveButton  = actualCard.GetComponent<PassiveButton>();
        var buttonRollover = actualCard.GetComponent<ButtonRolloverHandler>();

        selection.SetActive(false);

        passiveButton.OnMouseOver.AddListener(new Action(() =>
        {
            selection.SetActive(true);
            RoleName!.text = card.RoleName;
            RoleTeam!.text = card.TeamName;
            if (card.Icon != null) RoleIcon!.sprite = card.Icon;
            RoleIcon!.transform.localScale = Vector3.one * 0.35f;
        }));
        passiveButton.OnMouseOut.AddListener(new Action(() => { selection.SetActive(false); }));

        float angle = (2 * Mathf.PI / _cards.Count) * _currentCardIndex;
        float x = 1.9f * Mathf.Cos(angle);
        float y = 0.1f + 1.9f * Mathf.Sin(angle);

        newRoleObj.transform.localPosition = new Vector3(x, y, -1f);
        newRoleObj.name = card.RoleName + " DraftSelection";

        roleText.text  = card.RoleName;
        teamText.text  = card.TeamName;
        roleImage.sprite = card.Icon;
        roleImage.transform.localScale = Vector3.one * 0.4f;

        buttonRollover.OverColor = card.Color;
        roleText.color = card.Color;
        teamText.color = card.Color;

        passiveButton.OnClick.RemoveAllListeners();
        passiveButton.OnClick.AddListener(new Action(() =>
        {
            _onPick!.Invoke(card.Index);
            Close();
        }));

        _currentCardIndex++;
        newRoleObj.gameObject.SetActive(true);
        return passiveButton;
    }
}

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
