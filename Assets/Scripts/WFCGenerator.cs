using System;
using System.Collections.Generic;
using UnityEngine;

public class WFCGenerator : MonoBehaviour
{
    /* ───────────── TYPES ───────────── */
    public enum VoxelType
    {
        Empty,
        StemStraight, StemTurnX, StemTurnZ, StemFork, StemEnd,
        Leaf,
        FlowerA, FlowerB
    }

    [Serializable] struct ModuleRule
    {
        public string[] up, down, sides;
        public ModuleRule(string[] up, string[] down, string[] sides)
        { this.up = up; this.down = down; this.sides = sides; }
    }

    static string[] Only(params VoxelType[] t) => Array.ConvertAll(t, v => v.ToString());
    static string[] AnyOf(params VoxelType[] t) => Only(t);

    /* ─────────── INSPECTOR ────────── */
    [Header("Prefabs")]
    public GameObject StemStraightPrefab, StemTurnXPrefab, StemTurnZPrefab,
                      StemForkPrefab, StemEndPrefab,
                      LeafPrefab, FlowerAPrefab, FlowerBPrefab, EmptyPrefab;

    [Header("Grid Settings")]
    public int   GridSizeX = 10, GridSizeY = 50, GridSizeZ = 10;
    public float VoxelSize = .25f;

    /* ─────────── INTERNALS ─────────── */
    VoxelType?[,,]                    grid;
    Dictionary<VoxelType, GameObject> prefabMap;
    Dictionary<VoxelType, float>      weights;
    Dictionary<VoxelType, ModuleRule> rules;

    /* ─────────── LIFECYCLE ─────────── */
    void Awake()  { InitPrefabMap(); InitWeights(); InitRules(); }
    void Start()  { RunWFC(); ConvertColumnTips(); SpawnGrid(); }

    /* ─────── INITIALISATION ─────── */
    void InitPrefabMap() => prefabMap = new()
    {
        [VoxelType.StemStraight] = StemStraightPrefab,
        [VoxelType.StemTurnX]    = StemTurnXPrefab,
        [VoxelType.StemTurnZ]    = StemTurnZPrefab,
        [VoxelType.StemFork]     = StemForkPrefab,
        [VoxelType.StemEnd]      = StemEndPrefab,
        [VoxelType.Leaf]         = LeafPrefab,
        [VoxelType.FlowerA]      = FlowerAPrefab,
        [VoxelType.FlowerB]      = FlowerBPrefab,
        [VoxelType.Empty]        = EmptyPrefab
    };

    /* ➊ Weights tilted for *height* */
    void InitWeights() => weights = new()
    {
        [VoxelType.Empty]        = 5.0f,   // ↓ smaller   (stops wave less often)
        [VoxelType.StemStraight] = 2.0f,   // ↑ bigger
        [VoxelType.StemTurnX]    = 1.5f,
        [VoxelType.StemTurnZ]    = 1.5f,
        [VoxelType.StemFork]     = 1.0f,
        [VoxelType.StemEnd]      = 0.8f,
        [VoxelType.Leaf]         = 2.5f,
        [VoxelType.FlowerA]      = 0.7f,
        [VoxelType.FlowerB]      = 0.7f
    };

    void InitRules()
    {
        rules = new()
        {
            /* ── stems (flowers removed from 'up') ── */
            [VoxelType.StemStraight] = new ModuleRule(
                up   : AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.StemFork, VoxelType.StemEnd, VoxelType.Empty),
                down : AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.Empty),
                sides: AnyOf(VoxelType.Leaf, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.Empty)
            ),
            [VoxelType.StemTurnX] = new ModuleRule(
                up   : AnyOf(VoxelType.Empty, VoxelType.StemStraight),
                down : AnyOf(VoxelType.StemStraight),
                sides: AnyOf(VoxelType.Leaf, VoxelType.StemTurnZ, VoxelType.StemStraight,
                             VoxelType.Empty)
            ),
            [VoxelType.StemTurnZ] = new ModuleRule(
                up   : AnyOf(VoxelType.Empty, VoxelType.StemStraight),
                down : AnyOf(VoxelType.StemStraight),
                sides: AnyOf(VoxelType.Leaf, VoxelType.StemTurnX, VoxelType.StemStraight,
                             VoxelType.Empty)
            ),
            [VoxelType.StemFork] = new ModuleRule(
                up   : AnyOf(VoxelType.StemStraight, VoxelType.StemEnd, VoxelType.Empty),
                down : AnyOf(VoxelType.StemStraight),
                sides: AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.Leaf, VoxelType.Empty)
            ),
            /* ── stem end ── */
            [VoxelType.StemEnd] = new ModuleRule(
                up   : AnyOf(VoxelType.FlowerA, VoxelType.FlowerB, VoxelType.Empty),
                down : AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.StemFork),
                sides: AnyOf(VoxelType.Leaf, VoxelType.FlowerA, VoxelType.FlowerB,
                             VoxelType.Empty)
            ),
            /* ── leaf ── */
            [VoxelType.Leaf] = new ModuleRule(
                up   : Only(VoxelType.Empty),
                down : AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.StemFork),
                sides: AnyOf(VoxelType.Empty)
            ),
            /* ── flowers ── */
            [VoxelType.FlowerA] = new ModuleRule(
                up   : Only(VoxelType.Empty),
                down : AnyOf(VoxelType.StemEnd, VoxelType.StemStraight,
                             VoxelType.StemTurnX, VoxelType.StemTurnZ, VoxelType.Leaf),
                sides: AnyOf(VoxelType.Empty)
            ),
            [VoxelType.FlowerB] = new ModuleRule(
                up   : Only(VoxelType.Empty),
                down : AnyOf(VoxelType.StemEnd, VoxelType.StemStraight,
                             VoxelType.StemTurnX, VoxelType.StemTurnZ, VoxelType.Leaf),
                sides: AnyOf(VoxelType.Empty)
            )
        };
    }

    /* ───────────── WFC ───────────── */
    void RunWFC()
    {
        grid = new VoxelType?[GridSizeX, GridSizeY, GridSizeZ];
        var q = new Queue<Vector3Int>();

        /* seed ground */
        for (int x = 0; x < GridSizeX; ++x)
            for (int z = 0; z < GridSizeZ; ++z)
            {
                if (UnityEngine.Random.value < .6f)      // more stems to start
                { grid[x, 0, z] = VoxelType.StemStraight; q.Enqueue(new Vector3Int(x, 0, z)); }
                else grid[x, 0, z] = VoxelType.Empty;
            }

        /* propagate */
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var dir in Directions)
            {
                var n = cur + dir;
                if (!InBounds(n) || grid[n.x, n.y, n.z] != null) continue;

                var candidates = new List<VoxelType> { VoxelType.Empty };

                foreach (var t in rules.Keys)
                    if (IsCompatible(t, n, dir))
                        candidates.Add(t);

                /* ➋ no leaves/flowers when going UP */
                if (dir == Vector3Int.up)
                    candidates.RemoveAll(v => v is VoxelType.Leaf or VoxelType.FlowerA or VoxelType.FlowerB);

                var choice = WeightedRandom(candidates, weights);
                grid[n.x, n.y, n.z] = choice;

                if (choice is VoxelType.StemStraight or VoxelType.StemTurnX
                                 or VoxelType.StemTurnZ  or VoxelType.StemFork)
                    q.Enqueue(n);
            }
        }
    }

    /* ─────── add blossoms on tips ─────── */
    void ConvertColumnTips(float flowerChance = 0.7f, float endChance = 0.2f)
    {
        for (int x = 0; x < GridSizeX; ++x)
            for (int z = 0; z < GridSizeZ; ++z)
                for (int y = GridSizeY - 1; y >= 0; --y)
                {
                    var v = grid[x, y, z];
                    if (v == null || v == VoxelType.Empty) continue;

                    if (v is VoxelType.StemStraight or VoxelType.StemTurnX or VoxelType.StemTurnZ)
                    {
                        float r = UnityEngine.Random.value;
                        if      (r < flowerChance)
                            grid[x, y, z] = UnityEngine.Random.value < .5f ? VoxelType.FlowerA : VoxelType.FlowerB;
                        else if (r < flowerChance + endChance)
                            grid[x, y, z] = VoxelType.StemEnd;
                    }
                    break;  // tip fixed, next column
                }
    }

    /* ───────────── SPAWN ───────────── */
    void SpawnGrid()
    {
        for (int x = 0; x < GridSizeX; ++x)
            for (int y = 0; y < GridSizeY; ++y)
                for (int z = 0; z < GridSizeZ; ++z)
                {
                    var t = grid[x, y, z];
                    if (t == null || t == VoxelType.Empty) continue;
                    if (!prefabMap.TryGetValue(t.Value, out var prefab) || prefab == null) continue;

                    var pos = transform.position +
                              Vector3.Scale(new Vector3(x, y, z), Vector3.one * VoxelSize);

                    Quaternion rot = Quaternion.Euler(0, UnityEngine.Random.Range(0, 4) * 90, 0);
                    Vector3 jitter = (t == VoxelType.Leaf || t == VoxelType.FlowerA || t == VoxelType.FlowerB)
                                     ? new Vector3(UnityEngine.Random.Range(-.1f, .1f),
                                                   UnityEngine.Random.Range(-.05f, .05f),
                                                   UnityEngine.Random.Range(-.1f, .1f))
                                     : Vector3.zero;

                    var go = Instantiate(prefab, pos + jitter, rot, transform);
                    go.transform.localScale = Vector3.one * VoxelSize;
                }
    }

    /* ───────────── HELPERS ───────────── */
    Vector3Int[] Directions => new[]
    { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right,
      new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };

    bool InBounds(Vector3Int p) =>
        p.x >= 0 && p.x < GridSizeX &&
        p.y >= 0 && p.y < GridSizeY &&
        p.z >= 0 && p.z < GridSizeZ;

    VoxelType GetVoxelAt(Vector3Int p) =>
        !InBounds(p) ? VoxelType.Empty : grid[p.x, p.y, p.z] ?? VoxelType.Empty;

    bool IsCompatible(VoxelType candidate, Vector3Int pos, Vector3Int dir)
    {
        var need = dir == Vector3Int.up   ? rules[candidate].down
                 : dir == Vector3Int.down ? rules[candidate].up
                 :                          rules[candidate].sides;
        var neighbour = GetVoxelAt(pos - dir);
        return Array.Exists(need, s => s == neighbour.ToString());
    }

    static VoxelType WeightedRandom(List<VoxelType> opts,
                                    Dictionary<VoxelType, float> w)
    {
        float total = 0; foreach (var o in opts) total += w.TryGetValue(o, out var v) ? v : 1f;
        float r = UnityEngine.Random.value * total;
        foreach (var o in opts)
        {
            r -= w.TryGetValue(o, out var v) ? v : 1f;
            if (r <= 0) return o;
        }
        return opts[^1];
    }
}
