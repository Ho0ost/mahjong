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

    private readonly List<GameObject> _activeMeldPopups = new();

    static Color HexColor(string hex)
    { 
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    
    private int _targetHandSize = 13;
    public int TargetHandSize => _targetHandSize;

    private Wall _wall = new Wall();
    private Canvas _canvas;

    void Awake()
    {
        Instance = this;
    }

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

    void OnDrawButton()
    {
        int handSize = handContainer.childCount;

        if (handSize == 0)
        {
            for (int i = 0; i < 13; i++)
            {
                if (_wall.TryDraw(out Tile tile))
                    AddTileToHand(tile);
            }
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

    void SortHand()
    {
        int count = handContainer.childCount;

        // Pair each child with a numeric sort key
        var tiles = new System.Collections.Generic.List<(Transform obj, int key)>();
        for (int i = 0; i < count; i++)
        {
            Transform child = handContainer.GetChild(i);
            HandTileData data = child.GetComponent<HandTileData>();
            int key = data != null ? SortKey(data.tile) : int.MaxValue;
            tiles.Add((child, key));
        }

        tiles.Sort((a, b) => a.key.CompareTo(b.key));

        // Apply the new order by updating sibling indices
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

    void AddTileToScoringBox(Tile tile)
    {
        GameObject tileObj = Instantiate(handTilePrefab, scoringBox);
        tileObj.GetComponent<Image>().sprite = tileSprites[GetSpriteIndex(tile)];
        tileObj.AddComponent<HandTileData>().tile = tile;
        Destroy(tileObj.GetComponent<HandTileDrag>());
        Destroy(tileObj.GetComponent<SmoothMove>());
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

    public void RefreshHandPositions()
    {
        int count = handContainer.childCount;
        float totalWidth = count * 124f + (count - 1) * 5f;
        float startX = -totalWidth / 2f + 124f / 2f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = handContainer.GetChild(i).GetComponent<RectTransform>();
            SmoothMove sm = rt.GetComponent<SmoothMove>();
            if (sm != null)
                sm.MoveTo(new Vector2(startX + i * 129f, 0));
            else
                rt.anchoredPosition = new Vector2(startX + i * 129f, 0);
        }
    }

    // Called by HandTileDrag.OnEndDrag when the player discards.
    // screenPos      — where the pointer was released, in screen pixels
    // screenVelocity — pointer velocity in screen pixels/second
    public void DiscardTile(GameObject tileObj, Vector2 screenPos, Vector2 screenVelocity)
    {
        // Restore visual state that was changed during dragging
        CanvasGroup cg = tileObj.GetComponent<CanvasGroup>();
        if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = true; }

        // Stop any in-progress SmoothMove animation before handing off
        SmoothMove sm = tileObj.GetComponent<SmoothMove>();
        if (sm != null)
        {
            sm.TeleportTo(tileObj.GetComponent<RectTransform>().anchoredPosition);
            Destroy(sm);
        }

        // Remove drag behavior; tile is now a physics object
        Destroy(tileObj.GetComponent<HandTileDrag>());

        // Convert release position from screen space to DiscardContainer local space
        RectTransform discardRect = discardPhysics.GetComponent<RectTransform>();
        Camera uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            discardRect, screenPos, uiCam, out Vector2 localPos);

        // Convert velocity: screen px/s → canvas px/s, then cap so tiles don't rocket
        Vector2 localVelocity = Vector2.ClampMagnitude(screenVelocity / _canvas.scaleFactor, 700f);

        discardPhysics.Spawn(tileObj, localPos, localVelocity);

        RefreshHandPositions();

        tileDisplayText.text = "Discarded. Draw your next tile.";
        if (_wall.Remaining > 0)
            drawButton.interactable = true;

        UpdateWallCount();
        RefreshHandHighlights();
    }

    public void PushDiscardTilesAt(Vector2 screenPos)
    {
        Camera uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : _canvas.worldCamera;
        RectTransform discardRect = discardPhysics.GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            discardRect, screenPos, uiCam, out Vector2 localPos);
        discardPhysics.PushWithTile(localPos);
    }

    public RectTransform GetDiscardRect()
        => discardPhysics.GetComponent<RectTransform>();

    void UpdateWallCount()
    {
        wallCountText.text = $"Tiles remaining: {_wall.Remaining}";
    }

    int GetSpriteIndex(Tile tile)
    {
        return tile.Suit switch
        {
            Suit.Circles    => 0  + (tile.Value - 1),
            Suit.Bamboo     => 9  + (tile.Value - 1),
            Suit.Characters => 18 + (tile.Value - 1),
            Suit.Wind       => 27 + (tile.Value - 1),
            Suit.Dragon     => 31 + (tile.Value - 1),
            Suit.Flower     => 34 + (tile.Value - 1),
            Suit.Season     => 38 + (tile.Value - 1),
            _ => 0
        };
    }

    // set detection
    public void RefreshHandHighlights()
    {

        foreach (var popup in _activeMeldPopups)
            if (popup != null) Destroy(popup);
        _activeMeldPopups.Clear();

        // maybe fuckt?
        var tileList = new List<HandTileData>();
        for (int i = 0; i < handContainer.childCount; i++)
        {
            var data = handContainer.GetChild(i).GetComponent<HandTileData>();
            if (data != null) tileList.Add(data);
        }
        var tiles = tileList.ToArray();
        int count = tiles.Length;

        // Clear all existing outlines
        foreach (var td in tiles)
        {
            if (td == null) continue;
            var highlight = td.transform.Find("Highlight");
            if (highlight != null)
                highlight.GetComponent<Image>().color = Color.clear;
        }

        var marked = new bool[count];

        // Kong: 4 consecutive identical tiles
        for (int i = 0; i <= count - 4; i++)
        {
            if (tiles[i]?.tile == null) continue;
            if (IsKong(tiles, i))
            {
                for (int k = i; k < i + 4; k++) { marked[k] = true; ApplyHighlight(tiles[k].gameObject, kongColor); }
                var kongTiles = new List<GameObject>();
                for (int k = i; k < i + 4; k++) kongTiles.Add(tiles[k].gameObject);
                SpawnMeldPopup(kongTiles, MeldType.Kong, i, 4);
            }
        }

        // Pong: 3 consecutive identical tiles
        for (int i = 0; i <= count - 3; i++)
        {
            if (marked[i] || marked[i+1] || marked[i+2]) continue;
            if (tiles[i]?.tile == null) continue;
            if (IsPong(tiles, i))
            {
                for (int k = i; k < i + 3; k++) { marked[k] = true; ApplyHighlight(tiles[k].gameObject, pongColor); }
                var pongTiles = new List<GameObject>();
                for (int k = i; k < i + 3; k++) pongTiles.Add(tiles[k].gameObject);
                SpawnMeldPopup(pongTiles, MeldType.Pong, i, 3);
            }
        }

        // Chow: 3 consecutive tiles of same number suit, ascending values
        for (int i = 0; i <= count - 3; i++)
        {
            if (marked[i] || marked[i+1] || marked[i+2]) continue;
            if (tiles[i]?.tile == null) continue;
            if (IsChow(tiles, i))
            {
                for (int k = i; k < i + 3; k++) { marked[k] = true; ApplyHighlight(tiles[k].gameObject, chowColor); }
                var chowTiles = new List<GameObject>();
                for (int k = i; k < i + 3; k++) chowTiles.Add(tiles[k].gameObject);
                SpawnMeldPopup(chowTiles, MeldType.Chow, i, 3);
            }
        }
    }

    void SpawnMeldPopup(List<GameObject> meldTiles, MeldType type, int startIndex, int length)
    {
        Camera uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        int count = handContainer.childCount;
        float totalWidth = count * 124f + (count - 1) * 5f;
        float startX = -totalWidth / 2f + 124f / 2f;
        int centerIndex = startIndex + (length - 1) / 2;
        float centerX = startX + centerIndex * 129f;

        Vector3 worldPos = handContainer.TransformPoint(new Vector3(centerX, 0f, 0f));
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            handPopupLayer, screenPos, uiCam, out Vector2 localPos);
            
        localPos.y += meldPopupYOffset;

        GameObject popup = Instantiate(meldPopupPrefab, handPopupLayer);
        Sprite icon = type switch
        {
            MeldType.Pong => pongSprite,
            MeldType.Kong => kongSprite,
            MeldType.Chow => chowSprite,
            _             => null
        };
        popup.GetComponent<Image>().sprite = icon;

        popup.GetComponent<RectTransform>().anchoredPosition = localPos;

        var captured = meldTiles;
        popup.GetComponent<Button>().onClick.AddListener(() => ClaimMeld(captured));
        _activeMeldPopups.Add(popup);

    }

    void ClaimMeld(List<GameObject> meldTiles)
    {
        foreach (GameObject tileObj in meldTiles)
        {
            // Clear highlight
            var highlight = tileObj.transform.Find("Highlight");
            if (highlight != null)
                highlight.GetComponent<Image>().color = Color.clear;

            // Strip hand-only components
            Destroy(tileObj.GetComponent<HandTileDrag>());
            Destroy(tileObj.GetComponent<SmoothMove>());


            tileObj.transform.SetParent(scoringBox, false);
        }

        _targetHandSize -= meldTiles.Count;

        drawButton.interactable = _wall.Remaining > 0;
        UpdateWallCount();

        RefreshHandPositions();
        RefreshHandHighlights(); // also clears the popup
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

    void ApplyHighlight(GameObject obj, Color color)
    {
        var highlight = obj.transform.Find("Highlight");
        if (highlight != null)
            highlight.GetComponent<Image>().color = color;
    }

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
            if (i > 0 && tiles[i].Equals(tiles[i - 1])) continue; // skip duplicate pair attempts

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

        // Try pong (3 same)
        if (tiles.Count >= 3 && tiles[1].Equals(first) && tiles[2].Equals(first))
        {
            var remaining = new List<Tile>(tiles);
            remaining.RemoveRange(0, 3);
            if (CanFormSets(remaining)) return true;

            // Also try kong (4 same) if available
            if (tiles.Count >= 4 && tiles[3].Equals(first))
            {
                remaining = new List<Tile>(tiles);
                remaining.RemoveRange(0, 4);
                if (CanFormSets(remaining)) return true;
            }
        }

        // Try chow (3 consecutive, number suits only)
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
        // Move all remaining hand tiles to scoring box
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
}
