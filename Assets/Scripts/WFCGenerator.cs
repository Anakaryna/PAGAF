using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Générateur de plantes basé sur l'algorithme Wave Function Collapse (WFC).
/// </summary>
public class WFCGenerator : MonoBehaviour
{
    /* ─────────────────────────────────────────────────────────────
     * 1) Définition des types de voxel (= modules du WFC)
     * ────────────────────────────────────────────────────────────*/
    public enum VoxelType
    {
        Empty,                     // air / case vide
        StemStraight,              // tige verticale
        StemTurnX, StemTurnZ,      // coude horizontal (X ou Z)
        StemFork,                  // embranchement en “T”
        StemEnd,                   // terminaison (si pas de fleur)
        Leaf,                      // feuille décorative
        FlowerA, FlowerB           // deux variantes de fleur
    }

    /*  Chaque module possède trois listes de compatibilité               */
    [Serializable]
    struct ModuleRule
    {
        public string[] up, down, sides;
        public ModuleRule(string[] u, string[] d, string[] s)
        { up = u; down = d; sides = s; }
    }

    /* ─────────────────────────────────────────────────────────────
     * 2) Paramètres globaux qui sculptent la plante
     * ────────────────────────────────────────────────────────────*/
    const float GROUND_SEED       = 0.01f; // cellules (x,z) initialisées avec une graine
    const float SIDE_GROW_BOTTOM  = 0.05f; // probabilité de poussée latérale à y = 0
    const float SIDE_GROW_TOP     = 0.80f; // … à y = hauteur max → large canopée
    const float LEAF_RATE_BOTTOM  = 0.00f; // aucune feuille au pied
    const float LEAF_RATE_TOP     = 0.85f; // feuilles fréquentes en haut
    const float FLOWER_RATE       = 0.80f; // pourcentage de tips transformés en fleur
    const float FORK_BONUS_TOP    = 1.5f;  // poids ajouté aux forks dans la partie haute

    /* ─────────────────────────────────────────────────────────────
     * 3) Prefabs exposés + taille de la grille
     * ────────────────────────────────────────────────────────────*/
    [Header("Prefabs")]
    public GameObject StemStraightPrefab, StemTurnXPrefab, StemTurnZPrefab,
                      StemForkPrefab,  StemEndPrefab,
                      LeafPrefab,      FlowerAPrefab, FlowerBPrefab, EmptyPrefab;

    [Header("Grid")]
    public int   GridSizeX = 12, GridSizeY = 48, GridSizeZ = 12;
    public float VoxelSize = .25f;

    /* ─────────────────────────────────────────────────────────────
     * 4) Conteneurs internes (grille + dictionnaires)
     * ────────────────────────────────────────────────────────────*/
    VoxelType?[,,] grid;                           // état de chaque cellule
    Dictionary<VoxelType,GameObject> pref;         // voxel → prefab Unity
    Dictionary<VoxelType,float>      weight;       // voxel → poids aléatoire
    Dictionary<VoxelType,ModuleRule> rule;         // voxel → règles up/down/sides

    /* ─────────────────────────────────────────────────────────────
     * 5) Cycle de vie Unity
     * ────────────────────────────────────────────────────────────*/
    void Awake()
    {
        InitPref();     // lie voxels ←→ prefabs
        InitWeights();  // initialise les poids
        InitRules();    // crée les contraintes de voisinage
    }

    void Start()
    {
        BuildStems();     // 1) squelette de tiges via WFC
        AddLeaves();      // 2) feuilles latérales
        DecorateTips();   // 3) fleurs ou StemEnd en haut de chaque colonne
        Spawn();          // 4) instanciation des prefabs
    }

    /* ============================================================
     * INIT : conversion en dictionnaires
     * ============================================================*/
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

    /// <summary>
    /// Initialise les poids de chaque type de voxel.
    /// </summary>
    void InitWeights() => weight = new()
    {
        [VoxelType.Empty]        = 6f,   // fort biais vers l’air pour aérer la couronne
        [VoxelType.StemStraight] = 1f,
        [VoxelType.StemTurnX]    = 2.2f,
        [VoxelType.StemTurnZ]    = 2.2f,
        [VoxelType.StemFork]     = 1.2f,
        [VoxelType.StemEnd]      = 0.6f
    };

    /// <summary>
    /// Initialise les règles de compatibilité entre les voxels.
    /// Chaque voxel a trois listes : up, down et sides.
    /// - up : ce qui peut être au-dessus de ce voxel
    /// - down : ce qui peut être en-dessous
    /// - sides : ce qui peut être sur les côtés (gauche, droite, avant, arrière)
    /// </summary>
    void InitRules()
    {
        /* Petit helper : enum → string[] */
        string[] S(params VoxelType[] a) => Array.ConvertAll(a, v => v.ToString());

        rule = new()
        {
            // Tige verticale
            [VoxelType.StemStraight] = new(
                S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,
                  VoxelType.StemFork,   VoxelType.StemEnd,  VoxelType.Empty), // up
                S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,
                  VoxelType.Empty),                                           // down
                S(VoxelType.StemTurnX,VoxelType.StemTurnZ,VoxelType.Empty)    // sides
            ),
            // Coudes
            [VoxelType.StemTurnX] = new(
                S(VoxelType.Empty,VoxelType.StemStraight),
                S(VoxelType.StemStraight),
                S(VoxelType.StemTurnZ,VoxelType.StemStraight,VoxelType.Empty)
            ),
            [VoxelType.StemTurnZ] = new(
                S(VoxelType.Empty,VoxelType.StemStraight),
                S(VoxelType.StemStraight),
                S(VoxelType.StemTurnX,VoxelType.StemStraight,VoxelType.Empty)
            ),
            // Fourche
            [VoxelType.StemFork] = new(
                S(VoxelType.StemStraight,VoxelType.StemEnd,VoxelType.Empty),
                S(VoxelType.StemStraight),
                S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,VoxelType.Empty)
            ),
            // Terminaison
            [VoxelType.StemEnd] = new(
                S(VoxelType.Empty),
                S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,VoxelType.StemFork),
                S(VoxelType.Empty)
            )
        };
    }

    /* ============================================================
     * 1) Construction de la structure de tiges (WFC + BFS)
     * ============================================================*/
    void BuildStems()
    {
        grid = new VoxelType?[GridSizeX, GridSizeY, GridSizeZ];
        var Q = new Queue<Vector3Int>();               // file de cellules à explorer

        /* a. Graines sur le sol */
        for (int x = 0; x < GridSizeX; x++)
            for (int z = 0; z < GridSizeZ; z++)
                if (UnityEngine.Random.value < GROUND_SEED)
                {
                    grid[x, 0, z] = VoxelType.StemStraight;
                    Q.Enqueue(new(x, 0, z));
                }
                else grid[x, 0, z] = VoxelType.Empty;

        /* b. Directions voisines (6-connexité) */
        Vector3Int[] dir =
        {
            Vector3Int.up,
            Vector3Int.left, Vector3Int.right,
            new(0,0,1),      new(0,0,-1)
        };

        /* c. Propagation */
        while (Q.Count > 0)
        {
            var cur = Q.Dequeue();

            // Hauteur normalisée 0-1
            float h01 = (float)cur.y / (GridSizeY - 1);
            // Lerp : très peu de pousses latérales en bas, beaucoup en haut
            float sideGrow = Mathf.Lerp(SIDE_GROW_BOTTOM, SIDE_GROW_TOP, h01);

            foreach (var d in dir)
            {
                var n = cur + d;
                if (!Inside(n) || grid[n.x, n.y, n.z] != null) continue;
                if (d != Vector3Int.up && UnityEngine.Random.value > sideGrow) continue;

                /* Liste des modules compatibles avec tous les voisins déjà fixés */
                var opts = new List<VoxelType>();
                foreach (var t in rule.Keys)
                    if (Compatible(t, n, d)) opts.Add(t);

                if (d != Vector3Int.up) opts.Add(VoxelType.Empty); // autorise l’air sur les côtés

                /* Interdiction de StemEnd avant 80 % de la hauteur (si alternative) */
                if (d == Vector3Int.up && n.y < GridSizeY * 0.8f && opts.Count > 1)
                    opts.Remove(VoxelType.StemEnd);

                /* Sécurité : jamais de liste vide */
                if (opts.Count == 0) opts.Add(VoxelType.StemStraight);

                /* Momentum vertical = 3 tickets bonus pour StemStraight */
                if (d == Vector3Int.up && opts.Contains(VoxelType.StemStraight))
                    for (int k = 0; k < 3; k++) opts.Add(VoxelType.StemStraight);

                /* +FORK en haut */
                if (h01 > 0.5f && opts.Contains(VoxelType.StemFork))
                    for (int k = 0; k < Mathf.RoundToInt(FORK_BONUS_TOP); k++)
                        opts.Add(VoxelType.StemFork);

                /* Tirage pondéré puis écriture dans la grille */
                var choice = Pick(opts);
                grid[n.x, n.y, n.z] = choice;

                if (IsStem(choice)) Q.Enqueue(n); // on continue de propager
            }
        }
    }

    /* ============================================================
     * 2) Feuilles latérales dépendantes de la hauteur
     * ============================================================*/
    void AddLeaves()
    {
        Vector3Int[] side =
        {
            Vector3Int.left, Vector3Int.right,
            new(0,0,1),      new(0,0,-1)
        };

        for (int x = 0; x < GridSizeX; x++)
            for (int y = 0; y < GridSizeY; y++)
            {
                float h01 = (float)y / (GridSizeY - 1);
                float leafRate = Mathf.Lerp(LEAF_RATE_BOTTOM, LEAF_RATE_TOP, h01);

                for (int z = 0; z < GridSizeZ; z++)
                    if (IsStem(grid[x, y, z]))
                        foreach (var d in side)
                        {
                            var n = new Vector3Int(x + d.x, y, z + d.z);
                            if (!Inside(n) || grid[n.x, n.y, n.z] != VoxelType.Empty) continue;
                            if (UnityEngine.Random.value < leafRate)
                                grid[n.x, n.y, n.z] = VoxelType.Leaf;
                        }
            }
    }

    /* ============================================================
     * 3) Conversion des tips en fleurs / StemEnd
     * ============================================================*/
    void DecorateTips()
    {
        // Parcourt chaque colonne (x, z) du haut vers le bas
        for (int x = 0; x < GridSizeX; x++)
            for (int z = 0; z < GridSizeZ; z++)
                for (int y = GridSizeY - 1; y >= 0; --y)
                    if (IsStem(grid[x, y, z]))
                    {
                        grid[x, y, z] = UnityEngine.Random.value < FLOWER_RATE
                            ? (UnityEngine.Random.value < .5f ? VoxelType.FlowerA : VoxelType.FlowerB)
                            : VoxelType.StemEnd;
                        break; // on passe à la colonne suivante
                    }
    }

    /* ============================================================
     * 4) Instanciation Unity
     * ============================================================*/
    void Spawn()
    {
        for (int x = 0; x < GridSizeX; x++)
            for (int y = 0; y < GridSizeY; y++)
                for (int z = 0; z < GridSizeZ; z++)
                {
                    var t = grid[x, y, z];
                    if (t == null || t == VoxelType.Empty) continue;
                    if (!pref.TryGetValue(t.Value, out var pf) || pf == null) continue;

                    // position dans le monde + léger jitter pour les éléments décoratifs
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

    /* ============================================================
     * Helpers (géométrie & tirage pondéré)
     * ============================================================*/
    bool Inside(Vector3Int p) =>
        p.x >= 0 && p.x < GridSizeX &&
        p.y >= 0 && p.y < GridSizeY &&
        p.z >= 0 && p.z < GridSizeZ;

    bool IsStem(VoxelType? v) =>
        v is VoxelType.StemStraight or VoxelType.StemTurnX
          or VoxelType.StemTurnZ  or VoxelType.StemFork;

    bool Compatible(VoxelType cand, Vector3Int pos, Vector3Int d)
    {
        var need = d == Vector3Int.up   ? rule[cand].down
                 : d == Vector3Int.down ? rule[cand].up
                 :                        rule[cand].sides;

        return Array.Exists(need, s => s == Get(pos - d).ToString());
    }

    VoxelType Get(Vector3Int p) =>
        !Inside(p) ? VoxelType.Empty : grid[p.x, p.y, p.z] ?? VoxelType.Empty;

    VoxelType Pick(List<VoxelType> opts)
    {
        // Safety : si jamais la liste est vide on retourne Empty
        if (opts.Count == 0) return VoxelType.Empty;

        float tot = 0f;
        foreach (var o in opts) tot += weight.TryGetValue(o, out var v) ? v : 1f;

        float r = UnityEngine.Random.value * tot;
        foreach (var o in opts)
        {
            r -= weight.TryGetValue(o, out var v) ? v : 1f;
            if (r <= 0f) return o;
        }
        return opts[^1]; // secours
    }
}
