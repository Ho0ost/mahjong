using UnityEngine;

public class OpponentHandDisplay : MonoBehaviour
{
    private AIPlayer _player;
    private GameObject _tileBackPrefab;

    public void Setup(AIPlayer player, GameObject tileBackPrefab)
    {
        _player = player;
        _tileBackPrefab = tileBackPrefab;
    }

    public void Sync()
    {
        int target = _player.Hand.Count;
        int current = transform.childCount;

        for (int i = current; i < target; i++)
            Instantiate(_tileBackPrefab, transform);

        for (int i = current - 1; i >= target; i--)
            Destroy(transform.GetChild(i).gameObject);
    }
}