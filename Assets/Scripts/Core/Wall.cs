using System.Collections.Generic;

public enum Suit { Characters, Bamboo, Circles, Wind, Dragon, Flower, Season }

public enum FlowerType { Plum, Orchid, Chrysanthemum, BambooFlower }

public enum SeasonType { Spring, Summer, Autumn, Winter }

public enum TileModifier
{
    GoldenTile,
    WildSuit,
    Sticky,
    Lucky,
}

public class Tile
{
    public Suit Suit;
    public int Value;
    public int Level = 1;
    public List<TileModifier> Modifiers = new();
    public FlowerType? FlowerType;
    public SeasonType? SeasonType;

    public Tile(Suit suit, int value)
    {
        Suit = suit;
        Value = value;
    }

    public bool Equals(Tile other) => other != null && Suit == other.Suit && Value == other.Value;

    public bool IsBonusTile => Suit == Suit.Flower || Suit == Suit.Season;

    public override string ToString() => $"{Suit} {Value} (lv.{Level})";
}

public class Wall
{
    private List<Tile> _tiles = new();

    public int Remaining => _tiles.Count;

    public void Build()
    {
        _tiles.Clear();
        foreach (Suit s in System.Enum.GetValues(typeof(Suit)))
        {
            int max = s switch
            {
                Suit.Wind       => 4,
                Suit.Dragon     => 3,
                Suit.Characters => 9,
                Suit.Bamboo     => 9,
                Suit.Circles    => 9,
                Suit.Flower     => 0,
                Suit.Season     => 0,
                _ => throw new System.Exception($"Unknown suit: {s}")
            };
            for (int v = 1; v <= max; v++)
                for (int copy = 0; copy < 4; copy++)
                    _tiles.Add(new Tile(s, v));
        }

        _tiles.Add(new Tile(Suit.Flower, 1) { FlowerType = FlowerType.Plum });
        _tiles.Add(new Tile(Suit.Flower, 2) { FlowerType = FlowerType.Orchid });
        _tiles.Add(new Tile(Suit.Flower, 3) { FlowerType = FlowerType.Chrysanthemum });
        _tiles.Add(new Tile(Suit.Flower, 4) { FlowerType = FlowerType.BambooFlower });

        // Seasons - one copy each
        _tiles.Add(new Tile(Suit.Season, 1) { SeasonType = SeasonType.Spring });
        _tiles.Add(new Tile(Suit.Season, 2) { SeasonType = SeasonType.Summer });
        _tiles.Add(new Tile(Suit.Season, 3) { SeasonType = SeasonType.Autumn });
        _tiles.Add(new Tile(Suit.Season, 4) { SeasonType = SeasonType.Winter });
    }

    public void Shuffle(System.Random rng)
    {
        for (int i = _tiles.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (_tiles[i], _tiles[j]) = (_tiles[j], _tiles[i]);
        }
    }

    public bool TryDraw(out Tile tile)
    {
        if (_tiles.Count == 0) { tile = null; return false; }
        tile = _tiles[^1];
        _tiles.RemoveAt(_tiles.Count - 1);
        return true;
    }
}