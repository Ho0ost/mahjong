using System.Collections.Generic;
using UnityEngine;

// Attach to the DiscardContainer. Simulates sliding + bumping for discarded tiles.
public class DiscardPhysics : MonoBehaviour
{
    [Header("Motion")]
    [Tooltip("How quickly tiles slow down. Higher = stops faster.")]
    [SerializeField] private float damping = 2.5f;

    [Header("Collisions")]
    [Tooltip("0 = no bounce, 1 = fully elastic. 0.4-0.6 feels like bumper cars.")]
    [SerializeField] [Range(0f, 1f)] private float restitution = 0.5f;
    [Tooltip("How many collision-resolution passes per frame. More = more stable with many tiles.")]
    [SerializeField] private int solverIterations = 4;

    [Header("Tile Size (match your prefab's RectTransform)")]
    [SerializeField] private float tileHalfWidth  = 62f;
    [SerializeField] private float tileHalfHeight = 82f;
    [SerializeField] private float dragPushSpeed = 250f;

    private RectTransform _container;
    private readonly List<DiscardedTile> _tiles = new();

    void Awake()
    {
        _container = GetComponent<RectTransform>();
    }

    // Called by GameController when the player discards a tile.
    // localPos  — position already in this container's local space
    // velocity  — canvas-space pixels per second
    public void Spawn(GameObject tileObj, Vector2 localPos, Vector2 velocity)
    {
        // Keep the tile inside the container from the start
        Rect b = _container.rect;
        localPos.x = Mathf.Clamp(localPos.x, b.xMin + tileHalfWidth,  b.xMax - tileHalfWidth);
        localPos.y = Mathf.Clamp(localPos.y, b.yMin + tileHalfHeight, b.yMax - tileHalfHeight);

        tileObj.transform.SetParent(transform, false);
        tileObj.GetComponent<RectTransform>().anchoredPosition = localPos;

        DiscardedTile dt = tileObj.AddComponent<DiscardedTile>();
        dt.velocity = velocity;
        _tiles.Add(dt);
        tileObj.AddComponent<DiscardTileDrag>();
    }

    public void PushWithTile(Vector2 localPos)
    {
        foreach (DiscardedTile dt in _tiles)
        {
            if (dt == null || dt.isDragged) continue;

            RectTransform rt = dt.GetComponent<RectTransform>();
            Vector2 pos = rt.anchoredPosition;
            float dx = pos.x - localPos.x;
            float dy = pos.y - localPos.y;

            float overlapX = 2f * tileHalfWidth  - Mathf.Abs(dx);
            float overlapY = 2f * tileHalfHeight - Mathf.Abs(dy);

            if (overlapX <= 0f || overlapY <= 0f) continue;

            Vector2 normal;
            float overlap;
            if (overlapX < overlapY)
            { normal = new Vector2(Mathf.Sign(dx), 0f); overlap = overlapX; }
            else
            { normal = new Vector2(0f, Mathf.Sign(dy)); overlap = overlapY; }

            // Separate the tile so it no longer overlaps
            rt.anchoredPosition = pos + normal * overlap;

            // Nudge it away — but only up to dragPushSpeed so it can't accumulate
            float currentSpeed = Vector2.Dot(dt.velocity, normal);
            if (currentSpeed < dragPushSpeed)
                dt.velocity += normal * (dragPushSpeed - currentSpeed);

            BounceOffWalls(rt, dt);
        }
    }

    void Update()
    {
        _tiles.RemoveAll(t => t == null);

        // --- integrate velocity ---
        foreach (DiscardedTile dt in _tiles)
        {
            if (dt.isDragged || dt.velocity.sqrMagnitude < 4f)
            {
                dt.velocity = Vector2.zero;
                continue;
            }

            RectTransform rt = dt.GetComponent<RectTransform>();
            rt.anchoredPosition += dt.velocity * Time.deltaTime;

            // Exponential decay — frame-rate independent
            dt.velocity *= Mathf.Exp(-damping * Time.deltaTime);

            BounceOffWalls(rt, dt);
        }

        // --- resolve tile-tile collisions ---
        // We run multiple iterations so a chain of collisions fully propagates
        for (int iter = 0; iter < solverIterations; iter++)
        {
            for (int i = 0; i < _tiles.Count; i++)
                for (int j = i + 1; j < _tiles.Count; j++)
                    if (_tiles[i] != null && _tiles[j] != null)
                        ResolveCollision(_tiles[i], _tiles[j]);
        }
    }

    // Bounce a tile off the container edges.
    void BounceOffWalls(RectTransform rt, DiscardedTile dt)
    {
        Rect b = _container.rect;
        Vector2 pos = rt.anchoredPosition;
        bool moved = false;

        if (pos.x - tileHalfWidth < b.xMin)
        {
            pos.x = b.xMin + tileHalfWidth;
            dt.velocity.x = Mathf.Abs(dt.velocity.x) * restitution;
            moved = true;
        }
        else if (pos.x + tileHalfWidth > b.xMax)
        {
            pos.x = b.xMax - tileHalfWidth;
            dt.velocity.x = -Mathf.Abs(dt.velocity.x) * restitution;
            moved = true;
        }

        if (pos.y - tileHalfHeight < b.yMin)
        {
            pos.y = b.yMin + tileHalfHeight;
            dt.velocity.y = Mathf.Abs(dt.velocity.y) * restitution;
            moved = true;
        }
        else if (pos.y + tileHalfHeight > b.yMax)
        {
            pos.y = b.yMax - tileHalfHeight;
            dt.velocity.y = -Mathf.Abs(dt.velocity.y) * restitution;
            moved = true;
        }

        if (moved) rt.anchoredPosition = pos;
    }

    // AABB collision between two tiles.
    // Pushes them apart and exchanges velocity along the collision axis.
    void ResolveCollision(DiscardedTile a, DiscardedTile b)
    {
        if (a.isDragged || b.isDragged) return;

        RectTransform rtA = a.GetComponent<RectTransform>();
        RectTransform rtB = b.GetComponent<RectTransform>();

        Vector2 posA = rtA.anchoredPosition;
        Vector2 posB = rtB.anchoredPosition;

        float dx = posA.x - posB.x;
        float dy = posA.y - posB.y;

        float overlapX = 2f * tileHalfWidth  - Mathf.Abs(dx);
        float overlapY = 2f * tileHalfHeight - Mathf.Abs(dy);

        if (overlapX <= 0f || overlapY <= 0f) return; // no overlap

        // Separate along the axis of smallest overlap (minimum separation vector)
        Vector2 normal;
        float overlap;

        if (overlapX < overlapY)
        {
            normal  = new Vector2(Mathf.Sign(dx), 0f);
            overlap = overlapX;
        }
        else
        {
            normal  = new Vector2(0f, Mathf.Sign(dy));
            overlap = overlapY;
        }

        // Push positions apart so they no longer overlap
        rtA.anchoredPosition = posA + normal * (overlap * 0.5f);
        rtB.anchoredPosition = posB - normal * (overlap * 0.5f);

        // Apply velocity impulse only if tiles are approaching each other
        float relVel = Vector2.Dot(a.velocity - b.velocity, normal);
        if (relVel >= 0f) return; // already moving apart

        // Equal-mass elastic impulse scaled by restitution
        float impulse = -(1f + restitution) * relVel * 0.5f;
        a.velocity += impulse * normal;
        b.velocity -= impulse * normal;
    } 
}
