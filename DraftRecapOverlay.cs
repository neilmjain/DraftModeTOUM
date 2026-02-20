using Reactor.Utilities.Attributes;
using TMPro;
using UnityEngine;
using DraftModeTOUM.Managers;
using System.Collections.Generic;

namespace DraftModeTOUM
{
    /// <summary>
    /// Left-side HUD panel shown after the draft completes.
    /// Lists every player's drafted role with faction colour.
    /// Automatically hides when the game scene loads.
    /// </summary>
    [RegisterInIl2Cpp]
    public sealed class DraftRecapOverlay : MonoBehaviour
    {
        private static DraftRecapOverlay? _instance;

        private GameObject?  _root;
        private TextMeshPro? _titleText;
        private GameObject?  _entriesRoot;

        // The recap lines we last rendered — detect changes to avoid rebuilding every frame
        private int _lastLineCount = -1;

        // Stored recap data set by Show()
        private static readonly List<RecapEntry> _entries = new List<RecapEntry>();
        private static bool _visible = false;

        public DraftRecapOverlay(System.IntPtr ptr) : base(ptr) { }

        // ── Public API ────────────────────────────────────────────────────────

        public static void Show(List<RecapEntry> entries)
        {
            _entries.Clear();
            _entries.AddRange(entries);
            _visible = true;
            EnsureExists();
            _instance!.Rebuild();
            _instance.UpdateVisibility();
        }

        public static void Hide()
        {
            _visible = false;
            if (_instance != null)
                _instance.UpdateVisibility();
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftRecapOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftRecapOverlay>();
        }

        private void Awake()
        {
            BuildRoot();
        }

        private void BuildRoot()
        {
            if (HudManager.Instance == null) return;

            _root = new GameObject("DraftRecapRoot");
            DontDestroyOnLoad(_root);
            _root.transform.SetParent(HudManager.Instance.transform, false);

            // Left side of screen — x = -3.6 puts it just inside the left edge
            _root.transform.localPosition = new Vector3(-4.2f, 1.4f, -20f);

            var font    = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            // ── Title ─────────────────────────────────────────────────────────
            var titleGo = new GameObject("RecapTitle");
            titleGo.transform.SetParent(_root.transform, false);
            titleGo.transform.localPosition = Vector3.zero;
            _titleText = titleGo.AddComponent<TextMeshPro>();
            _titleText.font            = font;
            _titleText.fontMaterial    = fontMat;
            _titleText.fontSize        = 2.0f;
            _titleText.color           = new Color(1f, 0.85f, 0.1f);
            _titleText.fontStyle       = FontStyles.Bold;
            _titleText.alignment       = TextAlignmentOptions.Left;
            _titleText.enableWordWrapping = false;
            _titleText.text            = "── DRAFT RECAP ──";

            // ── Entries container ─────────────────────────────────────────────
            var entriesGo = new GameObject("RecapEntries");
            entriesGo.transform.SetParent(_root.transform, false);
            entriesGo.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            _entriesRoot = entriesGo;

            _root.SetActive(false);
        }

        private void Rebuild()
        {
            // Wait until HudManager is ready
            if (_root == null) BuildRoot();
            if (_root == null || _entriesRoot == null) return;

            // Destroy old entry lines
            for (int i = _entriesRoot.transform.childCount - 1; i >= 0; i--)
                Destroy(_entriesRoot.transform.GetChild(i).gameObject);

            var font    = HudManager.Instance?.TaskPanel?.taskText?.font;
            var fontMat = HudManager.Instance?.TaskPanel?.taskText?.fontMaterial;

            float lineHeight = 0.42f;

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];

                var lineGo = new GameObject($"RecapLine_{i}");
                lineGo.transform.SetParent(_entriesRoot.transform, false);
                lineGo.transform.localPosition = new Vector3(0f, -i * lineHeight, 0f);

                var tmp = lineGo.AddComponent<TextMeshPro>();
                if (font    != null) tmp.font         = font;
                if (fontMat != null) tmp.fontMaterial = fontMat;
                tmp.fontSize           = 1.55f;
                tmp.alignment          = TextAlignmentOptions.Left;
                tmp.enableWordWrapping = false;

                // Format: "PlayerName  RoleName"
                // Player name in grey, role name in faction colour
                string roleHex = ColorUtility.ToHtmlStringRGB(entry.RoleColor);
                tmp.text = $"<color=#AAAAAA>{entry.PlayerName}</color>  " +
                           $"<color=#{roleHex}><b>{entry.RoleName}</b></color>";
            }

            _lastLineCount = _entries.Count;
        }

        private void Update()
        {
            // Auto-hide when game scene loads (ShipStatus exists = in game)
            if (_visible && ShipStatus.Instance != null)
            {
                Hide();
                return;
            }

            if (_root == null && HudManager.Instance != null)
                BuildRoot();
        }

        private void UpdateVisibility()
        {
            if (_root == null) return;
            _root.SetActive(_visible);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }

    public sealed class RecapEntry
    {
        public string PlayerName { get; }
        public string RoleName   { get; }
        public Color  RoleColor  { get; }

        public RecapEntry(string playerName, string roleName, Color roleColor)
        {
            PlayerName = playerName;
            RoleName   = roleName;
            RoleColor  = roleColor;
        }
    }
}
