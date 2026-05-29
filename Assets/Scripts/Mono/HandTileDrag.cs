using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class HandTileDrag : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{

    private Transform _handContainer;
    private CanvasGroup _canvasGroup;
    private RectTransform _rectTransform;
    private Canvas _canvas;
    private int _currentIndex;

    private Vector2 _velocity;        // screen pixels/second, tracked during drag
    private Vector2 _lastPointerPos;

    private const float TileWidth   = 124f;
    private const float TileSpacing = 5f;
    private const float TileStep    = TileWidth + TileSpacing;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _canvasGroup   = gameObject.AddComponent<CanvasGroup>();
        _canvas        = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _handContainer = transform.parent;
        _currentIndex  = transform.GetSiblingIndex();

        _canvasGroup.alpha        = 0.8f;
        _canvasGroup.blocksRaycasts = false;

        transform.SetAsLastSibling();
        _rectTransform.anchoredPosition += new Vector2(0, 40f);

        _velocity       = Vector2.zero;
        _lastPointerPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        _rectTransform.anchoredPosition += new Vector2(
            eventData.delta.x / _canvas.scaleFactor,
            eventData.delta.y / _canvas.scaleFactor);

        // Rolling velocity estimate in screen pixels/second
        if (Time.deltaTime > 0f)
            _velocity = (eventData.position - _lastPointerPos) / Time.deltaTime;
        _lastPointerPos = eventData.position;

        int newIndex = GetIndexFromPosition(_rectTransform.anchoredPosition.x);
        newIndex = Mathf.Clamp(newIndex, 0, _handContainer.childCount - 1);

        if (newIndex != _currentIndex)
        {
            ShiftTiles(_currentIndex, newIndex);
            _currentIndex = newIndex;
            transform.SetSiblingIndex(_currentIndex);
        }

        if (IsTileFullyInDiscardZone())
            GameController.Instance.PushDiscardTilesAt(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        bool inDiscardZone = IsTileFullyInDiscardZone();
        bool handIsFull = _handContainer.childCount == GameController.Instance.TargetHandSize + 1;

        bool canDiscard = inDiscardZone 
            && handIsFull 
            && GameController.Instance.State == GameState.HumanDiscard;

        if (canDiscard)
        {
            GameController.Instance.DiscardTile(gameObject, eventData.position, _velocity);
            return;
        }

        // Normal snap-back to hand slot
        int count = _handContainer.childCount;
        float totalWidth = count * TileWidth + (count - 1) * TileSpacing;
        float startX = -totalWidth / 2f + TileWidth / 2f;

        _rectTransform.anchoredPosition = new Vector2(
            startX + _currentIndex * TileStep, 0);

        _canvasGroup.alpha          = 1f;
        _canvasGroup.blocksRaycasts = true;
        
        GameController.Instance.RefreshHandHighlights();
    }

    int GetIndexFromPosition(float x)
    {
        int count = _handContainer.childCount;
        float totalWidth = count * TileWidth + (count - 1) * TileSpacing;
        float startX = -totalWidth / 2f + TileWidth / 2f;
        return Mathf.RoundToInt((x - startX) / TileStep);
    }

    void ShiftTiles(int fromIndex, int toIndex)
    {
        int count = _handContainer.childCount;
        float totalWidth = count * TileWidth + (count - 1) * TileSpacing;
        float startX = -totalWidth / 2f + TileWidth / 2f;

        int assignedIndex = 0;
        for (int i = 0; i < count; i++)
        {
            Transform child = _handContainer.GetChild(i);
            if (child == transform) continue;

            if (assignedIndex == toIndex) assignedIndex++;

            SmoothMove sm = child.GetComponent<SmoothMove>();
            if (sm != null)
                sm.MoveTo(new Vector2(startX + assignedIndex * TileStep, 0));

            assignedIndex++;
        }
    }

    bool IsTileFullyInDiscardZone()
    {
        RectTransform discardRect = GameController.Instance.GetDiscardRect();

        Vector3[] tileCorners    = new Vector3[4];
        Vector3[] discardCorners = new Vector3[4];
        _rectTransform.GetWorldCorners(tileCorners);
        discardRect.GetWorldCorners(discardCorners);

        // GetWorldCorners always returns: [0]=bottom-left, [1]=top-left,
        //                                 [2]=top-right,   [3]=bottom-right
        float xMin = discardCorners[0].x, yMin = discardCorners[0].y;
        float xMax = discardCorners[2].x, yMax = discardCorners[2].y;

        foreach (Vector3 corner in tileCorners)
        {
            if (corner.x < xMin || corner.x > xMax ||
                corner.y < yMin || corner.y > yMax)
                return false;
        }
        return true;
    }
}
