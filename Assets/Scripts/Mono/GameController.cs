using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public enum MeldType { Pong, Kong, Chow }

public class GameController : MonoBehaviour
{
    public static GameController Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private Button drawButton;
    [SerializeField] private Button sortButton;
    [SerializeField] private TextMeshProUGUI tileDisplayText;
    [SerializeField] private TextMeshProUGUI wallCountText;

    [Header("Hand")]
    [SerializeField] private Transform handContainer;
    [SerializeField] private GameObject handTilePrefab;

    [Header("Discard")]
    [SerializeField] private DiscardPhysics discardPhysics;

    [Header("Scoring")]
    [SerializeField] private Transform scoringBox;

    [Header("Sprites")]
    [SerializeField] private Sprite[] tileSprites;

    [Header("Meld Highlights")]
    [SerializeField] private Color pongColor = HexColor("#038249");
    [SerializeField] private Color kongColor = HexColor("#bf3718");
    [SerializeField] private Color chowColor = HexColor("#2a3b92");

    [Header("Meld Popups")]
    [SerializeField] private GameObject meldPopupPrefab;
    [SerializeField] private float meldPopupYOffset = 120f;
    [SerializeField] private RectTransform handPopupLayer;
    [SerializeField] private Sprite pongSprite;
    [SerializeField] private Sprite kongSprite;
    [SerializeField] private Sprite chowSprite;

    [Header("Mahjong")]
    [SerializeField] private GameObject mahjongPopupPrefab;

    private const float TileWidth   = 124f;
    private const float TileSpacing = 5f;
    private const float TileStep    = TileWidth + TileSpacing;

    private readonly List<GameObject> _activeMeldPopups = new();
    private int _targetHandSize = 13;
    public int TargetHandSize => _targetHandSize;

    private Wall _wall = new Wall();
    private Canvas _canvas;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake() => Instance = this;

    void Start()
    {
        _canvas = FindAnyObjectByType<Canvas>();
        _wall.Build();
        _wall.Shuffle(new System.Random());
        drawButton.onClick.AddListener(OnDrawButton);
        sortButton.onClick.AddListener(SortHand);
        UpdateWallCount();
    }

    void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame && drawButton.interactable)
            OnDrawButton();
        if (Keyboard.current.sKey.wasPressedThisFrame)
            SortHand();
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    void OnDrawButton()
    {
        int handSize = handContainer.childCount;

        if (handSize == 0)
        {
            for (int i = 0; i < 13; i++)
                if (_wall.TryDraw(out Tile tile))
                    AddTileToHand(tile);
            tileDisplayText.text = "Hand ready! Draw your 14th tile.";
        }
        else if (handSize == _targetHandSize)
        {
            if (_wall.TryDraw(out Tile tile))
            {
                AddTileToHand(tile);
                tileDisplayText.text = "Hand full! Drag a tile up to discard.";
                drawButton.interactable = false;
                CheckForMahjong();
            }
        }

        UpdateWallCount();
        RefreshHandHighlights();
    }

    void AddTileToHand(Tile tile)
    {
        if (tile.IsBonusTile)
        {
            AddTileToScoringBox(tile);
            if (_wall.TryDraw(out Tile replacement))
                AddTileToHand(replacement);
            return;
        }

        GameObject tileObj = Instantiate(handTilePrefab, handContainer);
        tileObj.GetComponent<Image>().sprite = tileSprites[GetSpriteIndex(tile)];
        tileObj.AddComponent<HandTileData>().tile = tile;
        RefreshHandPositions();
    }

    void AddTileToScoringBox(Tile tile)
    {
        GameObject tileObj = Instantiate(handTilePrefab, scoringBox);
        tileObj.GetComponent<Image>().sprite = tileSprites[GetSpriteIndex(tile)];
        tileObj.AddComponent<HandTileData>().tile = tile;
        Destroy(tileObj.GetComponent<HandTileDrag>());
        Destroy(tileObj.GetComponent<SmoothMove>());
    }

    // ── Hand management ───────────────────────────────────────────────────────

    public void RefreshHandPositions()
    {
        int count = handContainer.childCount;
        float totalWidth = count * TileWidth + (count - 1) * TileSpacing;
        float startX = -totalWidth / 2f + TileWidth / 2f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = handContainer.GetChild(i).GetComponent<RectTransform>();
            SmoothMove sm = rt.GetComponent<SmoothMove>();
            if (sm != null)
                sm.MoveTo(new Vector2(startX + i * TileStep, 0));
            else
                rt.anchoredPosition = new Vector2(startX + i * TileStep, 0);
        }
    }

    void SortHand()
    {
        int count = handContainer.childCount;
        var tiles = new List<(Transform obj, int key)>();

        for (int i = 0; i < count; i++)
        {
            Transform child = handContainer.GetChild(i);
            HandTileData data = child.GetComponent<HandTileData>();
            tiles.Add((child, data != null ? SortKey(data.tile) : int.MaxValue));
        }

        tiles.Sort((a, b) => a.key.CompareTo(b.key));
        for (int i = 0; i < tiles.Count; i++)
            tiles[i].obj.SetSiblingIndex(i);

        RefreshHandPositions();
        RefreshHandHighlights();
    }

    int SortKey(Tile t) => SuitOrder(t.Suit) * 100 + t.Value;

    int SuitOrder(Suit s) => s switch
    {
        Suit.Circles    => 0,
        Suit.Bamboo     => 1,
        Suit.Characters => 2,
        Suit.Wind       => 3,
        Suit.Dragon     => 4,
        _               => 5
    };

    // ── Discard ───────────────────────────────────────────────────────────────

    public void DiscardTile(GameObject tileObj, Vector2 screenPos, Vector2 screenVelocity)
    {
        CanvasGroup cg = tileObj.GetComponent<CanvasGroup>();
        if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = true; }

        SmoothMove sm = tileObj.GetComponent<SmoothMove>();
        if (sm != null)
        {
            sm.TeleportTo(tileObj.GetComponent<RectTransform>().anchoredPosition);
            Destroy(sm);
        }

        Destroy(tileObj.GetComponent<HandTileDrag>());

        Camera uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransform discardRect = discardPhysics.GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(discardRect, screenPos, uiCam, out Vector2 localPos);

        Vector2 localVelocity = Vector2.ClampMagnitude(screenVelocity / _canvas.scaleFactor, 700f);
        discardPhysics.Spawn(tileObj, localPos, localVelocity);

        RefreshHandPositions();
        tileDisplayText.text = "Discarded. Draw your next tile.";
        if (_wall.Remaining > 0) drawButton.interactable = true;

        UpdateWallCount();
        RefreshHandHighlights();
    }

    public void PushDiscardTilesAt(Vector2 screenPos)
    {
        Camera uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransform discardRect = discardPhysics.GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(discardRect, screenPos, uiCam, out Vector2 localPos);
        discardPhysics.PushWithTile(localPos);
    }

    public RectTransform GetDiscardRect() => discardPhysics.GetComponent<RectTransform>();

    // ── Meld highlights ───────────────────────────────────────────────────────

    public void RefreshHandHighlights()
    {
        foreach (var popup in _activeMeldPopups)
            if (popup != null) Destroy(popup);
        _activeMeldPopups.Clear();

        var tileList = new List<HandTileData>();
        for (int i = 0; i < handContainer.childCount; i++)
        {
            var data = handContainer.GetChild(i).GetComponent<HandTileData>();
            if (data != null) tileList.Add(data);
        }
        var tiles = tileList.ToArray();
        int count = tiles.Length;

        foreach (var td in tiles)
        {
            var highlight = td.transform.Find("Highlight");
            if (highlight != null) highlight.GetComponent<Image>().color = Color.clear;
        }

        var marked = new bool[count];

        for (int i = 0; i <= count - 4; i++)
        {
            if (tiles[i]?.tile == null || !IsKong(tiles, i)) continue;
            for (int k = i; k < i + 4; k++) { marked[k] = true; ApplyHighlight(tiles[k].gameObject, kongColor); }
            var meldTiles = new List<GameObject>();
            for (int k = i; k < i + 4; k++) meldTiles.Add(tiles[k].gameObject);
            SpawnMeldPopup(meldTiles, MeldType.Kong, i, 4);
        }

        for (int i = 0; i <= count - 3; i++)
        {
            if (marked[i] || marked[i+1] || marked[i+2] || tiles[i]?.tile == null) continue;
            if (!IsPong(tiles, i)) continue;
            for (int k = i; k < i + 3; k++) { marked[k] = true; ApplyHighlight(tiles[k].gameObject, pongColor); }
            var meldTiles = new List<GameObject>();
            for (int k = i; k < i + 3; k++) meldTiles.Add(tiles[k].gameObject);
            SpawnMeldPopup(meldTiles, MeldType.Pong, i, 3);
        }

        for (int i = 0; i <= count - 3; i++)
        {
            if (marked[i] || marked[i+1] || marked[i+2] || tiles[i]?.tile == null) continue;
            if (!IsChow(tiles, i)) continue;
            for (int k = i; k < i + 3; k++) { marked[k] = true; ApplyHighlight(tiles[k].gameObject, chowColor); }
            var meldTiles = new List<GameObject>();
            for (int k = i; k < i + 3; k++) meldTiles.Add(tiles[k].gameObject);
            SpawnMeldPopup(meldTiles, MeldType.Chow, i, 3);
        }
    }

    void ApplyHighlight(GameObject obj, Color color)
    {
        var highlight = obj.transform.Find("Highlight");
        if (highlight != null) highlight.GetComponent<Image>().color = color;
    }

    bool IsKong(HandTileData[] t, int i) =>
        t[i].tile.Equals(t[i+1].tile) &&
        t[i].tile.Equals(t[i+2].tile) &&
        t[i].tile.Equals(t[i+3].tile);

    bool IsPong(HandTileData[] t, int i) =>
        t[i].tile.Equals(t[i+1].tile) &&
        t[i].tile.Equals(t[i+2].tile);

    bool IsChow(HandTileData[] t, int i)
    {
        Tile a = t[i].tile, b = t[i+1].tile, c = t[i+2].tile;
        if (a.Suit != b.Suit || b.Suit != c.Suit) return false;
        if (a.Suit == Suit.Wind || a.Suit == Suit.Dragon ||
            a.Suit == Suit.Flower || a.Suit == Suit.Season) return false;
        return b.Value == a.Value + 1 && c.Value == b.Value + 1;
    }

    // ── Meld popup / claim ────────────────────────────────────────────────────

    void SpawnMeldPopup(List<GameObject> meldTiles, MeldType type, int startIndex, int length)
    {
        Camera uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        int count = handContainer.childCount;
        float totalWidth = count * TileWidth + (count - 1) * TileSpacing;
        float startX = -totalWidth / 2f + TileWidth / 2f;
        float centerX = startX + (startIndex + (length - 1) / 2f) * TileStep;

        Vector3 worldPos = handContainer.TransformPoint(new Vector3(centerX, 0f, 0f));
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(handPopupLayer, screenPos, uiCam, out Vector2 localPos);
        localPos.y += meldPopupYOffset;

        GameObject popup = Instantiate(meldPopupPrefab, handPopupLayer);
        popup.GetComponent<RectTransform>().anchoredPosition = localPos;
        popup.GetComponent<Image>().sprite = type switch
        {
            MeldType.Pong => pongSprite,
            MeldType.Kong => kongSprite,
            MeldType.Chow => chowSprite,
            _             => null
        };

        var captured = meldTiles;
        popup.GetComponent<Button>().onClick.AddListener(() => ClaimMeld(captured));
        _activeMeldPopups.Add(popup);
    }

    void ClaimMeld(List<GameObject> meldTiles)
    {
        foreach (GameObject tileObj in meldTiles)
        {
            var highlight = tileObj.transform.Find("Highlight");
            if (highlight != null) highlight.GetComponent<Image>().color = Color.clear;

            Destroy(tileObj.GetComponent<HandTileDrag>());
            Destroy(tileObj.GetComponent<SmoothMove>());
            tileObj.transform.SetParent(scoringBox, false);
        }

        _targetHandSize -= meldTiles.Count;
        drawButton.interactable = _wall.Remaining > 0;
        UpdateWallCount();
        RefreshHandPositions();
        RefreshHandHighlights();
    }

    // ── Win detection ─────────────────────────────────────────────────────────

    List<Tile> GetAllNonBonusTiles()
    {
        var result = new List<Tile>();
        foreach (Transform child in handContainer)
        {
            var data = child.GetComponent<HandTileData>();
            if (data?.tile != null && !data.tile.IsBonusTile)
                result.Add(data.tile);
        }
        foreach (Transform child in scoringBox)
        {
            var data = child.GetComponent<HandTileData>();
            if (data?.tile != null && !data.tile.IsBonusTile)
                result.Add(data.tile);
        }
        return result;
    }

    bool IsWinningHand(List<Tile> tiles)
    {
        tiles.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));

        for (int i = 0; i < tiles.Count - 1; i++)
        {
            if (!tiles[i].Equals(tiles[i + 1])) continue;
            if (i > 0 && tiles[i].Equals(tiles[i - 1])) continue;

            var remaining = new List<Tile>(tiles);
            remaining.RemoveAt(i + 1);
            remaining.RemoveAt(i);
            if (CanFormSets(remaining)) return true;
        }
        return false;
    }

    bool CanFormSets(List<Tile> tiles)
    {
        if (tiles.Count == 0) return true;

        Tile first = tiles[0];

        if (tiles.Count >= 3 && tiles[1].Equals(first) && tiles[2].Equals(first))
        {
            var remaining = new List<Tile>(tiles);
            remaining.RemoveRange(0, 3);
            if (CanFormSets(remaining)) return true;

            if (tiles.Count >= 4 && tiles[3].Equals(first))
            {
                remaining = new List<Tile>(tiles);
                remaining.RemoveRange(0, 4);
                if (CanFormSets(remaining)) return true;
            }
        }

        if (first.Suit == Suit.Circles || first.Suit == Suit.Bamboo || first.Suit == Suit.Characters)
        {
            int idx1 = -1, idx2 = -1;
            for (int k = 1; k < tiles.Count; k++)
                if (tiles[k].Suit == first.Suit && tiles[k].Value == first.Value + 1)
                { idx1 = k; break; }
            if (idx1 >= 0)
                for (int k = 1; k < tiles.Count; k++)
                    if (k != idx1 && tiles[k].Suit == first.Suit && tiles[k].Value == first.Value + 2)
                    { idx2 = k; break; }

            if (idx1 >= 0 && idx2 >= 0)
            {
                var remaining = new List<Tile>(tiles);
                remaining.RemoveAt(idx2);
                remaining.RemoveAt(idx1);
                remaining.RemoveAt(0);
                if (CanFormSets(remaining)) return true;
            }
        }

        return false;
    }

    void CheckForMahjong()
    {
        if (!IsWinningHand(GetAllNonBonusTiles())) return;

        GameObject popup = Instantiate(mahjongPopupPrefab, _canvas.transform);
        popup.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        popup.GetComponent<Button>().onClick.AddListener(() => OnMahjong(popup));
    }

    void OnMahjong(GameObject popup)
    {
        for (int i = handContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = handContainer.GetChild(i);
            var highlight = child.Find("Highlight");
            if (highlight != null) highlight.GetComponent<Image>().color = Color.clear;
            Destroy(child.GetComponent<HandTileDrag>());
            Destroy(child.GetComponent<SmoothMove>());
            child.SetParent(scoringBox, false);
        }

        Destroy(popup);
        drawButton.interactable = false;
        tileDisplayText.text = "Mahjong!";
        RefreshHandHighlights();
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    int GetSpriteIndex(Tile tile) => tile.Suit switch
    {
        Suit.Circles    => 0  + (tile.Value - 1),
        Suit.Bamboo     => 9  + (tile.Value - 1),
        Suit.Characters => 18 + (tile.Value - 1),
        Suit.Wind       => 27 + (tile.Value - 1),
        Suit.Dragon     => 31 + (tile.Value - 1),
        Suit.Flower     => 34 + (tile.Value - 1),
        Suit.Season     => 38 + (tile.Value - 1),
        _               => 0
    };

    void UpdateWallCount() => wallCountText.text = $"Tiles remaining: {_wall.Remaining}";

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
