using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using Reactor.Utilities;
using System.Collections;
using TMPro;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;
using UnityEngine.Events;

namespace DraftModeTOUM
{
    public class DraftScreenController : MonoBehaviour
    {
        public static DraftScreenController Instance { get; private set; }

        private GameObject _screenRoot;
        private string[] _offeredRoles;
        private bool _hasPicked;
        private TextMeshPro _statusText;

        private static readonly Color BgColor = new Color32(6, 0, 0, 215);

        private const string PrefabName = "SelectRoleGame";

        public static void Show(string[] offeredRoles)
        {
            Hide();
            var go = new GameObject("DraftScreenController");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DraftScreenController>();
            Instance._offeredRoles = offeredRoles;
            Instance.BuildScreen();
        }

        public static void Hide()
        {
            if (Instance == null) return;
            if (Instance._screenRoot != null) Destroy(Instance._screenRoot);

            if (HudManager.Instance != null)
            {
                var hud = HudManager.Instance.transform;
                for (int i = hud.childCount - 1; i >= 0; i--)
                {
                    var child = hud.GetChild(i);
                    if (child != null && child.name.StartsWith("DraftCard_"))
                        Destroy(child.gameObject);
                }

                // Fade the background out exactly like TOU does on Close()
                HudManager.Instance.StartCoroutine(
                    HudManager.Instance.CoFadeFullScreen(BgColor, Color.clear));
            }

            Destroy(Instance.gameObject);
            Instance = null;
        }

        private void BuildScreen()
        {
            if (HudManager.Instance == null) return;

            // Fade background in — same as TraitorSelectionMinigame.Begin()
            HudManager.Instance.StartCoroutine(
                HudManager.Instance.CoFadeFullScreen(Color.clear, BgColor));

            GameObject prefab = null;
            try
            {
                var bundle = TouAssets.MainBundle;
                if (bundle != null)
                    prefab = bundle.LoadAsset(PrefabName)?.TryCast<GameObject>();
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftScreenController] Bundle load failed: {ex.Message}");
            }

            if (prefab == null)
            {
                DraftModePlugin.Logger.LogError("[DraftScreenController] SelectRoleGame prefab not found.");
                Destroy(gameObject);
                Instance = null;
                return;
            }

            _screenRoot = Instantiate(prefab);
            _screenRoot.name = "DraftRoleSelectScreen";
            DontDestroyOnLoad(_screenRoot);

            if (HudManager.Instance != null)
            {
                _screenRoot.transform.SetParent(HudManager.Instance.transform, false);
                _screenRoot.transform.localPosition = Vector3.zero;
            }

            var holderGo    = _screenRoot.transform.Find("RoleCardHolder");
            var statusGo    = _screenRoot.transform.Find("Status");
            var rolesHolder = _screenRoot.transform.Find("Roles");

            // Wire up status text — same as TOU Awake()
            if (statusGo != null)
            {
                _statusText = statusGo.GetComponent<TextMeshPro>();
                if (_statusText != null)
                {
                    _statusText.font         = HudManager.Instance.TaskPanel.taskText.font;
                    _statusText.fontMaterial = HudManager.Instance.TaskPanel.taskText.fontMaterial;
                    _statusText.text         = "<color=#FFFFFF><b>Pick Your Role!</b></color>";
                    _statusText.gameObject.SetActive(true);
                }
            }

            if (holderGo == null)
            {
                Destroy(_screenRoot);
                Destroy(gameObject);
                Instance = null;
                return;
            }

            var rolePrefab = holderGo.gameObject;

            // Build card data through the shared pipeline (correct icons, teams, colors)
            var roleList = new System.Collections.Generic.List<string>();
            if (_offeredRoles != null) roleList.AddRange(_offeredRoles);
            var cards = DraftUiManager.BuildCards(roleList);

            // Spawn cards — mirrors TraitorSelectionMinigame.Begin()
            int z = 0;
            foreach (var card in cards)
            {
                int capturedIdx = card.Index;
                var btn = CreateCard(
                    rolePrefab,
                    rolesHolder,
                    card.RoleName,
                    card.TeamName,
                    card.Icon ?? TouRoleIcons.RandomAny.LoadAsset(),
                    z,
                    card.Color);

                btn.OnClick.RemoveAllListeners();
                btn.OnClick.AddListener((UnityAction)(() => OnCardClicked(capturedIdx)));
                z++;
            }

            // Animate cards in — mirrors TraitorSelectionMinigame
            Coroutines.Start(CoAnimateCards(rolesHolder));
        }

        // ── Card factory — mirrors TraitorSelectionMinigame.CreateCard() exactly ────────

        private static PassiveButton CreateCard(
            GameObject rolePrefab,
            Transform rolesHolder,
            string roleName,
            string teamName,
            Sprite icon,
            int z,
            Color color)
        {
            var newRoleObj    = UnityEngine.Object.Instantiate(rolePrefab, rolesHolder);
            var actualCard    = newRoleObj!.transform.GetChild(0);
            var roleText      = actualCard.GetChild(0).GetComponent<TextMeshPro>();
            var roleImage     = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
            var teamText      = actualCard.GetChild(2).GetComponent<TextMeshPro>();
            var passiveButton = actualCard.GetComponent<PassiveButton>();
            var rollover      = actualCard.GetComponent<ButtonRolloverHandler>();

            // Z-depth hover push — identical to TOU
            passiveButton.OnMouseOver.AddListener((UnityAction)(() =>
            {
                var pos = newRoleObj.transform.localPosition;
                newRoleObj.transform.localPosition = new Vector3(pos.x, pos.y, pos.z - 10f);
            }));
            passiveButton.OnMouseOut.AddListener((UnityAction)(() =>
            {
                var pos = newRoleObj.transform.localPosition;
                newRoleObj.transform.localPosition = new Vector3(pos.x, pos.y, pos.z + 10f);
            }));

            // Random tilt + Z stacking — identical math to TOU
            float randZ = -10f + z * 5f + UnityEngine.Random.Range(-1.5f, 1.5f);
            newRoleObj.transform.localRotation = Quaternion.Euler(0f, 0f, -randZ);
            newRoleObj.transform.localPosition = new Vector3(
                newRoleObj.transform.localPosition.x,
                newRoleObj.transform.localPosition.y,
                z);

            roleText.text    = roleName;
            teamText.text    = teamName;
            roleImage.sprite = icon;
            roleImage.SetSizeLimit(2.8f);  // TOU icon sizing call

            rollover.OverColor = color;
            roleText.color     = color;
            teamText.color     = color;

            return passiveButton;
        }

        // ── Card animation — mirrors TraitorSelectionMinigame.CoAnimateCards() ──────────

        private static IEnumerator CoAnimateCards(Transform rolesHolder)
        {
            if (rolesHolder == null) yield break;

            int currentCard = 0;
            foreach (var o in rolesHolder)
            {
                var card = o.Cast<Transform>();
                if (card == null) continue;

                var child = card.GetChild(0);
                yield return CoAnimateCardIn(child, currentCard);
                Coroutines.Start(MiscUtils.BetterBloop(child, finalSize: 0.55f, duration: 0.22f, intensity: 0.16f));
                yield return new WaitForSeconds(0.1f);
                currentCard++;
            }
        }

        // ── Slide-in — mirrors TraitorSelectionMinigame.CoAnimateCardIn() exactly ───────

        private static IEnumerator CoAnimateCardIn(Transform card, int currentCard)
        {
            float randY = (currentCard * currentCard * 0.5f - currentCard) * 0.1f
                          + UnityEngine.Random.Range(-0.15f, 0f);
            float randZ = -10f + currentCard * 5f + UnityEngine.Random.Range(-1.5f, 0f);
            if (currentCard == 0) { randY = 0f; randZ = -2f; }

            card.localRotation = Quaternion.Euler(0f, 0f, -randZ);
            card.localPosition = new Vector3(
                card.localPosition.x,
                card.localPosition.y - 5f,
                card.localPosition.z);
            card.localRotation = Quaternion.Euler(0f, 0f, 14f);
            card.localScale    = new Vector3(0.3f, 0.3f, 0.3f);
            card.parent.gameObject.SetActive(true);

            for (float timer = 0f; timer < 0.4f; timer += Time.deltaTime)
            {
                float t = timer / 0.4f;
                card.localPosition = new Vector3(
                    card.localPosition.x,
                    Mathf.SmoothStep(-5f, randY, t),
                    card.localPosition.z);
                card.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.SmoothStep(-randZ + 2.5f, -randZ, t));
                yield return null;
            }

            card.localPosition = new Vector3(card.localPosition.x, randY, card.localPosition.z);
            card.localRotation = Quaternion.Euler(0f, 0f, -randZ);
        }

        // ── Timer display ─────────────────────────────────────────────────────────────

        private float _localTimeLeft = -1f;

        private void Update()
        {
            if (_hasPicked || _statusText == null) return;
            if (!DraftManager.IsDraftActive) return;

            int secs;
            if (AmongUsClient.Instance.AmHost)
            {
                secs = Mathf.Max(0, Mathf.CeilToInt(DraftManager.TurnTimeLeft));
            }
            else
            {
                if (_localTimeLeft < -0.5f) _localTimeLeft = DraftManager.TurnDuration;
                if (_localTimeLeft > 0f) _localTimeLeft -= Time.deltaTime;
                secs = Mathf.Max(0, Mathf.CeilToInt(_localTimeLeft));
            }

            _statusText.text =
                $"<color=#FFFFFF><b>Pick Your Role!</b></color>   " +
                $"<color={(secs <= 5 ? "#FF5555" : "#FFD700")}>" +
                $"{secs} Second{(secs != 1 ? "s" : "")} Remain</color>";
        }

        // ── Pick handling ─────────────────────────────────────────────────────────────

        private void OnCardClicked(int index)
        {
            if (_hasPicked) return;
            _hasPicked = true;
            DraftNetworkHelper.SendPickToHost(index);
            Invoke(nameof(DestroySelf), 1.2f);
        }

        private void DestroySelf() => Hide();
    }
}
