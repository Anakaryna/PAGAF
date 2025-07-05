// ProceduralTerrainGenerator.cs – hop-version, watertight, ★optimised v5
// stable incremental culling (snapshot + cursor, no InvalidOperationException)

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public enum BlockType : byte { Air = 0, Grass = 1, Dirt = 2, Stone = 3, Water = 4 }
public enum GenerationType : byte { Simple = 0, Hybrid = 1 }

[Serializable] public struct BlockData
{
    public BlockType blockType; public int matrixIndex; public bool generated;
    public BlockData(BlockType t, int idx) { blockType = t; matrixIndex = idx; generated = true; }
}

public sealed class ProceduralTerrainGenerator : MonoBehaviour
{
    /* ───────────── User settings ───────────── */
    [Header("Generation")]
    public GenerationType generationType = GenerationType.Simple;
    public int viewDistance = 50;
    public float blockSize = 1f;
    public int maxBlocksPerFrame = 200;
    public int maxHeight = 24, minHeight = -8;

    [Header("Heightmap")]
    public float noiseScale = .015f;
    public int baseHeight = 6, heightVariation = 12, dirtDepth = 4, seaLevel = 4;

    [Header("Rendering")]
    public Mesh blockMesh;
    public Material grassMat, dirtMat, stoneMat, waterMat;

    [Header("Player Reference")] public Transform player;
    [Header("Debug")] public bool validateRuntime = false;

    /* ───────────── Internal data ───────────── */
    readonly Dictionary<Vector3Int, BlockData> world = new();
    readonly Dictionary<BlockType, List<Matrix4x4>> mats = new()
    {
        { BlockType.Grass, new() }, { BlockType.Dirt, new() },
        { BlockType.Stone, new() }, { BlockType.Water, new() }
    };

    List<Vector3Int> shell;            // radial offsets sorted near → far
    Vector3Int generationCentre;       // updated each frame

    /* generation bookkeeping */
    int blocksThisFrame;
    float lastValidate;

    /* instancing scratch */
    const int kBatch = 1023;
    static readonly Matrix4x4[] drawBuf = new Matrix4x4[kBatch];

    /* ───────────── Incremental culling state (snapshot + cursor) ───────────── */
    const int maxCullPerFrame = 8000;
    readonly List<Vector3Int> keySnapshot = new();
    readonly List<Vector3Int> killList = new(maxCullPerFrame);
    int snapshotCursor = 0;            // where we resume next frame

    /* ───────────── Unity lifecycle ───────────── */
    void Awake()
    {
        Application.targetFrameRate = 90;
        BuildShell(viewDistance);
    }

    void Start()
    {
        foreach (var l in mats.Values)
            l.Capacity = viewDistance * viewDistance * 2;

        if (!player && Camera.main) player = Camera.main.transform;

        generationCentre = WorldToGrid(GetPlayerPos());
        RebuildSnapshot();                                 // first cull pass
        StartCoroutine(ChunkedUpdateTerrain());
    }

    void Update()
    {
        if (player) generationCentre = WorldToGrid(player.position);

#if UNITY_EDITOR
        if (validateRuntime && Time.time - lastValidate > 1f)
        {
            lastValidate = Time.time;
            if (!ValidateNoOverlaps()) Debug.LogWarning("[TerrainGen] overlaps detected");
        }
#endif
    }

    void LateUpdate()
    {
        DrawBatch(BlockType.Grass, grassMat);
        DrawBatch(BlockType.Dirt, dirtMat);
        DrawBatch(BlockType.Stone, stoneMat);
        DrawBatch(BlockType.Water, waterMat);
    }

    /* ───────────── Terrain coroutine ───────────── */
    IEnumerator ChunkedUpdateTerrain()
    {
        int radiusCullSq = (viewDistance + 3) * (viewDistance + 3);

        while (true)
        {
            CullFarBlocks(radiusCullSq);                  // ① safe incremental cull

            /* ② generate, throttled */
            do
            {
                blocksThisFrame = 0;
                GenerateBlocksInRadius(generationCentre);
                yield return null;
            }
            while (blocksThisFrame >= maxBlocksPerFrame);
        }
    }

    /* ───────────── Incremental culling (snapshot) ───────────── */
    void CullFarBlocks(int radiusSq)
    {
        if (keySnapshot.Count == 0) return;               // nothing to cull

        killList.Clear();
        int inspected = 0;

        while (snapshotCursor < keySnapshot.Count && inspected < maxCullPerFrame)
        {
            var key = keySnapshot[snapshotCursor++];
            if ((generationCentre - key).sqrMagnitude > radiusSq)
                killList.Add(key);
            ++inspected;
        }

        foreach (var k in killList) RemoveBlock(k);

        /* finished one full sweep? -> rebuild a fresh snapshot */
        if (snapshotCursor >= keySnapshot.Count)
        {
            RebuildSnapshot();
        }
    }

    void RebuildSnapshot()
    {
        keySnapshot.Clear();
        foreach (var kv in world)
            keySnapshot.Add(kv.Key);
        snapshotCursor = 0;
    }

    /* ───────────── Generation ───────────── */
    void GenerateBlocksInRadius(Vector3Int centre)
    {
        foreach (var off in shell)
        {
            int x = centre.x + off.x, z = centre.z + off.z;
            int h = GetTerrainHeight(x, z);

            int yStart = Mathf.Max(centre.y + minHeight, h - dirtDepth - 2);
            int yEnd = Mathf.Min(centre.y + maxHeight, Mathf.Max(h, seaLevel) + 1);

            for (int y = yStart; y <= yEnd; ++y)
            {
                var p = new Vector3Int(x, y, z);
                if (world.ContainsKey(p)) continue;

                var bt = generationType == GenerationType.Simple
                           ? GenerateSimpleTerrain(p)
                           : GenerateHybridTerrain(p);

                if (bt == BlockType.Water)
                {
                    FillWaterColumn(x, z, y);
                }
                else if (bt != BlockType.Air)
                {
                    if (PlaceBlock(p, bt) && ++blocksThisFrame >= maxBlocksPerFrame)
                        return;
                }
            }
        }
    }

    void FillWaterColumn(int x, int z, int startY)
    {
        for (int y = startY; y >= seaLevel && blocksThisFrame < maxBlocksPerFrame; --y)
        {
            var p = new Vector3Int(x, y, z);
            if (world.ContainsKey(p)) break;
            PlaceBlock(p, BlockType.Water);
            ++blocksThisFrame;
        }
    }

    /* ───────────── Block placement & removal ───────────── */
    bool PlaceBlock(Vector3Int g, BlockType bt)
    {
        var m = Matrix4x4.TRS(GridToWorld(g), Quaternion.identity, Vector3.one * blockSize);
        int idx = mats[bt].Count;
        mats[bt].Add(m);
        world.Add(g, new BlockData(bt, idx));
        return true;
    }

    void RemoveBlock(Vector3Int g)
    {
        if (!world.TryGetValue(g, out var bd)) return;
        var list = mats[bd.blockType];
        int last = list.Count - 1;

        list[bd.matrixIndex] = list[last];
        list.RemoveAt(last);

        if (bd.matrixIndex < list.Count)
            foreach (var kv in world)
                if (kv.Value.blockType == bd.blockType && kv.Value.matrixIndex == last)
                {
                    world[kv.Key] = new BlockData(bd.blockType, bd.matrixIndex);
                    break;
                }
        world.Remove(g);
    }

    /* ───────────── Noise helpers ───────────── */
    int GetTerrainHeight(int x, int z)
    {
        float n = MultiOctaveNoise(x, z);
        int h = baseHeight + Mathf.RoundToInt(n * heightVariation);
        return Mathf.Clamp(h, minHeight + 2, maxHeight - 2);
    }
    float Noise(float x, float y) => Mathf.PerlinNoise(x * noiseScale, y * noiseScale) * 2 - 1;
    float MultiOctaveNoise(float x, float y)
    {
        float a = 1, f = 1, s = 0, m = 0;
        for (int i = 0; i < 4; i++) { s += Noise(x * f, y * f) * a; m += a; a *= .5f; f *= 2; }
        return s / m;
    }

    BlockType GenerateSimpleTerrain(Vector3Int gp)
    {
        int h = GetTerrainHeight(gp.x, gp.z);
        if (gp.y > h)  return (gp.y <= seaLevel && h <= seaLevel) ? BlockType.Water : BlockType.Air;
        if (gp.y == h) return h <= seaLevel ? BlockType.Dirt : BlockType.Grass;
        return gp.y > h - dirtDepth ? BlockType.Dirt : BlockType.Stone;
    }
    BlockType GenerateHybridTerrain(Vector3Int gp)
    {
        var baseT = GenerateSimpleTerrain(gp);
        if (baseT == BlockType.Stone && gp.y > minHeight + 2)
        {
            float cave = Noise(gp.x * .08f, gp.z * .08f + gp.y * .06f);
            if (cave > .6f) return BlockType.Air;
        }
        return baseT;
    }

    /* ───────────── Rendering ───────────── */
    void DrawBatch(BlockType bt, Material mat)
    {
        var list = mats[bt];
        if (list.Count == 0) return;

        for (int i = 0; i < list.Count; i += kBatch)
        {
            int cnt = Mathf.Min(kBatch, list.Count - i);
            list.CopyTo(i, drawBuf, 0, cnt);
            Graphics.DrawMeshInstanced(blockMesh, 0, mat, drawBuf, cnt, null,
                                       UnityEngine.Rendering.ShadowCastingMode.On,
                                       true, 0, null,
                                       UnityEngine.Rendering.LightProbeUsage.Off, null);
        }
    }

    /* ───────────── Misc helpers ───────────── */
    Vector3 GetPlayerPos() => player ? player.position : Vector3.zero;
    Vector3Int WorldToGrid(Vector3 w) => new(Mathf.RoundToInt(w.x / blockSize),
                                            Mathf.RoundToInt(w.y / blockSize),
                                            Mathf.RoundToInt(w.z / blockSize));
    Vector3 GridToWorld(Vector3Int g) => new(g.x * blockSize + blockSize * .5f,
                                             g.y * blockSize + blockSize * .5f,
                                             g.z * blockSize + blockSize * .5f);

#if UNITY_EDITOR
    bool ValidateNoOverlaps()
    {
        var set = new HashSet<Vector3Int>();
        foreach (var kv in world)
            if (!set.Add(kv.Key)) return false;
        return true;
    }
#endif

    void BuildShell(int r)
    {
        shell = new List<Vector3Int>(r * r * 4);
        for (int dx = -r; dx <= r; ++dx)
            for (int dz = -r; dz <= r; ++dz)
                if (dx * dx + dz * dz <= r * r)
                    shell.Add(new Vector3Int(dx, 0, dz));

        shell.Sort((a, b) =>
            (a.x * a.x + a.z * a.z).CompareTo(b.x * b.x + b.z * b.z));
    }
}
