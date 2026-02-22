using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using Reactor.Utilities;
using System.Collections;
using TMPro;
using TownOfUs.Assets;
using UnityEngine;

namespace DraftModeTOUM
{
    public class DraftScreenController : MonoBehaviour
    {
        public static DraftScreenController Instance { get; private set; }

        private GameObject _screenRoot;
        private string[] _offeredRoles;
        private bool _hasPicked;
        private TextMeshPro _statusText;

        private static readonly Color NavyColor = new Color(0f, 0f, 0f, 01f);

        private const string PrefabName = "SelectRoleGame";
        public static float CardZSpacing = 0.5f;

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
            if (Instance._canvasOverlay != null) Destroy(Instance._canvasOverlay);
            if (Instance._cardCanvas != null) Destroy(Instance._cardCanvas);
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
            }
            Destroy(Instance.gameObject);
            Instance = null;
        }

        private GameObject _canvasOverlay;
        private GameObject _cardCanvas;

        private void BuildScreen()
        {
            var bgGo = new GameObject("DraftNavyBg");
            DontDestroyOnLoad(bgGo);
            _canvasOverlay = bgGo;
            _cardCanvas = null;

            if (HudManager.Instance != null)
                bgGo.transform.SetParent(HudManager.Instance.transform, false);
            var bgSr = bgGo.AddComponent<SpriteRenderer>();
            bgSr.sprite = GetWhiteSprite();
            bgSr.color = NavyColor;
            bgSr.sortingLayerName = "UI";
            bgSr.sortingOrder = 9999;

            var cam = Camera.main;
            float camH = cam != null ? cam.orthographicSize * 2f : 6f;
            float camW = camH * ((float)Screen.width / Screen.height);
            bgGo.transform.localPosition = new Vector3(0f, 0f, 1f);
            bgGo.transform.localScale = new Vector3(camW, camH, 1f);

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

            var rolesHolder = _screenRoot.transform.Find("Roles");
            var holderGo = _screenRoot.transform.Find("RoleCardHolder");
            var statusGo = _screenRoot.transform.Find("Status");

            if (statusGo != null)
            {
                _statusText = statusGo.GetComponent<TextMeshPro>();
                var sp = statusGo.transform.localPosition;
                statusGo.transform.localPosition = new Vector3(sp.x, sp.y - 0.5f, sp.z);
            }
            if (_statusText != null)
            {
                var font = HudManager.Instance?.TaskPanel?.taskText?.font;
                var fontMat = HudManager.Instance?.TaskPanel?.taskText?.fontMaterial;
                if (font != null) _statusText.font = font;
                if (fontMat != null) _statusText.fontMaterial = fontMat;
                _statusText.text = "<color=#FFFFFF><b>Pick Your Role!</b></color>";
            }

            if (holderGo == null)
            {
                Destroy(_screenRoot);
                Destroy(gameObject);
                Instance = null;
                return;
            }

            var cardParent = HudManager.Instance != null
                ? HudManager.Instance.transform
                : _screenRoot.transform;
            var rolePrefab = holderGo.gameObject;

            int offeredCount = _offeredRoles?.Length ?? 0;
            bool showRandom = DraftManager.ShowRandomOption;
            int totalCards = offeredCount + (showRandom ? 1 : 0);
            if (totalCards == 0) return;

            const int maxPerRow = 5;
            int row0Count = Mathf.Min(totalCards, maxPerRow);
            int row1Count = totalCards - row0Count;
            bool hasSecondRow = row1Count > 0;

            var layoutCam = Camera.main;
            float screenW = layoutCam != null
                ? layoutCam.orthographicSize * 2f * ((float)Screen.width / Screen.height)
                : 16f;
            float screenH = layoutCam != null ? layoutCam.orthographicSize * 2f : 9f;

            float usableW = screenW * 0.88f;
            float spacing = usableW / row0Count;
            float cardScale = Mathf.Clamp(spacing / 1.75f * 0.80f, 0.15f, 0.46f);
            float rowGap = screenH * 0.32f;

            float row0Y = hasSecondRow ? rowGap * 0.20f : -0.05f;
            float row1Y = row0Y - rowGap;

            // Build the shared card data once using the same pipeline as the circle UI.
            // This ensures role lookup, team labels, icons, and colors are all consistent.
            var roleList = new System.Collections.Generic.List<string>();
            if (_offeredRoles != null)
                roleList.AddRange(_offeredRoles);
            var cards = DraftUiManager.BuildCards(roleList);

            for (int i = 0; i < totalCards; i++)
            {
                int idx = i;
                bool isRandom = showRandom && (i == offeredCount);

                // Pull pre-resolved data from the card
                DraftRoleCard card = i < cards.Count ? cards[i] : null;
                string roleName = card?.RoleName ?? (isRandom ? "Random" : "?");
                Color color    = card?.Color    ?? Color.white;
                string team    = card?.TeamName ?? (isRandom ? "Any" : "Unknown");
                Sprite icon    = card?.Icon     ?? TouRoleIcons.RandomAny.LoadAsset();

                bool inRow1 = i >= row0Count;
                int colIdx = inRow1 ? i - row0Count : i;
                int rowSize = inRow1 ? row1Count : row0Count;
                float xPos = -((rowSize - 1) * spacing) / 2f + colIdx * spacing;
                float yPos = inRow1 ? row1Y : row0Y;

                var newRoleObj = Instantiate(rolePrefab, cardParent);
                var actualCard = newRoleObj.transform.GetChild(0);

                DontDestroyOnLoad(newRoleObj);
                newRoleObj.transform.localRotation = Quaternion.identity;
                newRoleObj.transform.localPosition = new Vector3(xPos, yPos, i * CardZSpacing);
                newRoleObj.transform.localScale = Vector3.one * cardScale;
                newRoleObj.SetActive(true);
                newRoleObj.name = $"DraftCard_{i}_{roleName}";

                var roleText  = actualCard.GetChild(0).GetComponent<TextMeshPro>();
                var roleImage = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
                var teamText  = actualCard.GetChild(2).GetComponent<TextMeshPro>();

                if (roleText  != null) { roleText.text  = roleName; roleText.color  = color; }
                if (teamText  != null) { teamText.text  = team;     teamText.color  = color; }
                if (roleImage != null)
                {
                    roleImage.sprite = icon;
                    // Ensure icon doesn't inherit a stale transform scale from the prefab
                    roleImage.transform.localScale = Vector3.one * 0.4f;
                }

                var cardBgSr = actualCard.GetComponent<SpriteRenderer>();
                if (cardBgSr != null)
                {
                    Color.RGBToHSV(color, out float h, out float s, out float v);
                    cardBgSr.color = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.75f), Mathf.Clamp01(v * 0.18f));
                }

                AddGlow(newRoleObj, color, scale: 1.18f, alpha: 0.35f, z: 0.8f, sortOrder: 10000);
                AddGlow(newRoleObj, color, scale: 1.32f, alpha: 0.15f, z: 1.2f, sortOrder: 10000);

                var btn = actualCard.GetComponent<PassiveButton>();
                if (btn != null)
                {
                    btn.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    btn.OnClick.AddListener((System.Action)(() => OnCardClicked(idx)));

                    var glowSrs = newRoleObj.GetComponentsInChildren<SpriteRenderer>();
                    float hoverScale = cardScale + 0.06f;

                    btn.OnMouseOver = new UnityEngine.Events.UnityEvent();
                    btn.OnMouseOver.AddListener((System.Action)(() =>
                    {
                        newRoleObj.transform.localScale = Vector3.one * hoverScale;
                        foreach (var sr in glowSrs)
                            if (sr.gameObject.name == "Glow")
                                sr.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(sr.color.a * 2.2f));
                    }));

                    btn.OnMouseOut = new UnityEngine.Events.UnityEvent();
                    btn.OnMouseOut.AddListener((System.Action)(() =>
                    {
                        newRoleObj.transform.localScale = Vector3.one * cardScale;
                        foreach (var sr in glowSrs)
                            if (sr.gameObject.name == "Glow")
                                sr.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(sr.color.a / 2.2f));
                    }));

                    var rollover = actualCard.GetComponent<ButtonRolloverHandler>();
                    if (rollover != null) rollover.OverColor = color;
                }
            }

            foreach (var sr in _screenRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.sortingLayerName = "UI";
                if (sr.sortingOrder < 10000) sr.sortingOrder = 10000;
            }
            foreach (var tmp in _screenRoot.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                var r = tmp.GetComponent<Renderer>();
                if (r != null) { r.sortingLayerName = "UI"; if (r.sortingOrder < 10001) r.sortingOrder = 10001; }
            }
        }

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

        private void OnCardClicked(int index)
        {
            if (_hasPicked) return;
            _hasPicked = true;
            DraftNetworkHelper.SendPickToHost(index);
            Invoke(nameof(DestroySelf), 1.2f);
        }

        private void DestroySelf() => Hide();

        private static void AddGlow(GameObject parent, Color color, float scale, float alpha, float z, int sortOrder)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetWhiteSprite();
            sr.color = new Color(color.r, color.g, color.b, alpha);
            sr.sortingLayerName = "UI";
            sr.sortingOrder = sortOrder;
        }

        private static Sprite _white;
        private static Sprite GetWhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _white;
        }
    }
}
