using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WFCGenerator : MonoBehaviour
{
    [Header("Grid Size")] public int Width = 20, Height = 20;
    [Header("Seed (0 = random)")] public int Seed = 0;

    public GameObject Ground;
    public GameObject Bridge, BridgeLarge, BridgeSmall;
    public GameObject Castle, Column;
    public GameObject DoubleDoor, SimpleDoor;
    public GameObject HouseA, HouseB;
    public GameObject TowerA, TowerB;
    public GameObject Wall;

    private GameObject[] _prefab;
    private Dictionary<Tile, Dictionary<Vector2Int, Tile[]>> _adj;
    private Dictionary<Tile, Vector2Int> _foot;
    private bool[,,] _wave;
    private System.Random _rng;

    private enum Tile
    {
        Ground,
        Bridge, BridgeLarge, BridgeSmall,
        Castle, Column,
        DoubleDoor, SimpleDoor,
        HouseA, HouseB,
        TowerA, TowerB,
        Wall
    }

    private static readonly Vector2Int[] DIR = 
    { 
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left 
    };

    void Awake()
    {
        _prefab = new[]
        {
            Ground,
            Bridge, BridgeLarge, BridgeSmall,
            Castle, Column,
            DoubleDoor, SimpleDoor,
            HouseA, HouseB,
            TowerA, TowerB,
            Wall
        };

        _foot = new()
        {
            { Tile.Ground,      new(1,1) },
            { Tile.Bridge,      new(1,1) },
            { Tile.BridgeSmall, new(1,1) },
            { Tile.BridgeLarge, new(2,1) },
            { Tile.Castle,      new(2,2) },
            { Tile.Column,      new(1,1) },
            { Tile.DoubleDoor,  new(1,1) },
            { Tile.SimpleDoor,  new(1,1) },
            { Tile.HouseA,      new(2,2) },
            { Tile.HouseB,      new(2,2) },
            { Tile.TowerA,      new(1,1) },
            { Tile.TowerB,      new(1,1) },
            { Tile.Wall,        new(1,1) },
        };

        BuildAdjacency();
    }

    void Start()
    {
        _rng = (Seed == 0) ? new System.Random() : new System.Random(Seed);

        InitWave();
        SeedBorderWalls();
        SeedCentralCastle();

        if (!Propagate() || !Collapse())
        {
            Debug.LogError("WFC failed to converge (check neighbour lists)");
            return;
        }

        InstantiateResult();
    }

    void BuildAdjacency()
    {
        _adj = new();
        foreach (Tile t in Enum.GetValues(typeof(Tile))) _adj[t] = new();

        var G  = Tile.Ground;
        var B  = Tile.Bridge;
        var BL = Tile.BridgeLarge;
        var BS = Tile.BridgeSmall;
        var C  = Tile.Castle;
        var Col= Tile.Column;
        var SD = Tile.SimpleDoor;
        var DD = Tile.DoubleDoor;
        var HA = Tile.HouseA;
        var HB = Tile.HouseB;
        var TA = Tile.TowerA;
        var TB = Tile.TowerB;
        var W  = Tile.Wall;

        void Allow(Tile a, Vector2Int dir, params Tile[] n)
        {
            _adj[a][dir] = n;
            var opp = new Vector2Int(-dir.x, -dir.y);
            foreach (var b in n)
            {
                if (!_adj[b].ContainsKey(opp)) _adj[b][opp] = new[] { a };
                else if (!_adj[b][opp].Contains(a))
                    _adj[b][opp] = _adj[b][opp].Append(a).ToArray();
            }
        }

        Tile[] ANY       = (Tile[])Enum.GetValues(typeof(Tile));
        Tile[] BRIDGES   = { B, BL, BS };
        Tile[] BRIDGE_EW = new[] { G, W, Col, TA, TB }.Concat(BRIDGES).ToArray();
        Tile[] COLUMN_NS = new[] { G, W, C, TA, TB, Col, SD, DD }.Concat(BRIDGES).ToArray();
        Tile[] DOOR_EW   = { W, G, SD, DD };
        Tile[] TOWER_ALL = new[] { G, W, C, Col, SD, DD }.Concat(BRIDGES).Concat(new[] { TA, TB }).ToArray();
        Tile[] CASTLE_ALL= new[] { G, C, W, Col, SD, DD, TA, TB }.Concat(BRIDGES).ToArray();
        Tile[] HOUSE_ALL = new[] { G, C, Col, HA, HB }.Concat(BRIDGES).ToArray();

        foreach (var dir in DIR) { Allow(G, dir, ANY); Allow(W, dir, ANY); }

        foreach (var b in BRIDGES)
        {
            Allow(b, Vector2Int.left,  BRIDGE_EW);
            Allow(b, Vector2Int.right, BRIDGE_EW);
            Allow(b, Vector2Int.up,    ANY);
            Allow(b, Vector2Int.down,  ANY);
        }

        foreach (var d in new[] { SD, DD })
        {
            Allow(d, Vector2Int.left,  DOOR_EW);
            Allow(d, Vector2Int.right, DOOR_EW);
            Allow(d, Vector2Int.up,    ANY);
            Allow(d, Vector2Int.down,  ANY);
        }

        Allow(Col, Vector2Int.up,    COLUMN_NS);
        Allow(Col, Vector2Int.down,  COLUMN_NS);
        Allow(Col, Vector2Int.left,  ANY);
        Allow(Col, Vector2Int.right, ANY);

        foreach (var dir in DIR) { Allow(TA, dir, TOWER_ALL); Allow(TB, dir, TOWER_ALL); }
        foreach (var dir in DIR) Allow(C, dir, CASTLE_ALL);
        foreach (var h in new[] { HA, HB })
        foreach (var dir in DIR) Allow(h, dir, HOUSE_ALL);

        // ⛏ Add wall as a fallback neighbor to everyone
        foreach (Tile t in Enum.GetValues(typeof(Tile)))
        {
            foreach (var dir in DIR)
            {
                if (!_adj[t][dir].Contains(Tile.Wall))
                    _adj[t][dir] = _adj[t][dir].Append(Tile.Wall).ToArray();
            }
        }

        // Ensure no direction is missing from the dictionary
        foreach (Tile t in Enum.GetValues(typeof(Tile)))
        foreach (var dir in DIR)
            if (!_adj[t].ContainsKey(dir)) _adj[t][dir] = Array.Empty<Tile>();
    }

    void InitWave()
    {
        int T = _prefab.Length;
        _wave = new bool[Width, Height, T];
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        for (int t = 0; t < T; t++) _wave[x, y, t] = true;
    }

    void SeedBorderWalls()
    {
        int WALL = (int)Tile.Wall;
        for (int x = 0; x < Width;  x++)
        for (int y = 0; y < Height; y++)
            if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1)
                for (int t = 0; t < _prefab.Length; t++)
                    _wave[x, y, t] = (t == WALL);
    }

    void SeedCentralCastle()
    {
        int cx = Width / 2 - 1, cy = Height / 2 - 1;
        PlaceFootprint(cx, cy, Tile.Castle);
    }

    void PlaceFootprint(int ax, int ay, Tile t)
    {
        var sz = _foot[t];
        for (int dx = 0; dx < sz.x; dx++)
        for (int dy = 0; dy < sz.y; dy++)
        {
            int x = ax + dx, y = ay + dy;
            if (x < 0 || x >= Width || y < 0 || y >= Height) continue;
            for (int tt = 0; tt < _prefab.Length; tt++)
                _wave[x, y, tt] = (tt == (int)t);
        }
    }

    bool Collapse()
    {
        int T = _prefab.Length;
        while (true)
        {
            int best = int.MaxValue, bx = -1, by = -1;
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                int cnt = 0; for (int t = 0; t < T; t++) if (_wave[x,y,t]) cnt++;
                if (cnt > 1 && cnt < best) { best = cnt; bx = x; by = y; }
            }
            if (bx < 0) break;

            var opts = new List<int>();
            for (int t = 0; t < T; t++) if (_wave[bx,by,t]) opts.Add(t);
            int pick = opts[_rng.Next(opts.Count)];
            for (int t = 0; t < T; t++) _wave[bx,by,t] = (t == pick);

            if (!Propagate()) return false;
        }
        return true;
    }

    bool Propagate()
    {
        int T = _prefab.Length;
        var q = new Queue<Vector2Int>();
        for (int x=0;x<Width;x++) for (int y=0;y<Height;y++) q.Enqueue(new(x,y));

        while (q.Count>0)
        {
            var p=q.Dequeue(); int x=p.x,y=p.y;
            foreach (var dir in DIR)
            {
                int nx=x+dir.x, ny=y+dir.y;
                if(nx<0||nx>=Width||ny<0||ny>=Height) continue;

                bool changed=false;
                for(int t2=0;t2<T;t2++) if(_wave[nx,ny,t2])
                {
                    bool ok=false;
                    foreach(int t1 in Options(x,y))
                    {
                        var opp=new Vector2Int(-dir.x,-dir.y);
                        if(_adj[(Tile)t1][opp].Contains((Tile)t2)){ ok=true; break; }
                    }
                    if(!ok){ _wave[nx,ny,t2]=false; changed=true; }
                }
                if(changed) q.Enqueue(new(nx,ny));
            }
        }

        for(int x=0;x<Width;x++)
        for(int y=0;y<Height;y++)
            if(!Options(x,y).Any()) return false;
        return true;
    }

    IEnumerable<int> Options(int x,int y)
    {
        for(int t=0;t<_prefab.Length;t++) if(_wave[x,y,t]) yield return t;
    }

    void InstantiateResult()
    {
        var claimed = new bool[Width, Height];

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            if (claimed[x, y]) continue;

            Tile t = (Tile)Options(x, y).First();
            var sz = _foot[t];
            bool full = true;

            for (int dx = 0; dx < sz.x && full; dx++)
            for (int dy = 0; dy < sz.y && full; dy++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx >= Width || yy >= Height) full = false;
                else if (claimed[xx, yy]) full = false;
                else if ((Tile)Options(xx, yy).First() != t) full = false;
            }

            if (!full)
            {
                sz = Vector2Int.one;
                t = Fallback(t);
            }

            var obj = Instantiate(_prefab[(int)t], new Vector3(x, 0, y), Quaternion.identity, transform);

            // Optional: scale down for visual spacing clarity
            obj.transform.localScale *= 0.8f;

            Debug.Log($"Instantiated {t} at {x},{y}");

            for (int dx = 0; dx < sz.x; dx++)
            for (int dy = 0; dy < sz.y; dy++)
                if (x + dx < Width && y + dy < Height) claimed[x + dx, y + dy] = true;
        }
    }

    Tile Fallback(Tile t) => t switch
    {
        Tile.BridgeLarge => Tile.Bridge,
        Tile.Castle      => Tile.Column,
        Tile.HouseA      => Tile.Column,
        Tile.HouseB      => Tile.Column,
        _                => t
    };
}
