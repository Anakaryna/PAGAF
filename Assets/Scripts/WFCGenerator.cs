using System;
using System.Collections.Generic;
using UnityEngine;

/// Procedural vine generator – slim trunk, wide airy crown.
public class WFCGenerator : MonoBehaviour
{
    /* ───── voxel types ───── */
    public enum VoxelType { Empty, StemStraight, StemTurnX, StemTurnZ, StemFork, StemEnd,
                            Leaf, FlowerA, FlowerB }

    [Serializable] struct ModuleRule
    {
        public string[] up, down, sides;
        public ModuleRule(string[] u,string[] d,string[] s){up=u;down=d;sides=s;}
    }

    /* ───── global shape parameters ───── */
    const float GROUND_SEED       = 0.01f;
    const float SIDE_GROW_BOTTOM  = 0.05f;
    const float SIDE_GROW_TOP     = 0.80f;          // wider crown
    const float LEAF_RATE_BOTTOM  = 0.00f;
    const float LEAF_RATE_TOP     = 0.85f;
    const float FLOWER_RATE       = 0.80f;
    const float FORK_BONUS_TOP    = 1.5f;           // extra forks up high

    /* ───── prefabs & grid size ───── */
    [Header("Prefabs")]
    public GameObject StemStraightPrefab, StemTurnXPrefab, StemTurnZPrefab,
                      StemForkPrefab,  StemEndPrefab,
                      LeafPrefab,      FlowerAPrefab, FlowerBPrefab, EmptyPrefab;

    [Header("Grid")]
    public int   GridSizeX = 12, GridSizeY = 48, GridSizeZ = 12;
    public float VoxelSize = .25f;

    /* ───── internals ───── */
    VoxelType?[,,] grid;
    Dictionary<VoxelType,GameObject> pref;
    Dictionary<VoxelType,float>      weight;
    Dictionary<VoxelType,ModuleRule> rule;

    /* ───── lifecycle ───── */
    void Awake(){InitPref();InitWeights();InitRules();}
    void Start (){BuildStems();AddLeaves();DecorateTips();Spawn();}

    /* ───────────────────── init ───────────────────── */
    void InitPref()=> pref = new()
    {
        [VoxelType.StemStraight]=StemStraightPrefab, [VoxelType.StemTurnX]=StemTurnXPrefab,
        [VoxelType.StemTurnZ]  =StemTurnZPrefab,   [VoxelType.StemFork]  =StemForkPrefab,
        [VoxelType.StemEnd]    =StemEndPrefab,     [VoxelType.Leaf]      =LeafPrefab,
        [VoxelType.FlowerA]    =FlowerAPrefab,     [VoxelType.FlowerB]   =FlowerBPrefab,
        [VoxelType.Empty]      =EmptyPrefab
    };

    void InitWeights()=> weight = new()
    {
        [VoxelType.Empty]=6f,
        [VoxelType.StemStraight]=1f,
        [VoxelType.StemTurnX]=2.2f, [VoxelType.StemTurnZ]=2.2f,
        [VoxelType.StemFork]=1.2f,  [VoxelType.StemEnd]=0.6f
    };

    void InitRules()
    {
        string[] S(params VoxelType[] a)=>Array.ConvertAll(a,v=>v.ToString());
        rule = new()
        {
            [VoxelType.StemStraight]=new(S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,
                                           VoxelType.StemFork,VoxelType.StemEnd,VoxelType.Empty),
                                         S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,VoxelType.Empty),
                                         S(VoxelType.StemTurnX,VoxelType.StemTurnZ,VoxelType.Empty)),
            [VoxelType.StemTurnX]  =new(S(VoxelType.Empty,VoxelType.StemStraight),
                                         S(VoxelType.StemStraight),
                                         S(VoxelType.StemTurnZ,VoxelType.StemStraight,VoxelType.Empty)),
            [VoxelType.StemTurnZ]  =new(S(VoxelType.Empty,VoxelType.StemStraight),
                                         S(VoxelType.StemStraight),
                                         S(VoxelType.StemTurnX,VoxelType.StemStraight,VoxelType.Empty)),
            [VoxelType.StemFork]   =new(S(VoxelType.StemStraight,VoxelType.StemEnd,VoxelType.Empty),
                                         S(VoxelType.StemStraight),
                                         S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,VoxelType.Empty)),
            [VoxelType.StemEnd]    =new(S(VoxelType.Empty),
                                         S(VoxelType.StemStraight,VoxelType.StemTurnX,VoxelType.StemTurnZ,VoxelType.StemFork),
                                         S(VoxelType.Empty))
        };
    }

    /* ───────────────────── stems ───────────────────── */
    void BuildStems()
    {
        grid = new VoxelType?[GridSizeX,GridSizeY,GridSizeZ];
        var Q = new Queue<Vector3Int>();

        for(int x=0;x<GridSizeX;x++)
            for(int z=0;z<GridSizeZ;z++)
                if(UnityEngine.Random.value<GROUND_SEED)
                { grid[x,0,z]=VoxelType.StemStraight; Q.Enqueue(new(x,0,z)); }
                else grid[x,0,z]=VoxelType.Empty;

        Vector3Int[] dir={Vector3Int.up,Vector3Int.left,Vector3Int.right,new(0,0,1),new(0,0,-1)};

        while(Q.Count>0){
            var cur=Q.Dequeue();
            float h01=(float)cur.y/(GridSizeY-1);
            float sideGrow=Mathf.Lerp(SIDE_GROW_BOTTOM,SIDE_GROW_TOP,h01);

            foreach(var d in dir){
                var n=cur+d; if(!Inside(n)||grid[n.x,n.y,n.z]!=null) continue;
                if(d!=Vector3Int.up && UnityEngine.Random.value>sideGrow) continue;

                var opts=new List<VoxelType>();
                foreach(var t in rule.Keys) if(Compatible(t,n,d)) opts.Add(t);
                if(d!=Vector3Int.up) opts.Add(VoxelType.Empty);      // never empty upward

                /* forbid early StemEnd */
                if(d==Vector3Int.up && n.y<GridSizeY*0.8f && opts.Count>1)
                    opts.Remove(VoxelType.StemEnd);

                if(opts.Count==0) opts.Add(VoxelType.StemStraight);  // safety

                /* upward momentum */
                if(d==Vector3Int.up && opts.Contains(VoxelType.StemStraight))
                    for(int k=0;k<3;k++) opts.Add(VoxelType.StemStraight);

                /* extra forks high up */
                if(h01>0.5f && opts.Contains(VoxelType.StemFork))
                    for(int k=0;k<Mathf.RoundToInt(FORK_BONUS_TOP);k++) opts.Add(VoxelType.StemFork);

                var choice=Pick(opts);
                grid[n.x,n.y,n.z]=choice;
                if(IsStem(choice)) Q.Enqueue(n);
            }
        }
    }

    /* ───────────────────── leaves ───────────────────── */
    void AddLeaves()
    {
        Vector3Int[] side={Vector3Int.left,Vector3Int.right,new(0,0,1),new(0,0,-1)};
        for(int x=0;x<GridSizeX;x++)
            for(int y=0;y<GridSizeY;y++){
                float h01=(float)y/(GridSizeY-1);
                float leafRate=Mathf.Lerp(LEAF_RATE_BOTTOM,LEAF_RATE_TOP,h01);
                for(int z=0;z<GridSizeZ;z++) if(IsStem(grid[x,y,z]))
                    foreach(var d in side){
                        var n=new Vector3Int(x+d.x,y,z+d.z);
                        if(!Inside(n)||grid[n.x,n.y,n.z]!=VoxelType.Empty) continue;
                        if(UnityEngine.Random.value<leafRate) grid[n.x,n.y,n.z]=VoxelType.Leaf;
                    }
            }
    }

    /* ───────────────────── tips ───────────────────── */
    void DecorateTips()
    {
        for(int x=0;x<GridSizeX;x++)
            for(int z=0;z<GridSizeZ;z++)
                for(int y=GridSizeY-1;y>=0;--y)
                    if(IsStem(grid[x,y,z])){
                        grid[x,y,z]=UnityEngine.Random.value<FLOWER_RATE
                            ? (UnityEngine.Random.value<.5f?VoxelType.FlowerA:VoxelType.FlowerB)
                            : VoxelType.StemEnd;
                        break;
                    }
    }

    /* ───────────────────── spawn ───────────────────── */
    void Spawn()
    {
        for(int x=0;x<GridSizeX;x++) for(int y=0;y<GridSizeY;y++) for(int z=0;z<GridSizeZ;z++)
        {
            var t=grid[x,y,z]; if(t==null||t==VoxelType.Empty) continue;
            if(!pref.TryGetValue(t.Value,out var pf)||pf==null) continue;

            var pos=transform.position+Vector3.Scale(new Vector3(x,y,z),Vector3.one*VoxelSize);
            var rot=Quaternion.Euler(0,UnityEngine.Random.Range(0,4)*90,0);
            var jitter=t is VoxelType.Leaf or VoxelType.FlowerA or VoxelType.FlowerB
                        ? new Vector3(UnityEngine.Random.Range(-.1f,.1f),
                                      UnityEngine.Random.Range(-.05f,.05f),
                                      UnityEngine.Random.Range(-.1f,.1f))
                        : Vector3.zero;
            var go=Instantiate(pf,pos+jitter,rot,transform);
            go.transform.localScale=Vector3.one*VoxelSize;
        }
    }

    /* ───────────────────── helpers ───────────────────── */
    bool Inside(Vector3Int p)=>p.x>=0&&p.x<GridSizeX&&p.y>=0&&p.y<GridSizeY&&p.z>=0&&p.z<GridSizeZ;
    bool IsStem(VoxelType? v)=>v is VoxelType.StemStraight or VoxelType.StemTurnX
                               or VoxelType.StemTurnZ  or VoxelType.StemFork;

    bool Compatible(VoxelType cand,Vector3Int pos,Vector3Int d)
    {
        var need = d==Vector3Int.up   ? rule[cand].down
                 : d==Vector3Int.down ? rule[cand].up
                 :                      rule[cand].sides;
        return Array.Exists(need,s=>s==Get(pos-d).ToString());
    }

    VoxelType Get(Vector3Int p)=>!Inside(p)?VoxelType.Empty:grid[p.x,p.y,p.z]??VoxelType.Empty;

    VoxelType Pick(List<VoxelType> opts)
    {
        if(opts.Count==0) return VoxelType.Empty;          // safety

        float tot=0; foreach(var o in opts) tot+=weight.TryGetValue(o,out var v)?v:1f;
        float r=UnityEngine.Random.value*tot;
        foreach(var o in opts){
            r-=weight.TryGetValue(o,out var v)?v:1f;
            if(r<=0f) return o;
        }
        return opts[^1];
    }
}
