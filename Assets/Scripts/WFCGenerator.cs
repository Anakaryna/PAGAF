using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural “vine” generator –
///   1. grows a sparse stem skeleton with WFC-style constraints,
///   2. decorates free sides with leaves,
///   3. caps every column with a flower-or-end.
/// Drop it on an empty GameObject, assign the 9 prefabs, press Play.
/// </summary>
public class WFCGenerator : MonoBehaviour
{
    /* ───────────── ENUMS ───────────── */
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

    /* ────────── STYLE TWEAKS ───────── */
    const float GROUND_SEED = 0.15f;   // fraction of ground cells that start a stem
    const float SIDE_GROW   = 0.5f;   // sideways-growth probability per step
    const float LEAF_RATE   = 0.85f;   // chance to stick a leaf on each free side
    const float FLOWER_RATE = 0.80f;   // chance column-tip becomes a flower

    /* ───── PREFABS & GRID ───── */
    [Header("Prefab palette")]
    public GameObject StemStraightPrefab, StemTurnXPrefab, StemTurnZPrefab,
                      StemForkPrefab,  StemEndPrefab,
                      LeafPrefab,      FlowerAPrefab, FlowerBPrefab, EmptyPrefab;

    [Header("Grid")]
    public int   GridSizeX = 12, GridSizeY = 48, GridSizeZ = 12;
    public float VoxelSize = 0.25f;

    /* ───── INTERNAL DATA ───── */
    VoxelType?[,,] grid;
    Dictionary<VoxelType, GameObject> pref;
    Dictionary<VoxelType, float>      weight;
    Dictionary<VoxelType, ModuleRule> rule;

    /* ───────── LIFECYCLE ───────── */
    void Awake () { InitPref(); InitWeights(); InitRules(); }
    void Start  ()
    {
        BuildStemSkeleton();
        AddLeaves();
        DecorateTips();
        Spawn();
    }

    /* ─────── INIT HELPERS ─────── */
    void InitPref() => pref = new()
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

    void InitWeights() => weight = new()
    {
        [VoxelType.Empty]        = 6f,
        [VoxelType.StemStraight] = 1.0f,
        [VoxelType.StemTurnX]    = 2.2f,
        [VoxelType.StemTurnZ]    = 2.2f,
        [VoxelType.StemFork]     = 1.2f,
        [VoxelType.StemEnd]      = 0.6f
    };

    void InitRules()
    {
        rule = new()
        {
            [VoxelType.StemStraight] = new ModuleRule(
                up   : Str(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                           VoxelType.StemFork, VoxelType.StemEnd, VoxelType.Empty),
                down : Str(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                           VoxelType.Empty),
                sides: Str(VoxelType.StemTurnX, VoxelType.StemTurnZ, VoxelType.Empty)
            ),
            [VoxelType.StemTurnX] = new ModuleRule(
                up   : Str(VoxelType.Empty, VoxelType.StemStraight),
                down : Str(VoxelType.StemStraight),
                sides: Str(VoxelType.StemTurnZ, VoxelType.StemStraight, VoxelType.Empty)
            ),
            [VoxelType.StemTurnZ] = new ModuleRule(
                up   : Str(VoxelType.Empty, VoxelType.StemStraight),
                down : Str(VoxelType.StemStraight),
                sides: Str(VoxelType.StemTurnX, VoxelType.StemStraight, VoxelType.Empty)
            ),
            [VoxelType.StemFork] = new ModuleRule(
                up   : Str(VoxelType.StemStraight, VoxelType.StemEnd, VoxelType.Empty),
                down : Str(VoxelType.StemStraight),
                sides: Str(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                           VoxelType.Empty)
            ),
            [VoxelType.StemEnd] = new ModuleRule(
                up   : Str(VoxelType.Empty),
                down : Str(VoxelType.StemStraight, VoxelType.StemTurnX, VoxelType.StemTurnZ,
                           VoxelType.StemFork),
                sides: Str(VoxelType.Empty)
            )
        };

        static string[] Str(params VoxelType[] t) => Array.ConvertAll(t, v => v.ToString());
    }

    /* ───────── BUILD STEMS ───────── */
    void BuildStemSkeleton()
    {
        grid = new VoxelType?[GridSizeX, GridSizeY, GridSizeZ];
        var q = new Queue<Vector3Int>();

        // seed ground
        for (int x = 0; x < GridSizeX; x++)
            for (int z = 0; z < GridSizeZ; z++)
            {
                if (UnityEngine.Random.value < GROUND_SEED)
                { grid[x, 0, z] = VoxelType.StemStraight; q.Enqueue(new Vector3Int(x, 0, z)); }
                else grid[x, 0, z] = VoxelType.Empty;
            }

        Vector3Int[] dir = { Vector3Int.up, Vector3Int.left, Vector3Int.right,
                             new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var d in dir)
            {
                var n = cur + d;
                if (!Inside(n) || grid[n.x, n.y, n.z] != null) continue;

                if (d != Vector3Int.up && UnityEngine.Random.value > SIDE_GROW) continue;

                var opts = new List<VoxelType>();
                foreach (var t in rule.Keys)
                    if (Compatible(t, n, d)) opts.Add(t);

                if (d != Vector3Int.up) opts.Add(VoxelType.Empty);    // air sideways
                // ← FIX: never add Empty when going up
                var choice = Pick(opts);
                grid[n.x, n.y, n.z] = choice;

                if (IsStem(choice)) q.Enqueue(n);
            }
        }
    }

    /* ───────── ADD LEAVES ───────── */
    void AddLeaves()
    {
        Vector3Int[] side = { Vector3Int.left, Vector3Int.right,
                              new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
        for (int x = 0; x < GridSizeX; x++)
            for (int y = 0; y < GridSizeY; y++)
                for (int z = 0; z < GridSizeZ; z++)
                    if (IsStem(grid[x, y, z]))
                        foreach (var d in side)
                        {
                            var n = new Vector3Int(x + d.x, y, z + d.z);
                            if (!Inside(n) || grid[n.x, n.y, n.z] != VoxelType.Empty) continue;
                            if (UnityEngine.Random.value < LEAF_RATE)
                                grid[n.x, n.y, n.z] = VoxelType.Leaf;
                        }
    }

    /* ───────── FLOWERS / ENDS ───────── */
    void DecorateTips()
    {
        for (int x = 0; x < GridSizeX; x++)
            for (int z = 0; z < GridSizeZ; z++)
                for (int y = GridSizeY - 1; y >= 0; y--)
                {
                    var v = grid[x, y, z];
                    if (!IsStem(v)) continue;

                    grid[x, y, z] = UnityEngine.Random.value < FLOWER_RATE
                        ? (UnityEngine.Random.value < .5f ? VoxelType.FlowerA : VoxelType.FlowerB)
                        : VoxelType.StemEnd;
                    break;
                }
    }

    /* ───────── SPAWN MESHES ───────── */
    void Spawn()
    {
        for (int x = 0; x < GridSizeX; x++)
            for (int y = 0; y < GridSizeY; y++)
                for (int z = 0; z < GridSizeZ; z++)
                {
                    var t = grid[x, y, z]; if (t == null || t == VoxelType.Empty) continue;
                    if (!pref.TryGetValue(t.Value, out var pf) || pf == null) continue;

                    var pos = transform.position +
                              Vector3.Scale(new Vector3(x, y, z), Vector3.one * VoxelSize);
                    var rot = Quaternion.Euler(0, UnityEngine.Random.Range(0, 4) * 90, 0);
                    var jitter = t is VoxelType.Leaf or VoxelType.FlowerA or VoxelType.FlowerB
                                 ? new Vector3(UnityEngine.Random.Range(-.1f, .1f),
                                               UnityEngine.Random.Range(-.05f, .05f),
                                               UnityEngine.Random.Range(-.1f, .1f))
                                 : Vector3.zero;

                    var go = Instantiate(pf, pos + jitter, rot, transform);
                    go.transform.localScale = Vector3.one * VoxelSize;
                }
    }

    /* ───────── UTILITY ───────── */
    bool Inside(Vector3Int p) => p.x >= 0 && p.x < GridSizeX &&
                                 p.y >= 0 && p.y < GridSizeY &&
                                 p.z >= 0 && p.z < GridSizeZ;

    bool IsStem(VoxelType? v) => v is VoxelType.StemStraight or VoxelType.StemTurnX
                                  or VoxelType.StemTurnZ  or VoxelType.StemFork;

    bool Compatible(VoxelType cand, Vector3Int pos, Vector3Int d)
    {
        var need = d == Vector3Int.up   ? rule[cand].down
                 : d == Vector3Int.down ? rule[cand].up
                 :                        rule[cand].sides;
        return Array.Exists(need, s => s == Get(pos - d).ToString());
    }

    VoxelType Get(Vector3Int p) => !Inside(p) ? VoxelType.Empty
                                : grid[p.x, p.y, p.z] ?? VoxelType.Empty;

    VoxelType Pick(List<VoxelType> opts)
    {
        float sum = 0; foreach (var o in opts) sum += weight.TryGetValue(o, out var v) ? v : 1f;
        float r = UnityEngine.Random.value * sum;
        foreach (var o in opts)
        {
            r -= weight.TryGetValue(o, out var v) ? v : 1f;
            if (r <= 0) return o;
        }
        return opts[^1];
    }
}
