// ProceduralTerrainGenerator.cs – hop-version, watertight
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
    public BlockData(BlockType t,int idx){ blockType=t; matrixIndex=idx; generated=true; }
}

public sealed class ProceduralTerrainGenerator : MonoBehaviour
{
    /* ───────────── Settings (unchanged) ───────────── */
    [Header("Generation")]
    public GenerationType generationType = GenerationType.Simple;
    public int viewDistance = 50; public float blockSize = 1;
    public int maxBlocksPerFrame = 200;
    public int maxHeight = 24, minHeight = -8;

    [Header("Heightmap")]
    public float noiseScale = .015f;
    public int baseHeight = 6, heightVariation = 12, dirtDepth = 4, seaLevel = 4;

    [Header("Rendering")]
    public Mesh blockMesh;
    public Material grassMat, dirtMat, stoneMat, waterMat;

    [Header("Player Reference")] public Transform player;
    [Header("Debug")] public bool debugLogs = true, validateRuntime = true;

    /* ───────────── Data containers ───────────── */
    readonly Dictionary<Vector3Int,BlockData> world = new();
    readonly HashSet<Vector3Int>              loadedAir = new();
    readonly Dictionary<BlockType,List<Matrix4x4>> mats = new()
    { {BlockType.Grass,new()},{BlockType.Dirt,new()},{BlockType.Stone,new()},{BlockType.Water,new()} };

    /* dirty flags */ bool dirtyGrass,dirtyDirt,dirtyStone,dirtyWater;
    List<Vector3Int> shell;                             // pre-sorted offsets
    Vector3 lastPlayerPos; Vector3Int lastPlayerGrid;
    int blocksThisFrame; float lastValidate;
    
    readonly List<Vector3Int> tmpKeys = new();   // snapshot buffer

    /* ───────────── Unity lifecycle ───────────── */
    void Awake(){ Application.targetFrameRate = 90; BuildShell(viewDistance); }

    void Start()
    {
        foreach(var l in mats.Values) l.Capacity = viewDistance*viewDistance*2;
        if(!player && Camera.main) player = Camera.main.transform;

        lastPlayerPos  = GetPlayerPos();
        lastPlayerGrid = WorldToGrid(lastPlayerPos);
        StartCoroutine(ChunkedUpdateTerrain(lastPlayerGrid));
    }

    void Update()
    {
        Vector3 pPos = GetPlayerPos(); Vector3Int pGrid = WorldToGrid(pPos);

        if(pGrid!=lastPlayerGrid || Vector3.Distance(pPos,lastPlayerPos)>blockSize*.8f)
        {
            lastPlayerPos=pPos; lastPlayerGrid=pGrid;
            StopCoroutine(nameof(ChunkedUpdateTerrain));
            StartCoroutine(ChunkedUpdateTerrain(lastPlayerGrid));
        }

        if(validateRuntime && Time.time-lastValidate>1f)
        {
            lastValidate=Time.time;
            if(!ValidateNoOverlaps()) Debug.LogWarning("[TerrainGen] overlaps detected");
        }
    }

    void LateUpdate()
    {
        if(dirtyGrass) RebuildMatrix(BlockType.Grass,ref dirtyGrass);
        if(dirtyDirt ) RebuildMatrix(BlockType.Dirt ,ref dirtyDirt );
        if(dirtyStone) RebuildMatrix(BlockType.Stone,ref dirtyStone);
        if(dirtyWater) RebuildMatrix(BlockType.Water,ref dirtyWater);

        DrawBatch(BlockType.Grass,grassMat);
        DrawBatch(BlockType.Dirt ,dirtMat );
        DrawBatch(BlockType.Stone,stoneMat);
        DrawBatch(BlockType.Water,waterMat);
    }

    /* ───────────── Terrain coroutine ───────────── */
    IEnumerator ChunkedUpdateTerrain(Vector3Int center)
    {
        RemoveDistantBlocks(center, viewDistance + 3);

        do
        {
            blocksThisFrame = 0;
            GenerateBlocksInRadius(center);
            yield return null;
        }
        while (blocksThisFrame >= maxBlocksPerFrame);

        if(debugLogs) Debug.Log($"[ChunkedUpdate] center={center}, total={world.Count}");
    }

    void GenerateBlocksInRadius(Vector3Int center)
    {
        foreach(var off in shell)
        {
            int x=center.x+off.x, z=center.z+off.z;
            int h=GetTerrainHeight(x,z);

            int yStart=Mathf.Max(center.y+minHeight, h-dirtDepth-2);
            int yEnd  =Mathf.Min(center.y+maxHeight, Mathf.Max(h,seaLevel)+1);

            for(int y=yStart; y<=yEnd; ++y)
            {
                var p=new Vector3Int(x,y,z);
                if(world.ContainsKey(p)) continue;              // NOTE: removed loadedAir test

                var bt = generationType==GenerationType.Simple
                           ? GenerateSimpleTerrain(p)
                           : GenerateHybridTerrain(p);

                if(bt==BlockType.Water)
                {
                    FillWaterColumn(x,z,y);                     // NEW – back-fill downwards
                }
                else if(bt==BlockType.Air)
                {
                    loadedAir.Add(p);
                }
                else if(PlaceBlock(p,bt) && ++blocksThisFrame>=maxBlocksPerFrame)
                    return;
            }
        }
    }

    /* ───────────── Water helper ───────────── */
    void FillWaterColumn(int x,int z,int startY)
    {
        for(int y=startY; y>=seaLevel && blocksThisFrame<maxBlocksPerFrame; --y)
        {
            var p=new Vector3Int(x,y,z);
            if(world.ContainsKey(p)) break;           // reached already-filled cell
            PlaceBlock(p,BlockType.Water);
            ++blocksThisFrame; dirtyWater=true;
        }
    }

    /* ───────────── Block placement / removal ───────────── */
    bool PlaceBlock(Vector3Int g, BlockType bt)
    {
        var m = Matrix4x4.TRS(GridToWorld(g), Quaternion.identity, Vector3.one*blockSize);
        int idx=mats[bt].Count; mats[bt].Add(m);
        world.Add(g,new BlockData(bt,idx));
        return true;
    }

    void RemoveBlock(Vector3Int g)
    {
        if(!world.TryGetValue(g,out var bd)) return;
        var list=mats[bd.blockType]; int last=list.Count-1;
        list[bd.matrixIndex]=list[last]; list.RemoveAt(last);

        if(bd.matrixIndex<list.Count)
            foreach(var kv in world)
                if(kv.Value.blockType==bd.blockType && kv.Value.matrixIndex==last)
                { world[kv.Key]=new BlockData(bd.blockType,bd.matrixIndex); break; }

        world.Remove(g); MarkDirty(bd.blockType);
    }

    void MarkDirty(BlockType bt)
    { if(bt==BlockType.Grass) dirtyGrass=true;
      else if(bt==BlockType.Dirt) dirtyDirt=true;
      else if(bt==BlockType.Stone) dirtyStone=true;
      else if(bt==BlockType.Water) dirtyWater=true; }

    /* ───────────── Matrix rebuild ───────────── */
    // put this inside ProceduralTerrainGenerator

    void RebuildMatrix(BlockType bt, ref bool flag)
    {
        var list = mats[bt];
        list.Clear();

        /* 1️⃣ snapshot all keys of the wanted block-type */
        tmpKeys.Clear();
        foreach (var kv in world)
            if (kv.Value.blockType == bt)
                tmpKeys.Add(kv.Key);

        /* 2️⃣ rebuild from the snapshot, so we’re no longer
              iterating over the dictionary we’re about to edit */
        foreach (var key in tmpKeys)
        {
            var bd = world[key];
            bd.matrixIndex = list.Count;
            world[key] = bd;                     // safe: not iterating world now

            list.Add(Matrix4x4.TRS(
                GridToWorld(key),
                Quaternion.identity,
                Vector3.one * blockSize));
        }
        flag = false;
    }


    /* ───────────── Noise helpers ───────────── */
    int GetTerrainHeight(int x,int z)
    {
        float n=MultiOctaveNoise(x,z);
        int h=baseHeight+Mathf.RoundToInt(n*heightVariation);
        return Mathf.Clamp(h,minHeight+2,maxHeight-2);
    }
    float Noise(float x,float y)=>Mathf.PerlinNoise(x*noiseScale,y*noiseScale)*2-1;
    float MultiOctaveNoise(float x,float y)
    { float a=1,f=1,s=0,m=0; for(int i=0;i<4;i++){ s+=Noise(x*f,y*f)*a; m+=a; a*=.5f; f*=2; } return s/m; }

    BlockType GenerateSimpleTerrain(Vector3Int gp)
    {
        int h=GetTerrainHeight(gp.x,gp.z);
        if(gp.y>h)  return (gp.y<=seaLevel&&h<=seaLevel)?BlockType.Water:BlockType.Air;
        if(gp.y==h) return h<=seaLevel?BlockType.Dirt:BlockType.Grass;
        return gp.y>h-dirtDepth?BlockType.Dirt:BlockType.Stone;
    }
    BlockType GenerateHybridTerrain(Vector3Int gp)
    {
        var baseT=GenerateSimpleTerrain(gp);
        if(baseT==BlockType.Stone && gp.y>minHeight+2)
        {
            float caveNoise=Noise(gp.x*.08f,gp.z*.08f+gp.y*.06f);
            if(caveNoise>.6f) return BlockType.Air;
        }
        return baseT;
    }

    /* ───────────── Rendering ───────────── */
    void DrawBatch(BlockType bt,Material mat)
    {
        var list=mats[bt]; if(list.Count==0) return;
        const int batch=1023;
        for(int i=0;i<list.Count;i+=batch)
            Graphics.DrawMeshInstanced(blockMesh,0,mat,
                list.GetRange(i,Mathf.Min(batch,list.Count-i)),null,
                UnityEngine.Rendering.ShadowCastingMode.On,true,0,null,
                UnityEngine.Rendering.LightProbeUsage.Off,null);
    }

    /* ───────────── Misc helpers ───────────── */
    Vector3 GetPlayerPos()=>player?player.position:Vector3.zero;
    Vector3Int WorldToGrid(Vector3 w)=>new(Mathf.RoundToInt(w.x/blockSize),
                                           Mathf.RoundToInt(w.y/blockSize),
                                           Mathf.RoundToInt(w.z/blockSize));
    Vector3 GridToWorld(Vector3Int g)=>new(g.x*blockSize+blockSize*.5f,
                                           g.y*blockSize+blockSize*.5f,
                                           g.z*blockSize+blockSize*.5f);

    void RemoveDistantBlocks(Vector3Int c,int r)
    {
        List<Vector3Int> rem=null;
        foreach(var kv in world) if((c-kv.Key).sqrMagnitude>r*r) (rem??=new()).Add(kv.Key);
        if(rem!=null) foreach(var p in rem) RemoveBlock(p);
    }

    bool ValidateNoOverlaps()
    {
        var set=new HashSet<Vector3Int>();
        foreach(var kv in world) if(!set.Add(kv.Key)) return false;
        return true;
    }

    void BuildShell(int r)
    {
        shell=new List<Vector3Int>(r*r*4);
        for(int dx=-r;dx<=r;++dx) for(int dz=-r;dz<=r;++dz)
            if(dx*dx+dz*dz<=r*r) shell.Add(new Vector3Int(dx,0,dz));
        shell.Sort((a,b)=>(a.x*a.x+a.z*a.z).CompareTo(b.x*b.x+b.z*b.z)); // near→far
    }
}
