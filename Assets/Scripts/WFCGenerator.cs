using System;
using System.Collections.Generic;
using UnityEngine;

public class WFCGenerator : MonoBehaviour
{
    /* ────────────  TYPES  ──────────── */
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

        // parameter names now match the named-argument calls ↓↓↓
        public ModuleRule(string[] up, string[] down, string[] sides)
        {
            this.up    = up;
            this.down  = down;
            this.sides = sides;
        }
    }

    static string[] Only(params VoxelType[] t) => Array.ConvertAll(t, v => v.ToString());
    static string[] AnyOf(params VoxelType[] t) => Only(t);

    /* ────────────  INSPECTOR  ──────────── */
    [Header("Prefabs")]
    public GameObject StemStraightPrefab, StemTurnXPrefab, StemTurnZPrefab, StemForkPrefab, StemEndPrefab;
    public GameObject LeafPrefab, FlowerAPrefab, FlowerBPrefab, EmptyPrefab;   // Empty can remain null

    [Header("Grid Settings")]
    public int   GridSizeX = 10;
    public int   GridSizeY = 20;
    public int   GridSizeZ = 10;
    public float VoxelSize = .25f;

    /* ────────────  INTERNALS  ──────────── */
    VoxelType?[,,]                   grid;
    Dictionary<VoxelType, GameObject> prefabMap;
    Dictionary<VoxelType, float>      weights;
    Dictionary<VoxelType, ModuleRule> rules;

    /* ────────────  LIFECYCLE  ──────────── */
    void Awake()
    {
        InitPrefabMap();
        InitWeights();
        InitRules();
    }
    void Start()
    {
        RunWFC();
        SpawnGrid();
    }

    /* ────────────  INIT  ──────────── */
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

    void InitWeights() => weights = new()
    {
        [VoxelType.Empty]        = 2.5f,
        [VoxelType.StemStraight] = 0.8f,
        [VoxelType.StemTurnX]    = 0.6f,
        [VoxelType.StemTurnZ]    = 0.6f,
        [VoxelType.StemFork]     = 0.3f,
        [VoxelType.StemEnd]      = 0.2f,   // ↓ smaller
        [VoxelType.Leaf]         = 1.6f,
        [VoxelType.FlowerA]      = 4.0f,   // ↑ bigger
        [VoxelType.FlowerB]      = 4.0f
    };


    void InitRules()
    {
        rules = new()
        {
            [VoxelType.StemStraight] = new ModuleRule(
                up   : AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.StemFork, VoxelType.StemEnd,
                             VoxelType.FlowerA, VoxelType.FlowerB, VoxelType.Empty),
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
                up   : AnyOf(VoxelType.StemStraight, VoxelType.StemEnd,
                             VoxelType.FlowerA, VoxelType.FlowerB, VoxelType.Empty),
                down : AnyOf(VoxelType.StemStraight),
                sides: AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.Leaf, VoxelType.Empty)
            ),
            [VoxelType.StemEnd] = new ModuleRule(
                up   : Only(VoxelType.Empty),
                down : AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.StemFork),
                sides: AnyOf(VoxelType.Leaf, VoxelType.FlowerA, VoxelType.FlowerB,
                             VoxelType.Empty)
            ),
            [VoxelType.Leaf] = new ModuleRule(
                up   : Only(VoxelType.Empty),
                down : AnyOf(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                             VoxelType.StemFork),
                sides: AnyOf(VoxelType.Empty)
            ),
            [VoxelType.FlowerA] = new ModuleRule(
                up   : Only(VoxelType.Empty),
                down : AnyOf(VoxelType.StemEnd, VoxelType.Leaf),
                sides: AnyOf(VoxelType.Empty)
            ),
            [VoxelType.FlowerB] = new ModuleRule(
                up   : Only(VoxelType.Empty),
                down : AnyOf(VoxelType.StemEnd, VoxelType.Leaf),
                sides: AnyOf(VoxelType.Empty)
            )
        };
    }

    /* ────────────  WFC  ──────────── */
    void RunWFC()
    {
        grid = new VoxelType?[GridSizeX, GridSizeY, GridSizeZ];
        var queue = new Queue<Vector3Int>();

        // 1️⃣ seed: random stems on ground
        for (int x = 0; x < GridSizeX; ++x)
            for (int z = 0; z < GridSizeZ; ++z)
            {
                if (UnityEngine.Random.value < .45f)
                {
                    grid[x, 0, z] = VoxelType.StemStraight;
                    queue.Enqueue(new Vector3Int(x, 0, z));
                }
                else grid[x, 0, z] = VoxelType.Empty;
            }

        // 2️⃣ propagate wave
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var dir in Directions)
            {
                var n = cur + dir;
                if (!InBounds(n) || grid[n.x, n.y, n.z] != null) continue;

                var candidates = new List<VoxelType> { VoxelType.Empty };   // Empty always allowed

                foreach (var t in rules.Keys)
                    if (IsCompatible(t, n, dir))
                        candidates.Add(t);

                var choice = WeightedRandom(candidates, weights);
                grid[n.x, n.y, n.z] = choice;

                // only stems & forks keep propagating
                if (choice is VoxelType.StemStraight or VoxelType.StemTurnX
                                 or VoxelType.StemTurnZ or VoxelType.StemFork)
                    queue.Enqueue(n);
            }
        }

        // 3️⃣ ensure a StemEnd at the tip of each column
        for (int x = 0; x < GridSizeX; ++x)
            for (int z = 0; z < GridSizeZ; ++z)
                for (int y = GridSizeY - 1; y >= 0; --y)
                {
                    var v = grid[x, y, z];
                    if (v == null || v == VoxelType.Empty) continue;
                    if (v is VoxelType.StemStraight or VoxelType.StemTurnX or VoxelType.StemTurnZ)
                        grid[x, y, z] = VoxelType.StemEnd;
                    break;
                }
    }

    /* ────────────  SPAWN  ──────────── */
    void SpawnGrid()
    {
        for (int x = 0; x < GridSizeX; ++x)
            for (int y = 0; y < GridSizeY; ++y)
                for (int z = 0; z < GridSizeZ; ++z)
                {
                    var t = grid[x, y, z];
                    if (t == null || t == VoxelType.Empty) continue;
                    if (!prefabMap.TryGetValue(t.Value, out var prefab) || prefab == null) continue;

                    var worldPos = transform.position +
                                   Vector3.Scale(new Vector3(x, y, z), Vector3.one * VoxelSize);

                    Quaternion rot = Quaternion.Euler(0, UnityEngine.Random.Range(0, 4) * 90, 0);
                    Vector3 jitter = (t == VoxelType.Leaf || t == VoxelType.FlowerA || t == VoxelType.FlowerB)
                                     ? new Vector3(UnityEngine.Random.Range(-.1f, .1f),
                                                   UnityEngine.Random.Range(-.05f, .05f),
                                                   UnityEngine.Random.Range(-.1f, .1f))
                                     : Vector3.zero;

                    var go = Instantiate(prefab, worldPos + jitter, rot, transform);
                    go.transform.localScale = Vector3.one * VoxelSize;
                }
    }

    /* ────────────  HELPERS  ──────────── */
    Vector3Int[] Directions => new[]
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right,
        new Vector3Int(0,0,1), new Vector3Int(0,0,-1)
    };

    bool InBounds(Vector3Int p) =>
        p.x >= 0 && p.x < GridSizeX &&
        p.y >= 0 && p.y < GridSizeY &&
        p.z >= 0 && p.z < GridSizeZ;

    VoxelType GetVoxelAt(Vector3Int p) =>
        !InBounds(p) ? VoxelType.Empty : grid[p.x, p.y, p.z] ?? VoxelType.Empty;

    bool IsCompatible(VoxelType candidate, Vector3Int pos, Vector3Int dir)
    {
        var need = dir == Vector3Int.up   ? rules[candidate].down :
                   dir == Vector3Int.down ? rules[candidate].up   :
                                             rules[candidate].sides;
        var neighbour = GetVoxelAt(pos - dir);   // existing neighbour
        return Array.Exists(need, s => s == neighbour.ToString());
    }

    static VoxelType WeightedRandom(List<VoxelType> opts, Dictionary<VoxelType, float> w)
    {
        float total = 0;
        foreach (var o in opts) total += w.TryGetValue(o, out var v) ? v : 1f;

        float r = UnityEngine.Random.value * total;
        foreach (var o in opts)
        {
            r -= w.TryGetValue(o, out var v) ? v : 1f;
            if (r <= 0) return o;
        }
        return opts[^1];
    }
}
