using UnityEngine;
using UnityEngine.EventSystems;

public class DiscardTileDrag : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform _rectTransform;
    private RectTransform _containerRect;
    private CanvasGroup   _canvasGroup;
    private Canvas        _canvas;
    private DiscardedTile _discardedTile;
    private Vector2       _lastPointerPos;
    private Vector2       _velocity;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _containerRect = transform.parent.GetComponent<RectTransform>();
        _canvasGroup   = GetComponent<CanvasGroup>();
        _canvas        = GetComponentInParent<Canvas>();
        _discardedTile = GetComponent<DiscardedTile>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _discardedTile.isDragged = true;
        _discardedTile.velocity  = Vector2.zero;

        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.alpha          = 0.8f;
        transform.SetAsLastSibling(); // visually on top

        _velocity       = Vector2.zero;
        _lastPointerPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Move with the pointer
        _rectTransform.anchoredPosition += eventData.delta / _canvas.scaleFactor;

        // Clamp to container bounds using the tile's own size
        Rect   bounds = _containerRect.rect;
        float  halfW  = _rectTransform.rect.width  * 0.5f;
        float  halfH  = _rectTransform.rect.height * 0.5f;
        Vector2 pos   = _rectTransform.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, bounds.xMin + halfW, bounds.xMax - halfW);
        pos.y = Mathf.Clamp(pos.y, bounds.yMin + halfH, bounds.yMax - halfH);
        _rectTransform.anchoredPosition = pos;

        // Push other tiles in the discard pile
        GameController.Instance.PushDiscardTilesAt(eventData.position);

        // Track velocity for the release throw
        if (Time.deltaTime > 0f)
            _velocity = (eventData.position - _lastPointerPos) / Time.deltaTime;
        _lastPointerPos = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.alpha          = 1f;

        // Hand velocity back to the physics system and release control
        _discardedTile.velocity = _velocity / _canvas.scaleFactor;
        _discardedTile.isDragged = false;
    }
}