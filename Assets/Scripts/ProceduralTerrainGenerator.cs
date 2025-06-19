using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public enum BlockType : byte { Air = 0, Grass = 1, Dirt = 2, Stone = 3, Water = 4 }
public enum GenerationType : byte { Simple = 0, Hybrid = 1 }

[Serializable]
public struct BlockData
{
    public BlockType blockType;
    public int matrixIndex;
    public bool generated;
    public BlockData(BlockType t, int idx) { blockType = t; matrixIndex = idx; generated = true; }
}

public sealed class ProceduralTerrainGenerator : MonoBehaviour
{
    [Header("Generation")]
    public GenerationType generationType = GenerationType.Simple;
    public int viewDistance = 50;
    public float blockSize = 1f;
    public int maxBlocksPerFrame = 200;
    public int maxHeight = 24;
    public int minHeight = -8;

    [Header("Heightmap")]
    public float noiseScale = 0.015f;
    public int baseHeight = 6;
    public int heightVariation = 12;
    public int dirtDepth = 4;
    public int seaLevel = 4;

    [Header("Rendering")]
    public Mesh blockMesh;
    public Material grassMat, dirtMat, stoneMat, waterMat;

    [Header("Player Reference")]
    public Transform player; // glisser-dépo    ser ici votre player ou la caméra

    [Header("Debug")]
    public bool debugLogs = true;
    public bool validateRuntime = true;

    // Structures de données
    readonly Dictionary<Vector3Int, BlockData> worldGrid = new();
    readonly HashSet<Vector3Int> loadedAir = new();
    readonly Dictionary<BlockType, List<Matrix4x4>> matrices = new()
    {
        { BlockType.Grass, new() },
        { BlockType.Dirt,  new() },
        { BlockType.Stone, new() },
        { BlockType.Water, new() }
    };

    Vector3    lastPlayerPos;
    Vector3Int lastPlayerGrid;
    int        blocksGeneratedThisFrame;
    float      lastGenerationMs, lastValidationT;

    void Awake()
    {
        Application.targetFrameRate = 90;
    }

    void Start()
    {
        // Si aucun player n'a été assigné dans l'inspecteur, on prend la MainCamera par défaut
        if (!player && Camera.main != null)
            player = Camera.main.transform;

        // On mémorise la position initiale
        lastPlayerPos  = GetPlayerPos();
        lastPlayerGrid = WorldToGrid(lastPlayerPos);

        // Lancement de la génération “chunkée” initiale
        StartCoroutine(ChunkedUpdateTerrain(lastPlayerGrid));
    }

    void Update()
    {
        Vector3    pPos  = GetPlayerPos();
        Vector3Int pGrid = WorldToGrid(pPos);

        // Si on a bougé suffisamment (changement de cellule ou déplacement > 0.8*blockSize)
        if (pGrid != lastPlayerGrid
            || Vector3.Distance(pPos, lastPlayerPos) > blockSize * 0.8f)
        {
            lastPlayerPos  = pPos;
            lastPlayerGrid = pGrid;

            // On redémarre la coroutine pour rafraîchir la zone autour du joueur
            StopCoroutine(nameof(ChunkedUpdateTerrain));
            StartCoroutine(ChunkedUpdateTerrain(lastPlayerGrid));
        }

        // Contrôle d'éventuels recouvrements (debug)
        if (validateRuntime && Time.time - lastValidationT > 1f)
        {
            lastValidationT = Time.time;
            if (!ValidateNoOverlaps())
                Debug.LogWarning("[TerrainGen] Overlaps détectés → auto-fixing...");
        }
    }

    void LateUpdate()
    {
        // Affichage instancié par type de bloc
        DrawBlockBatch(BlockType.Grass, grassMat);
        DrawBlockBatch(BlockType.Dirt,  dirtMat);
        DrawBlockBatch(BlockType.Stone, stoneMat);
        DrawBlockBatch(BlockType.Water, waterMat);
    }

    // Coroutine générique pour nettoyage + génération chunkée
    IEnumerator ChunkedUpdateTerrain(Vector3Int center)
    {
        // 1) On supprime les blocs trop éloignés
        RemoveDistantBlocks(center, viewDistance + 3);

        // 2) On génère en paquets jusqu'à épuisement
        do
        {
            blocksGeneratedThisFrame = 0;
            GenerateBlocksInRadius(center, viewDistance);
            RebuildAllMatrices();

            // On attend la prochaine frame pour ne pas bloquer l'UI
            yield return null;
        }
        while (blocksGeneratedThisFrame >= maxBlocksPerFrame);

        if (debugLogs)
            Debug.Log($"[ChunkedUpdate] Centre={center}, total blocs={worldGrid.Count}");
    }

    Vector3 GetPlayerPos()
    {
        return player ? player.position : Vector3.zero;
    }

    Vector3Int WorldToGrid(Vector3 w)
    {
        return new Vector3Int(
            Mathf.RoundToInt(w.x / blockSize),
            Mathf.RoundToInt(w.y / blockSize),
            Mathf.RoundToInt(w.z / blockSize)
        );
    }

    Vector3 GridToWorld(Vector3Int g)
    {
        return new Vector3(
            g.x * blockSize + blockSize * 0.5f,
            g.y * blockSize + blockSize * 0.5f,
            g.z * blockSize + blockSize * 0.5f
        );
    }

    void GenerateBlocksInRadius(Vector3Int center, int radius)
    {
        for (int x = center.x - radius; x <= center.x + radius; ++x)
        for (int z = center.z - radius; z <= center.z + radius; ++z)
        for (int y = center.y + minHeight; y <= center.y + maxHeight; ++y)
        {
            var p = new Vector3Int(x, y, z);
            if (!IsWithinRadius(center, p, radius)) continue;
            if (worldGrid.ContainsKey(p) || loadedAir.Contains(p)) continue;

            var bt = (generationType == GenerationType.Simple)
                     ? GenerateSimpleTerrain(p)
                     : GenerateHybridTerrain(p);

            if (bt == BlockType.Air)
            {
                loadedAir.Add(p);
            }
            else if (PlaceBlock(p, bt) && ++blocksGeneratedThisFrame >= maxBlocksPerFrame)
            {
                // Limite atteinte pour cette frame : on quitte
                return;
            }
        }
    }

    BlockType GenerateSimpleTerrain(Vector3Int gp)
    {
        int h = GetTerrainHeight(gp.x, gp.z);

        if (gp.y > h)
            return (gp.y <= seaLevel && h <= seaLevel)
                ? BlockType.Water
                : BlockType.Air;

        if (gp.y == h)
            return (h <= seaLevel)
                ? BlockType.Dirt
                : BlockType.Grass;

        return (gp.y > h - dirtDepth)
            ? BlockType.Dirt
            : BlockType.Stone;
    }

    BlockType GenerateHybridTerrain(Vector3Int gp)
    {
        var baseT = GenerateSimpleTerrain(gp);
        if (baseT == BlockType.Stone && gp.y > minHeight + 2)
        {
            float caveNoise = Noise(gp.x * 0.08f, gp.z * 0.08f + gp.y * 0.06f);
            if (caveNoise > 0.6f) return BlockType.Air;
        }
        return baseT;
    }

    bool PlaceBlock(Vector3Int gp, BlockType bt)
    {
        if (worldGrid.ContainsKey(gp)) return false;

        var worldPos = GridToWorld(gp);
        var m = Matrix4x4.TRS(worldPos, Quaternion.identity, Vector3.one * blockSize);

        var list = matrices[bt];
        int idx = list.Count;
        list.Add(m);
        worldGrid.Add(gp, new BlockData(bt, idx));
        return true;
    }

    void RemoveBlock(Vector3Int gp)
    {
        if (!worldGrid.TryGetValue(gp, out var bd)) return;

        var list    = matrices[bd.blockType];
        int lastIdx = list.Count - 1;

        // Swap-remove
        list[bd.matrixIndex] = list[lastIdx];
        list.RemoveAt(lastIdx);

        // Mettre à jour l’index du bloc déplacé
        if (bd.matrixIndex < list.Count)
        {
            foreach (var kvp in worldGrid)
            {
                if (kvp.Value.blockType == bd.blockType
                    && kvp.Value.matrixIndex == lastIdx)
                {
                    worldGrid[kvp.Key] = new BlockData(bd.blockType, bd.matrixIndex);
                    break;
                }
            }
        }

        worldGrid.Remove(gp);
    }

    void RemoveDistantBlocks(Vector3Int center, int maxDist)
    {
        List<Vector3Int> toRemove = null;

        foreach (var kvp in worldGrid)
        {
            if (!IsWithinRadius(center, kvp.Key, maxDist))
                (toRemove ??= new()).Add(kvp.Key);
        }

        if (toRemove != null)
        {
            foreach (var p in toRemove)
                RemoveBlock(p);
        }
    }

    bool ValidateNoOverlaps()
    {
        var set = new HashSet<Vector3Int>();
        foreach (var kvp in worldGrid)
            if (!set.Add(kvp.Key))
                return false;
        return true;
    }

    void DrawBlockBatch(BlockType bt, Material mat)
    {
        var list = matrices[bt];
        if (list.Count == 0) return;

        const int batchSize = 1023;
        for (int i = 0; i < list.Count; i += batchSize)
        {
            int count = Mathf.Min(batchSize, list.Count - i);
            Graphics.DrawMeshInstanced(
                blockMesh, 0, mat,
                list.GetRange(i, count),
                null,
                UnityEngine.Rendering.ShadowCastingMode.On,
                true, 0, null,
                UnityEngine.Rendering.LightProbeUsage.Off,
                null
            );
        }
    }

    void RebuildAllMatrices()
    {
        // On vide d'abord toutes les listes
        foreach (var kv in matrices) kv.Value.Clear();

        // Puis on reconstruit
        var keys = new List<Vector3Int>(worldGrid.Keys);
        foreach (var key in keys)
        {
            var bd = worldGrid[key];
            var m  = Matrix4x4.TRS(GridToWorld(key), Quaternion.identity, Vector3.one * blockSize);

            var target = matrices[bd.blockType];
            bd.matrixIndex = target.Count;
            target.Add(m);

            worldGrid[key] = bd;
        }
    }

    bool IsWithinRadius(Vector3Int c, Vector3Int p, int r)
    {
        return (c - p).sqrMagnitude <= r * r;
    }

    int GetTerrainHeight(int x, int z)
    {
        float n = MultiOctaveNoise(x, z);
        int h   = baseHeight + Mathf.RoundToInt(n * heightVariation);
        return Mathf.Clamp(h, minHeight + 2, maxHeight - 2);
    }

    float Noise(float x, float y)
    {
        return Mathf.PerlinNoise(x * noiseScale, y * noiseScale) * 2f - 1f;
    }

    float MultiOctaveNoise(float x, float y)
    {
        float amp = 1f, freq = 1f, sum = 0f, max = 0f;
        for (int i = 0; i < 4; i++)
        {
            sum += Noise(x * freq, y * freq) * amp;
            max += amp;
            amp  *= 0.5f;
            freq *= 2f;
        }
        return sum / max;
    }

    void ClearAllTerrain()
    {
        worldGrid.Clear();
        loadedAir.Clear();
        foreach (var list in matrices.Values)
            list.Clear();
    }
}
