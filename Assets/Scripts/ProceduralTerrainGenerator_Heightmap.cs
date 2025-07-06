/*  ProceduralTerrainGenerator_Heightmap.cs
    -----------------------------------------------------------------------
    Uses the voxel generator’s noise + height params, but outputs a smooth
    Unity Terrain (no cubes, no water for now).
    ----------------------------------------------------------------------- */

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteInEditMode]
[RequireComponent(typeof(Terrain))]
public class ProceduralTerrainGenerator_Heightmap : MonoBehaviour
{
    /* ─────────────── 1.  Noise & “Voxel” Height Range ──────────────── */
    [Header("Noise")]
    public float  noiseScale  = 0.015f;
    [Range(1,8)] public int   octaves     = 4;
    [Range(0,1)] public float persistence = .5f;
    public float  lacunarity  = 2f;
    public Vector2 offset;

    [Header("Block-style Height")]
    public int baseHeight      = 6;   // blocks
    public int heightVariation = 12;  // ± blocks
    public int minHeight       = -8;  // world bottom  (blocks)
    public int maxHeight       = 24;  // world top     (blocks)
    public float blockSize     = 1f;  // metres per block

    /* ─────────────── 2.  Optional Texture Painting ─────────────────── */
    [Header("Terrain Layers (optional)")]
    public TerrainLayer grassLayer;
    public TerrainLayer dirtLayer;
    public TerrainLayer rockLayer;
    [Range(0,1)]  public float grassHeightMax = .45f;
    [Range(0,60)] public float grassSlopeMax  = 30;
    [Range(0,1)]  public float rockHeightMin  = .75f;
    [Range(0,60)] public float rockSlopeMin   = 25;

    /* ─────────────── 3.  Generation Control ────────────────────────── */
    [Header("Generation")]
    public int  seed = 0;
    public bool autoGenerateOnPlay = false;

    /* ─────────────── Internals ─────────────────────────────────────── */
    Terrain     terrain;
    TerrainData data;

    /* ================================================================ */
    void Awake()
    {
        terrain = GetComponent<Terrain>();
        data    = terrain.terrainData;

        // match vertical size to block range so 0-1 heightmap == min→max blocks
        data.size = new Vector3(
            data.size.x,
            (maxHeight - minHeight) * blockSize,
            data.size.z
        );

        if (autoGenerateOnPlay && Application.isPlaying)
            GenerateAll();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        lacunarity = Mathf.Max(1f, lacunarity);
        octaves    = Mathf.Clamp(octaves, 1, 8);

        if (!terrain) terrain = GetComponent<Terrain>();
        if (terrain)  data = terrain.terrainData;
        if (data)
            data.size = new Vector3(
                data.size.x,
                (maxHeight - minHeight) * blockSize,
                data.size.z
            );
    }
#endif

    /* =====================  ENTRY POINT  ============================ */
    [ContextMenu("Generate All")]
    public void GenerateAll()
    {
        Random.InitState(seed);

        GenerateHeights();

        if (grassLayer && dirtLayer && rockLayer)
        {
            SetupLayers();
            PaintTextures();
        }

        terrain.Flush();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    /* -------------------- 1.  Heightmap ----------------------------- */
    void GenerateHeights()
    {
        int res = data.heightmapResolution;
        var hmap = new float[res, res];

        for (int x = 0; x < res; x++)
        for (int z = 0; z < res; z++)
        {
            float n = MultiOctaveNoise(x + offset.x, z + offset.y);      // -1 → +1
            int   hBlocks = baseHeight + Mathf.RoundToInt(n * heightVariation);
            hBlocks = Mathf.Clamp(hBlocks, minHeight, maxHeight);

            float h01 = (hBlocks - minHeight) / (float)(maxHeight - minHeight);
            hmap[z, x] = h01;                                            // 0-1
        }

        data.SetHeights(0, 0, hmap);
    }

    /* -------------------- 2.  Optional splat-map -------------------- */
    void SetupLayers() =>
        data.terrainLayers = new[] { grassLayer, dirtLayer, rockLayer };

    void PaintTextures()
    {
        int res = data.alphamapResolution;
        var splat = new float[res, res, 3];

        for (int x = 0; x < res; x++)
        for (int z = 0; z < res; z++)
        {
            float nx = x / (float)(res - 1);
            float nz = z / (float)(res - 1);

            float h = data.GetInterpolatedHeight(nx, nz) / data.size.y;
            float s = data.GetSteepness(nx, nz);

            float[] w = new float[3];
            if (h <= grassHeightMax && s <= grassSlopeMax) w[0] = 1f;   // grass
            if (h >= rockHeightMin  || s >= rockSlopeMin)  w[2] = 1f;   // rock
            w[1] = 1f;                                                  // dirt fallback

            float tot = w[0] + w[1] + w[2];
            w[0] /= tot; w[1] /= tot; w[2] /= tot;

            for (int i = 0; i < 3; i++) splat[z, x, i] = w[i];
        }

        data.SetAlphamaps(0, 0, splat);
    }

    /* -------------------- 3.  Noise helpers ------------------------- */
    float Noise(float x, float y) =>
        Mathf.PerlinNoise(x * noiseScale, y * noiseScale) * 2f - 1f;

    float MultiOctaveNoise(float x, float y)
    {
        float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum  += Noise(x * freq, y * freq) * amp;
            norm += amp;
            amp  *= persistence;
            freq *= lacunarity;
        }
        return sum / norm;   // -1 … +1
    }
}
