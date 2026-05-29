using UnityEngine;

public class SmoothMove : MonoBehaviour
{
    private RectTransform _rectTransform;
    private Vector2 _targetPosition;
    private bool _isMoving = false;
    public float smoothSpeed = 12f;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _targetPosition = _rectTransform.anchoredPosition;
    }

    void Update()
    {
        if (!_isMoving) return;

        _rectTransform.anchoredPosition = Vector2.Lerp(
            _rectTransform.anchoredPosition,
            _targetPosition,
            Time.deltaTime * smoothSpeed
        );

        if (Vector2.Distance(_rectTransform.anchoredPosition, _targetPosition) < 0.5f)
        {
            _rectTransform.anchoredPosition = _targetPosition;
            _isMoving = false;
        }
    }

    public void MoveTo(Vector2 target)
    {
        _targetPosition = target;
        _isMoving = true;
    }

    public void TeleportTo(Vector2 target)
    {
        _targetPosition = target;
        _rectTransform.anchoredPosition = target;
        _isMoving = false;
    }
}