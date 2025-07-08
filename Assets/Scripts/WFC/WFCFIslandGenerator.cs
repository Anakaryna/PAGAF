using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wave Function Collapse (WFC) island generator
/// </summary>
[ExecuteAlways]
public class WFCFIslandGenerator : MonoBehaviour
{
    [Header("Grid Size")]
    public int width  = 20;
    public int height = 20;

    [Header("Cell Spacing (X,Z)")]
    public Vector2 cellSize = new Vector2(2f, 2f);

    [Header("Your Six Tile Prefabs")]
    [Tooltip("0: Air, 1: Island, 2: Water, 3: Bubble, 4: Tree, 5: Animal")]
    public TileDef[] tiles = new TileDef[6];

    [Serializable]
    public class TileDef
    {
        public string name;

        [Tooltip("List of interchangeable prefab variants for this tile")]
        public GameObject[] prefabs;

        [Range(0.1f, 10f)]
        public float weight = 1f;
    }


    // wave[x,y,t]: is tile t still possible at cell (x,y)?
    private bool[,,] wave;
    // neighborAllowed[a,b,d]: can tile b sit in direction d of tile a?
    private bool[,,] neighborAllowed;
    private System.Random rng;
    
    [Header("Random Rotation Settings")]
    [Tooltip("If enabled, each cell when placed will pick one of the angles below.")]
    public bool enableRandomRotation = false;

    [Tooltip("Y-axis angles (in degrees) that can be chosen at random")]
    public float[] rotationAngles = new float[] { 0f, 45f, 90f, 135f };



    // void Start()
    // {
    //     rng = new System.Random();
    //     BuildNeighborRules();
    //     GenerateIsland();
    // }

    /// <summary>
    /// Automatically build neighborAllowed[,] based purely on each tile's name
    /// </summary>
    void BuildNeighborRules()
    {
        int T = tiles.Length;
        neighborAllowed = new bool[T, T, 4];

        for (int a = 0; a < T; a++)
        for (int b = 0; b < T; b++)
        {
            bool ok = IsLogicallyAllowed(tiles[a].name, tiles[b].name);
            // same rule in all 4 cardinal directions
            for (int d = 0; d < 4; d++)
                neighborAllowed[a, b, d] = ok;
        }
    }

    /// <summary>
    /// Logical adjacency
    /// </summary>
    bool IsLogicallyAllowed(string a, string b)
    {
        bool aAir = a.Equals("Air",       StringComparison.OrdinalIgnoreCase);
        bool bAir = b.Equals("Air",       StringComparison.OrdinalIgnoreCase);
        if (aAir && bAir) return true;
        if (aAir) return b.Equals("Island", StringComparison.OrdinalIgnoreCase);
        if (bAir) return a.Equals("Island", StringComparison.OrdinalIgnoreCase);



        bool aDecor = a.Equals("Tree", StringComparison.OrdinalIgnoreCase) || a.Equals("Animal", StringComparison.OrdinalIgnoreCase);
        bool bDecor = b.Equals("Tree", StringComparison.OrdinalIgnoreCase) || b.Equals("Animal", StringComparison.OrdinalIgnoreCase);

        if (aDecor && bDecor) return true; // tree/animal next to each other = ok
        if (aDecor) return b.Equals("Island", StringComparison.OrdinalIgnoreCase) || b.Equals("Water", StringComparison.OrdinalIgnoreCase);
        if (bDecor) return a.Equals("Island", StringComparison.OrdinalIgnoreCase) || a.Equals("Water", StringComparison.OrdinalIgnoreCase);


        
        bool aBubble  = a.Equals("Bubble",   StringComparison.OrdinalIgnoreCase);
        bool bBubble  = b.Equals("Bubble",   StringComparison.OrdinalIgnoreCase);
        if (aBubble)  return b.Equals("Water",  StringComparison.OrdinalIgnoreCase);
        if (bBubble)  return a.Equals("Water",  StringComparison.OrdinalIgnoreCase);

        // Island ↔ Water, Island ↔ Island, Water ↔ Water all allowed
        return true;
    }
    

    /// <summary>
    /// Starts a fresh WFC run, retries on contradiction
    /// </summary>
    [ContextMenu("WFC → Generate Island")]
    public void GenerateIsland()
    {
        // ensure rng & rules exist
        if (rng == null) {
            rng = new System.Random();
            BuildNeighborRules();
        }
        
        ClearPrevious();
        InitializeWave();

        bool ok = RunWFC();
        if (!ok)
        {
            Debug.Log("Contradiction hit, retrying…");
            GenerateIsland();
        }
        else
        {
            InstantiateTiles();
            FillAirWithWaterOrBubble();
        }
        
#if UNITY_EDITOR
        EditorUtility.SetDirty(gameObject);
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    /// <summary>
    /// Clears the previous island by removing all children of this GameObject
    /// </summary>
    void ClearPrevious()
    {
        // Destroy all direct children, backwards so the index stays valid
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }


    /// <summary>
    /// Initializes the wave to allow all tiles in every cell.
    /// </summary>
    void InitializeWave()
    {
        int T = tiles.Length;
        wave = new bool[width, height, T];

        float cx = width  / 2f;
        float cy = height / 2f;
        float radius = Mathf.Min(width, height) / 2f;

        for (int x = 0; x < width;  x++)
        for (int y = 0; y < height; y++)
        {
            float dx = x - cx;
            float dy = y - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);

            bool insideCircle = dist <= radius;

            for (int t = 0; t < T; t++)
            {
                // If outside circle, only allow "Air" tile (index 0)
                if (!insideCircle)
                    wave[x, y, t] = (t == 0); // Air only
                else
                    wave[x, y, t] = true; // All tiles possible
            }
        }
    }


    /// <summary>
    /// Runs the Wave Function Collapse algorithm.
    /// </summary>
    bool RunWFC()
    {
        // each iteration collapses one cell
        for (int step = 0; step < width * height; step++)
        {
            var pos = Observe();
            if (pos.x < 0) return false;  // contradiction
            Propagate();
        }
        return true;
    }

    /// <summary>
    /// Finds the cell with lowest non-zero entropy, collapses it to one tile
    /// </summary>
    Vector2Int Observe()
    {
        int bestX = -1, bestY = -1;
        float bestEntropy = float.MaxValue;
        int T = tiles.Length;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            // count how many tiles are still possible
            int count = 0;
            float sumW = 0, sumWlogW = 0;
            for (int t = 0; t < T; t++) if (wave[x, y, t])
            {
                count++;
                float w = tiles[t].weight;
                sumW    += w;
                sumWlogW += w * Mathf.Log(w);
            }
            if (count == 0)
                return new Vector2Int(-1, -1);
            if (count == 1) 
                continue;

            // Shannon entropy + tiny jitter to break ties
            float entropy = Mathf.Log(sumW) - sumWlogW / sumW
                            + (float)(rng.NextDouble() * 1e-6);
            if (entropy < bestEntropy)
            {
                bestEntropy = entropy;
                bestX = x; bestY = y;
            }
        }

        if (bestX < 0) // already fully collapsed
            return new Vector2Int(-1, -1);

        // pick exactly one tile (weighted)
        wave[bestX, bestY, SelectTile(bestX, bestY)] = true;
        for (int t = 0; t < T; t++)
            if (t != SelectTile(bestX, bestY)) 
                wave[bestX, bestY, t] = false;

        return new Vector2Int(bestX, bestY);
    }
    
    /// <summary>
    /// Selects a tile at (x,y) based on the weights of the remaining tiles.
    /// </summary>
    int SelectTile(int x, int y)
    {
        float sum = 0;
        int T = tiles.Length;
        for (int t = 0; t < T; t++)
            if (wave[x, y, t]) sum += tiles[t].weight;

        float r = (float)(rng.NextDouble() * sum);
        for (int t = 0; t < T; t++) if (wave[x, y, t])
        {
            r -= tiles[t].weight;
            if (r <= 0) return t;
        }
        // fallback
        for (int t = 0; t < T; t++) if (wave[x, y, t]) return t;
        return 0;
    }
    
    /// <summary>
    /// Fills pure Air cells with Water or Bubble based on neighbor counts
    /// </summary>
    void FillAirWithWaterOrBubble()
    {
        int airIndex = 0;
        int waterIndex = 2;
        int bubbleIndex = 3;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            // Check if this cell is pure Air
            if (!IsCollapsedTo(x, y, airIndex)) continue;

            // Count how many neighbors are Water
            int waterNeighbors = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    if (IsCollapsedTo(nx, ny, waterIndex))
                        waterNeighbors++;
                }
            }

            // Only replace Air if surrounded by at least 2 Water
            if (waterNeighbors >= 2)
            {
                int chosen = (rng.NextDouble() < 0.8f) ? bubbleIndex : waterIndex;
                SetTile(x, y, chosen);
            }
        }

        // Regenerate with updated wave
        ClearPrevious();
        InstantiateTiles();
    }

    /// <summary>
    /// Checks if the cell at (x,y) is fully collapsed to a specific tile index
    /// </summary>
    bool IsCollapsedTo(int x, int y, int tileIndex)
    {
        int count = 0;
        for (int t = 0; t < tiles.Length; t++)
            if (wave[x, y, t]) count++;

        return count == 1 && wave[x, y, tileIndex];
    }

    /// <summary>
    /// Sets the tile at (x,y) to a specific tile index, collapsing it
    /// </summary>
    void SetTile(int x, int y, int tileIndex)
    {
        for (int t = 0; t < tiles.Length; t++)
            wave[x, y, t] = (t == tileIndex);
    }


    
    /// <summary>
    /// Propagates the constraints of the collapsed cell to its neighbors
    /// </summary>
    void Propagate()
    {
        var queue = new Queue<Vector2Int>();
        int T = tiles.Length;
        // enqueue every fully-collapsed cell
        for (int x = 0; x < width;  x++)
        for (int y = 0; y < height; y++)
        {
            int c = 0;
            for (int t = 0; t < T; t++) if (wave[x,y,t]) c++;
            if (c == 1) queue.Enqueue(new Vector2Int(x,y));
        }

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = p.x + (d==1 ? 1 : d==3 ? -1 : 0);
                int ny = p.y + (d==0 ? 1 : d==2 ? -1 : 0);
                if (nx<0||nx>=width||ny<0||ny>=height) continue;

                bool changed = false;
                for (int t = 0; t < T; t++) if (wave[nx,ny,t])
                {
                    bool ok = false;
                    for (int t2 = 0; t2 < T; t2++)
                        if (wave[p.x,p.y,t2] && neighborAllowed[t2,t,d])
                        {
                            ok = true; break;
                        }
                    if (!ok)
                    {
                        wave[nx,ny,t] = false;
                        changed = true;
                    }
                }
                if (changed) queue.Enqueue(new Vector2Int(nx,ny));
            }
        }
    }

    /// <summary>
    /// Instantiate your flat island on X,Z at Y=0.
    /// </summary>
    void InstantiateTiles()
    {
        for (int x = 0; x < width;  x++)
        for (int y = 0; y < height; y++)
        {
            for (int t = 0; t < tiles.Length; t++) if (wave[x,y,t])
            {
                // --- position ---
                Vector3 pos = transform.position
                              + new Vector3(x * cellSize.x, 0, y * cellSize.y);

                // --- choose a random variant prefab ---
                var variants = tiles[t].prefabs;
                if (variants == null || variants.Length == 0)
                {
                    Debug.LogWarning($"No prefabs set for tile {tiles[t].name}");
                    continue;
                }
                int idx = rng.Next(variants.Length);
                GameObject chosenPrefab = variants[idx];

                // --- choose rotation ---
                Quaternion rot = Quaternion.identity;
                if (enableRandomRotation && rotationAngles != null && rotationAngles.Length > 0)
                {
                    // pick a random index into your allowed angles
                    int ri = rng.Next(rotationAngles.Length);
                    float angle = rotationAngles[ri];
                    rot = Quaternion.Euler(0f, angle, 0f);
                }


                // --- instantiate ---
                Instantiate(chosenPrefab, pos, rot, transform);
                break;
            }
        }
    }

}
