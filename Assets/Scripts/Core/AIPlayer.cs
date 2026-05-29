using System.Collections.Generic;
public enum SeatPosition { Left, Top, Right }

public class AIPlayer
{
    public SeatPosition Seat { get; private set; }
    public List<Tile> Hand { get; private set; } = new();

    private System.Random _rng;

    public AIPlayer(SeatPosition seat, System.Random rng)
    {
        Seat = seat;
        _rng = rng;
    }

    public void DrawTile(Tile tile)
    {
        Hand.Add(tile);
    }

    public Tile Discard()
    {
        int index = _rng.Next(Hand.Count);
        Tile tile = Hand[index];
        Hand.RemoveAt(index);
        return tile;
    }
}