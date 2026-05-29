using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Collections;

public enum MeldType  { Pong, Kong, Chow }
public enum GameState { WaitingToStart, HumanDraw, HumanDiscard, AITurn, ClaimWindow, GameOver }

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
    [SerializeField] private Color lastDiscardGlowColor = HexColor("#ffe066");
    [SerializeField] private GameObject discardedTilePrefab;

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

    [Header("Claim")]
    [SerializeField] private GameObject claimPopupPrefab;

    [Header("Opponents")]
    [SerializeField] private Transform leftOpponentHand;
    [SerializeField] private Transform topOpponentHand;
    [SerializeField] private Transform rightOpponentHand;
    [SerializeField] private GameObject tileBackPrefab;

    private const float TileWidth   = 124f;
    private const float TileSpacing = 5f;
    private const float TileStep    = TileWidth + TileSpacing;

    private readonly List<GameObject> _activeMeldPopups = new();
    private int _targetHandSize = 13;
    public int TargetHandSize => _targetHandSize;

    private Wall _wall = new Wall();
    private Canvas _canvas;
    private GameState _state = GameState.WaitingToStart;
    public GameState State => _state;

    private AIPlayer   _leftPlayer,  _topPlayer,  _rightPlayer;
    private OpponentHandDisplay _leftDisplay, _topDisplay, _rightDisplay;
    private AIPlayer[] _aiTurnOrder;
    private int        _aiTurnIndex;

    private GameObject _lastDiscardedTileObj;
    private Tile       _claimTile;
    private GameObject _activeClaimPopup;
    private bool       _canMeldClaim;
    private bool       _canMahjongClaim;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake() => Instance = this;

    void Start()
    {
        _canvas = FindAnyObjectByType<Canvas>();
        _wall.Build();
        var rng = new System.Random();
        _wall.Shuffle(rng);

        _leftPlayer  = new AIPlayer(SeatPosition.Left,  rng);
        _topPlayer   = new AIPlayer(SeatPosition.Top,   rng);
        _rightPlayer = new AIPlayer(SeatPosition.Right, rng);
        _aiTurnOrder = new AIPlayer[] { _rightPlayer, _topPlayer, _leftPlayer };

        _leftDisplay  = leftOpponentHand.gameObject.AddComponent<OpponentHandDisplay>();
        _topDisplay   = topOpponentHand.gameObject.AddComponent<OpponentHandDisplay>();
        _rightDisplay = rightOpponentHand.gameObject.AddComponent<OpponentHandDisplay>();
        _leftDisplay.Setup(_leftPlayer,   tileBackPrefab);
        _topDisplay.Setup(_topPlayer,     tileBackPrefab);
        _rightDisplay.Setup(_rightPlayer, tileBackPrefab);

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
            foreach (AIPlayer ai in _aiTurnOrder)
            {
                for (int i = 0; i < 13; i++)
                    if (_wall.TryDraw(out Tile tile))
                        AIDrawTile(ai, tile);
                GetDisplayFor(ai).Sync();
            }

            for (int i = 0; i < 13; i++)
                if (_wall.TryDraw(out Tile tile))
                    AddTileToHand(tile);

            _state = GameState.HumanDraw;
            tileDisplayText.text = "Hand ready! Draw your 14th tile.";
        }
        else if (handSize == _targetHandSize && _state == GameState.HumanDraw)
        {
            if (_wall.TryDraw(out Tile tile))
            {
                AddTileToHand(tile);
                tileDisplayText.text = "Hand full! Drag a tile up to discard.";
                drawButton.interactable = false;
                _state = GameState.HumanDiscard;
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

        Sprite sprite   = tileObj.GetComponent<Image>().sprite;
        Tile   tileData = tileObj.GetComponent<HandTileData>().tile;
        tileObj.transform.SetParent(null);
        Destroy(tileObj);

        GameObject discardObj = Instantiate(discardedTilePrefab);
        discardObj.GetComponent<Image>().sprite = sprite;
        discardObj.AddComponent<HandTileData>().tile = tileData;

        discardPhysics.Spawn(discardObj, localPos, localVelocity);
        SetLastDiscardGlow(discardObj);

        RefreshHandPositions();
        tileDisplayText.text = "Discarded. Waiting for other players...";
        UpdateWallCount();
        RefreshHandHighlights();

        _aiTurnIndex = 0;
        _state = GameState.AITurn;
        StartCoroutine(RunAITurn(_aiTurnOrder[_aiTurnIndex]));
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
        UpdateWallCount();
        RefreshHandPositions();
        RefreshHandHighlights();
    }

    // ── Claim window ──────────────────────────────────────────────────────────

    void CheckForClaim(Tile tile, bool fromLeftPlayer)
    {
        List<Tile> hand = GetHumanHandTiles();

        bool canPong = hand.FindAll(t => t.Equals(tile)).Count >= 2;
        bool canKong = hand.FindAll(t => t.Equals(tile)).Count >= 3;
        bool canChow = fromLeftPlayer && CanChowWith(hand, tile);
        var handWithClaim = new List<Tile>(hand) { tile };
        bool canMahjong = IsWinningHand(handWithClaim);

        _canMeldClaim    = canPong || canKong || canChow;
        _canMahjongClaim = canMahjong;

        if (_canMeldClaim || _canMahjongClaim)
        {
            _state     = GameState.ClaimWindow;
            _claimTile = tile;
            SpawnClaimPopup();
        }
        else
        {
            AdvanceTurn();
        }
    }

    void SpawnClaimPopup()
    {
        _activeClaimPopup = Instantiate(claimPopupPrefab, _canvas.transform);
        _activeClaimPopup.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        Button claimBtn   = _activeClaimPopup.transform.Find("ClaimButton").GetComponent<Button>();
        Button passBtn    = _activeClaimPopup.transform.Find("PassButton").GetComponent<Button>();
        Button mahjongBtn = _activeClaimPopup.transform.Find("MahjongButton")?.GetComponent<Button>();

        claimBtn.gameObject.SetActive(_canMeldClaim);
        if (mahjongBtn != null)
        {
            mahjongBtn.gameObject.SetActive(_canMahjongClaim);
            mahjongBtn.onClick.AddListener(OnMahjongClaim);
        }

        claimBtn.onClick.AddListener(OnClaim);
        passBtn.onClick.AddListener(OnPass);
    }

    void OnClaim()
    {
        Destroy(_activeClaimPopup);

        List<GameObject> meldGroup = FindMeldGroup(_claimTile);
        AddTileToScoringBox(_claimTile);

        if (meldGroup != null)
        {
            foreach (GameObject tileObj in meldGroup)
            {
                var highlight = tileObj.transform.Find("Highlight");
                if (highlight != null) highlight.GetComponent<Image>().color = Color.clear;
                Destroy(tileObj.GetComponent<HandTileDrag>());
                Destroy(tileObj.GetComponent<SmoothMove>());
                tileObj.transform.SetParent(scoringBox, false);
            }
            _targetHandSize -= meldGroup.Count + 1;
        }

        _state = GameState.HumanDiscard;
        drawButton.interactable = false;
        tileDisplayText.text = "You claimed! Discard a tile to continue.";
        RefreshHandPositions();
        RefreshHandHighlights();
    }

    void OnMahjongClaim()
    {
        Destroy(_activeClaimPopup);
        AddTileToScoringBox(_claimTile);

        for (int i = handContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = handContainer.GetChild(i);
            var highlight = child.Find("Highlight");
            if (highlight != null) highlight.GetComponent<Image>().color = Color.clear;
            Destroy(child.GetComponent<HandTileDrag>());
            Destroy(child.GetComponent<SmoothMove>());
            child.SetParent(scoringBox, false);
        }

        _state = GameState.GameOver;
        drawButton.interactable = false;
        tileDisplayText.text = "Mahjong!";
        RefreshHandHighlights();
    }

    void OnPass()
    {
        Destroy(_activeClaimPopup);
        AdvanceTurn();
    }

    List<GameObject> FindMeldGroup(Tile claimTile)
    {
        var handTiles = new List<(GameObject obj, Tile tile)>();
        for (int i = 0; i < handContainer.childCount; i++)
        {
            var child = handContainer.GetChild(i);
            var data = child.GetComponent<HandTileData>();
            if (data != null) handTiles.Add((child.gameObject, data.tile));
        }

        var matches = handTiles.FindAll(t => t.tile.Equals(claimTile));
        if (matches.Count >= 3) return matches.GetRange(0, 3).ConvertAll(t => t.obj);
        if (matches.Count >= 2) return matches.GetRange(0, 2).ConvertAll(t => t.obj);

        if (claimTile.Suit == Suit.Circles || claimTile.Suit == Suit.Bamboo || claimTile.Suit == Suit.Characters)
        {
            GameObject Find(int value) =>
                handTiles.Find(t => t.tile.Suit == claimTile.Suit && t.tile.Value == value).obj;

            int v = claimTile.Value;
            var a = Find(v - 2); var b = Find(v - 1);
            if (a != null && b != null) return new List<GameObject> { a, b };

            var c = Find(v - 1); var d = Find(v + 1);
            if (c != null && d != null) return new List<GameObject> { c, d };

            var e = Find(v + 1); var f = Find(v + 2);
            if (e != null && f != null) return new List<GameObject> { e, f };
        }

        return null;
    }

    List<Tile> GetHumanHandTiles()
    {
        var result = new List<Tile>();
        for (int i = 0; i < handContainer.childCount; i++)
        {
            var data = handContainer.GetChild(i).GetComponent<HandTileData>();
            if (data != null) result.Add(data.tile);
        }
        return result;
    }

    bool CanChowWith(List<Tile> hand, Tile tile)
    {
        if (tile.Suit == Suit.Wind   || tile.Suit == Suit.Dragon ||
            tile.Suit == Suit.Flower || tile.Suit == Suit.Season) return false;

        bool Has(int v) => hand.Exists(t => t.Suit == tile.Suit && t.Value == v);

        return (tile.Value >= 3 && Has(tile.Value - 2) && Has(tile.Value - 1))
            || (tile.Value >= 2 && tile.Value <= 8 && Has(tile.Value - 1) && Has(tile.Value + 1))
            || (tile.Value <= 7 && Has(tile.Value + 1) && Has(tile.Value + 2));
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
        _state = GameState.GameOver;
        drawButton.interactable = false;
        tileDisplayText.text = "Mahjong!";
        RefreshHandHighlights();
    }

    // ── AI turns ──────────────────────────────────────────────────────────────

    IEnumerator RunAITurn(AIPlayer ai)
    {
        yield return new WaitForSeconds(0.6f);

        if (_wall.TryDraw(out Tile drawn))
            AIDrawTile(ai, drawn);
        GetDisplayFor(ai).Sync();

        yield return new WaitForSeconds(0.8f);

        if (ai.Hand.Count == 0) { AdvanceTurn(); yield break; }

        Tile discarded = ai.Discard();
        GetDisplayFor(ai).Sync();
        SpawnAIDiscard(discarded);

        CheckForClaim(discarded, ai.Seat == SeatPosition.Left);
    }

    void SpawnAIDiscard(Tile tile)
    {
        GameObject tileObj = Instantiate(discardedTilePrefab);
        tileObj.GetComponent<Image>().sprite = tileSprites[GetSpriteIndex(tile)];
        tileObj.AddComponent<HandTileData>().tile = tile;
        Destroy(tileObj.GetComponent<HandTileDrag>());
        Destroy(tileObj.GetComponent<SmoothMove>());

        Rect b = discardPhysics.GetComponent<RectTransform>().rect;
        Vector2 localPos = new Vector2(
            UnityEngine.Random.Range(b.xMin * 0.5f, b.xMax * 0.5f),
            UnityEngine.Random.Range(b.yMin * 0.5f, b.yMax * 0.5f)
        );
        Vector2 velocity = new Vector2(
            UnityEngine.Random.Range(-150f, 150f),
            UnityEngine.Random.Range(-150f, 150f)
        );

        discardPhysics.Spawn(tileObj, localPos, velocity);
        SetLastDiscardGlow(tileObj);
    }

    void AdvanceTurn()
    {
        _aiTurnIndex++;

        if (_aiTurnIndex < _aiTurnOrder.Length)
        {
            StartCoroutine(RunAITurn(_aiTurnOrder[_aiTurnIndex]));
        }
        else
        {
            _aiTurnIndex = 0;
            _state = GameState.HumanDraw;
            if (_wall.Remaining > 0) drawButton.interactable = true;
            tileDisplayText.text = "Your turn! Draw a tile.";
            UpdateWallCount();
        }
    }

    void AIDrawTile(AIPlayer ai, Tile tile)
    {
        if (tile.IsBonusTile)
        {
            if (_wall.TryDraw(out Tile replacement))
                AIDrawTile(ai, replacement);
            return;
        }
        ai.DrawTile(tile);
    }

    OpponentHandDisplay GetDisplayFor(AIPlayer ai) => ai.Seat switch
    {
        SeatPosition.Left  => _leftDisplay,
        SeatPosition.Top   => _topDisplay,
        SeatPosition.Right => _rightDisplay,
        _                  => null
    };

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

    void SetLastDiscardGlow(GameObject tileObj)
    {
        if (_lastDiscardedTileObj != null)
        {
            var old = _lastDiscardedTileObj.transform.Find("Highlight");
            if (old != null) old.GetComponent<Image>().color = Color.clear;
        }

        var highlight = tileObj.transform.Find("Highlight");
        if (highlight != null) highlight.GetComponent<Image>().color = lastDiscardGlowColor;

        _lastDiscardedTileObj = tileObj;
    }

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
