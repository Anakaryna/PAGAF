// using UnityEngine;
// using Unity.Collections;
// using Unity.Jobs;
// using Unity.Mathematics;
// using System.Collections.Generic;
//
// public class OptimizedFishManager : MonoBehaviour
// {
//     public static OptimizedFishManager Instance { get; private set; }
//
//     [Header("Spatial Hash")]
//     public float cellSize = 5f;
//     public int   gridWidth  = 100;
//     public int   gridHeight = 100;
//
//     [Header("Job Settings")]
//     public int jobBatchSize  = 32;
//     public int maxFishCount  = 1000;
//
//     [Header("Boids Parameters")]
//     public float neighborRadius            = 3f;
//     public float separationRadius          = 1.5f;
//     public float separationWeight          = 1.5f;
//     public float alignmentWeight           = 1f;
//     public float cohesionWeight            = 1f;
//     public float targetWeight              = 2f;
//     public float baseAcceleration          = 2f;
//     public float boostAcceleration         = 8f;
//     public int   maxNeighbors              = 20;
//     public bool  onlySchoolWithSameSpecies = true;
//     public bool  avoidLargerSpecies        = true;
//
//     [Header("Obstacle Avoidance")]
//     public float lookAheadDistance      = 5f;
//     public float emergencyDistance      = 1.5f;
//     public float avoidanceMemoryTime    = 2f;
//     public float clearPathCheckDistance = 8f;
//
//     [Header("Debug")]
//     public bool showDebugInfo = false;
//
//     readonly List<OptimizedFishController> allFish = new List<OptimizedFishController>();
//
//     // Native arrays for job I/O
//     NativeArray<FishData> fishDataArray;
//     NativeArray<float3>   desiredDirections;
//     NativeArray<float3>   smoothedDesiredDirections;
//     NativeArray<float>    accelerations;
//     NativeArray<float3>   avoidanceDirections;
//     NativeArray<bool>     avoidingFlags;
//     NativeArray<bool>     emergencyFlags;
//     NativeArray<float>    avoidanceMemoryTimers;
//     NativeParallelMultiHashMap<int,int> spatialHashMap;
//     NativeArray<int>      hashKeys;
//
//     JobHandle obstacleJobHandle;
//     JobHandle boidsJobHandle;
//     bool jobsScheduled = false;
//
//     void Awake()
//     {
//         if (Instance == null)
//         {
//             Instance = this;
//             DontDestroyOnLoad(gameObject);
//             AllocateArrays();
//         }
//         else
//         {
//             Destroy(gameObject);
//         }
//     }
//
//     void AllocateArrays()
//     {
//         // Dispose old arrays if they exist (fixes persistent leaks)
//         if (fishDataArray.IsCreated)            fishDataArray.Dispose();
//         if (desiredDirections.IsCreated)        desiredDirections.Dispose();
//         if (smoothedDesiredDirections.IsCreated) smoothedDesiredDirections.Dispose();
//         if (accelerations.IsCreated)            accelerations.Dispose();
//         if (avoidanceDirections.IsCreated)      avoidanceDirections.Dispose();
//         if (avoidingFlags.IsCreated)            avoidingFlags.Dispose();
//         if (emergencyFlags.IsCreated)           emergencyFlags.Dispose();
//         if (avoidanceMemoryTimers.IsCreated)    avoidanceMemoryTimers.Dispose();
//         if (spatialHashMap.IsCreated)           spatialHashMap.Dispose();
//         if (hashKeys.IsCreated)                 hashKeys.Dispose();
//
//         fishDataArray            = new NativeArray<FishData>(maxFishCount, Allocator.Persistent);
//         desiredDirections        = new NativeArray<float3>(maxFishCount, Allocator.Persistent);
//         smoothedDesiredDirections= new NativeArray<float3>(maxFishCount, Allocator.Persistent);
//         accelerations            = new NativeArray<float>(   maxFishCount, Allocator.Persistent);
//         avoidanceDirections      = new NativeArray<float3>(maxFishCount, Allocator.Persistent);
//         avoidingFlags            = new NativeArray<bool>(    maxFishCount, Allocator.Persistent);
//         emergencyFlags           = new NativeArray<bool>(    maxFishCount, Allocator.Persistent);
//         avoidanceMemoryTimers    = new NativeArray<float>(   maxFishCount, Allocator.Persistent);
//         spatialHashMap           = new NativeParallelMultiHashMap<int,int>(maxFishCount * 4, Allocator.Persistent);
//         hashKeys                 = new NativeArray<int>(     maxFishCount, Allocator.Persistent);
//     }
//
//     void Update()
//     {
//         if (allFish.Count == 0) return;
//
//         // 1) Complete last‐frame jobs before reading
//         if (jobsScheduled)
//         {
//             boidsJobHandle.Complete();
//             obstacleJobHandle.Complete();
//             ConsumeJobResults();
//         }
//
//         // 2) Build input data
//         BuildInputData();
//
//         // 3) Schedule obstacle avoidance
//         var obstacleJob = new ObstacleAvoidanceJob
//         {
//             fishData               = fishDataArray,
//             lookAheadDistance      = lookAheadDistance,
//             emergencyDistance      = emergencyDistance,
//             avoidanceMemoryTime    = avoidanceMemoryTime,
//             clearPathCheckDistance = clearPathCheckDistance,
//             deltaTime              = Time.deltaTime,
//             avoidanceDirections    = avoidanceDirections,
//             avoidingFlags          = avoidingFlags,
//             emergencyFlags         = emergencyFlags,
//             avoidanceMemoryTimers  = avoidanceMemoryTimers
//         };
//         obstacleJobHandle = obstacleJob.Schedule(allFish.Count, jobBatchSize);
//
//         // 4) Schedule boids behavior, dependent on obstacle job
//         var boidsJob = new EnhancedBoidsJob
//         {
//             fishData                  = fishDataArray,
//             avoidanceDirections       = avoidanceDirections,
//             avoidingFlags             = avoidingFlags,
//             emergencyFlags            = emergencyFlags,
//             spatialHashMap            = spatialHashMap,
//             neighborRadius            = neighborRadius,
//             separationRadius          = separationRadius,
//             separationWeight          = separationWeight,
//             alignmentWeight           = alignmentWeight,
//             cohesionWeight            = cohesionWeight,
//             targetWeight              = targetWeight,
//             baseAcceleration          = baseAcceleration,
//             boostAcceleration         = boostAcceleration,
//             maxNeighbors              = maxNeighbors,
//             onlySchoolWithSameSpecies = onlySchoolWithSameSpecies,
//             avoidLargerSpecies        = avoidLargerSpecies,
//             cellSize                  = cellSize,
//             gridWidth                 = gridWidth,
//             gridHeight                = gridHeight,
//             deltaTime                 = Time.deltaTime,
//             desiredDirections         = desiredDirections,
//             accelerations             = accelerations,
//             smoothedDesiredDirections = smoothedDesiredDirections
//         };
//         boidsJobHandle = boidsJob.Schedule(allFish.Count, jobBatchSize, obstacleJobHandle);
//
//         jobsScheduled = true;
//     }
//
//     void ConsumeJobResults()
//     {
//         for (int i = 0; i < allFish.Count; i++)
//         {
//             allFish[i].UpdateFromJobResult(
//                 desiredDirections[i],
//                 smoothedDesiredDirections[i],
//                 accelerations[i],
//                 avoidingFlags[i],
//                 emergencyFlags[i],
//                 avoidanceMemoryTimers[i],
//                 avoidanceDirections[i]
//             );
//         }
//     }
//
//     void BuildInputData()
//     {
//         for (int i = 0; i < allFish.Count; i++)
//         {
//             allFish[i].position = allFish[i].transform.position;
//             fishDataArray[i]    = allFish[i].GetFishData();
//         }
//
//         spatialHashMap.Clear();
//         for (int i = 0; i < allFish.Count; i++)
//         {
//             float3 p = fishDataArray[i].position;
//             int gx = math.clamp((int)(p.x / cellSize), 0, gridWidth-1);
//             int gz = math.clamp((int)(p.z / cellSize), 0, gridHeight-1);
//             int h = gx + gz * gridWidth;
//             spatialHashMap.Add(h, i);
//             hashKeys[i] = h;
//         }
//     }
//
//     public void RegisterFish(OptimizedFishController fish)
//     {
//         if (allFish.Count < maxFishCount && !allFish.Contains(fish))
//         {
//             fish.fishIndex = allFish.Count;
//             allFish.Add(fish);
//         }
//     }
//
//     public void UnregisterFish(OptimizedFishController fish)
//     {
//         if (allFish.Remove(fish))
//         {
//             for (int i = 0; i < allFish.Count; i++)
//                 allFish[i].fishIndex = i;
//         }
//     }
//
//     void OnDestroy()
//     {
//         // Ensure completion before disposing
//         if (jobsScheduled)
//         {
//             boidsJobHandle.Complete();
//             obstacleJobHandle.Complete();
//         }
//
//         // Dispose everything
//         if (fishDataArray.IsCreated)            fishDataArray.Dispose();
//         if (desiredDirections.IsCreated)        desiredDirections.Dispose();
//         if (smoothedDesiredDirections.IsCreated) smoothedDesiredDirections.Dispose();
//         if (accelerations.IsCreated)            accelerations.Dispose();
//         if (avoidanceDirections.IsCreated)      avoidanceDirections.Dispose();
//         if (avoidingFlags.IsCreated)            avoidingFlags.Dispose();
//         if (emergencyFlags.IsCreated)           emergencyFlags.Dispose();
//         if (avoidanceMemoryTimers.IsCreated)    avoidanceMemoryTimers.Dispose();
//         if (spatialHashMap.IsCreated)           spatialHashMap.Dispose();
//         if (hashKeys.IsCreated)                 hashKeys.Dispose();
//     }
//
//     void OnGUI()
//     {
//         if (!showDebugInfo || !Application.isPlaying) return;
//
//         GUILayout.BeginArea(new Rect(10, Screen.height - 150, 300, 140));
//         GUILayout.Box("Fish Manager Debug");
//         GUILayout.Label($"Count: {allFish.Count}");
//         GUILayout.Label($"Neighbor R: {neighborRadius:F1}");
//         GUILayout.Label($"Cell Size: {cellSize:F1}");
//         GUILayout.Label($"Grid: {gridWidth}×{gridHeight}");
//         GUILayout.Label($"Batch Size: {jobBatchSize}");
//         GUILayout.Label($"Max Fish: {maxFishCount}");
//         GUILayout.EndArea();
//     }
// }
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;

public class OptimizedFishManager : MonoBehaviour
{
    public static OptimizedFishManager Instance { get; private set; }

    [Header("Spatial Hash")]
    public float cellSize = 5f;
    public int gridWidth = 100;
    public int gridHeight = 100;

    [Header("Job Settings")]
    public int jobBatchSize = 32;
    public int maxFishCount = 1000;

    [Header("Boids Parameters - TIGHT SCHOOL FORMATION")]
    public float neighborRadius = 4f;        // See enough neighbors
    public float separationRadius = 1.2f;    // Small personal space
    public float separationWeight = 1.5f;    // Gentle separation
    public float alignmentWeight = 2.0f;     // Strong alignment for tight formation
    public float cohesionWeight = 2.5f;      // Strong pull together  
    public float targetWeight = 3.0f;        // Strong target following
    public float baseAcceleration = 2f;
    public float boostAcceleration = 6f;     // Reduced for smoother movement
    public int maxNeighbors = 12;            // Fewer neighbors for performance
    public bool onlySchoolWithSameSpecies = true;
    public bool avoidLargerSpecies = true;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showSeparationDebug = true;

    readonly List<OptimizedFishController> allFish = new List<OptimizedFishController>();

    NativeArray<FishData> fishDataArray;
    NativeArray<float3> desiredDirections;
    NativeArray<float3> smoothedDesiredDirections;
    NativeArray<float> accelerations;
    NativeArray<float3> avoidanceDirections;
    NativeArray<bool> avoidingFlags;
    NativeArray<bool> emergencyFlags;
    NativeArray<float> avoidanceMemoryTimers;
    NativeArray<float3> stableAvoidanceDirections;
    NativeArray<float> avoidanceDirectionTimers;
    NativeParallelMultiHashMap<int, int> spatialHashMap;

    JobHandle obstacleJobHandle;
    JobHandle boidsJobHandle;
    bool jobsScheduled = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            AllocateArrays();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void AllocateArrays()
    {
        DisposeArrays();

        fishDataArray = new NativeArray<FishData>(maxFishCount, Allocator.Persistent);
        desiredDirections = new NativeArray<float3>(maxFishCount, Allocator.Persistent);
        smoothedDesiredDirections = new NativeArray<float3>(maxFishCount, Allocator.Persistent);
        accelerations = new NativeArray<float>(maxFishCount, Allocator.Persistent);
        avoidanceDirections = new NativeArray<float3>(maxFishCount, Allocator.Persistent);
        avoidingFlags = new NativeArray<bool>(maxFishCount, Allocator.Persistent);
        emergencyFlags = new NativeArray<bool>(maxFishCount, Allocator.Persistent);
        avoidanceMemoryTimers = new NativeArray<float>(maxFishCount, Allocator.Persistent);
        stableAvoidanceDirections = new NativeArray<float3>(maxFishCount, Allocator.Persistent);
        avoidanceDirectionTimers = new NativeArray<float>(maxFishCount, Allocator.Persistent);
        spatialHashMap = new NativeParallelMultiHashMap<int, int>(maxFishCount * 4, Allocator.Persistent);
    }

    void Update()
    {
        if (allFish.Count == 0) return;

        if (jobsScheduled)
        {
            boidsJobHandle.Complete();
            obstacleJobHandle.Complete();
            ConsumeJobResults();
        }

        BuildInputData();

        // DEBUG: Check spatial hash and separation
        if (showSeparationDebug && Time.frameCount % 120 == 0)
        {
            DebugSpatialHash();
        }

        var obstacleJob = new ObstacleAvoidanceJob
        {
            fishData = fishDataArray,
            lookAheadDistance = 5f,
            emergencyDistance = 1.5f,
            avoidanceMemoryTime = 2f,
            clearPathCheckDistance = 8f,
            deltaTime = Time.deltaTime,
            avoidanceBlendSpeed = 2f,
            avoidanceDirections = avoidanceDirections,
            avoidingFlags = avoidingFlags,
            emergencyFlags = emergencyFlags,
            avoidanceMemoryTimers = avoidanceMemoryTimers,
            stableAvoidanceDirections = stableAvoidanceDirections,
            avoidanceDirectionTimers = avoidanceDirectionTimers
        };
        obstacleJobHandle = obstacleJob.Schedule(allFish.Count, jobBatchSize);

        var boidsJob = new EnhancedBoidsJob
        {
            fishData = fishDataArray,
            avoidanceDirections = avoidanceDirections,
            avoidingFlags = avoidingFlags,
            emergencyFlags = emergencyFlags,
            stableAvoidanceDirections = stableAvoidanceDirections,
            spatialHashMap = spatialHashMap,
            neighborRadius = neighborRadius,
            separationRadius = separationRadius,
            separationWeight = separationWeight,
            alignmentWeight = alignmentWeight,
            cohesionWeight = cohesionWeight,
            targetWeight = targetWeight,
            baseAcceleration = baseAcceleration,
            boostAcceleration = boostAcceleration,
            maxNeighbors = maxNeighbors,
            onlySchoolWithSameSpecies = onlySchoolWithSameSpecies,
            avoidLargerSpecies = avoidLargerSpecies,
            cellSize = cellSize,
            gridWidth = gridWidth,
            gridHeight = gridHeight,
            deltaTime = Time.deltaTime,
            desiredDirections = desiredDirections,
            accelerations = accelerations,
            smoothedDesiredDirections = smoothedDesiredDirections
        };
        boidsJobHandle = boidsJob.Schedule(allFish.Count, jobBatchSize, obstacleJobHandle);

        jobsScheduled = true;
    }

    void DebugSpatialHash()
    {
        if (allFish.Count == 0) return;

        int totalNeighbors = 0;
        float minDistance = float.MaxValue;
        int clusteredFish = 0;

        for (int i = 0; i < allFish.Count; i++)
        {
            var fish = fishDataArray[i];
            int neighborCount = 0;
            float closestDistance = float.MaxValue;

            // Check neighbors for this fish
            int gx = math.clamp((int)(fish.position.x / cellSize), 0, gridWidth - 1);
            int gz = math.clamp((int)(fish.position.z / cellSize), 0, gridHeight - 1);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int cx = gx + dx;
                    int cz = gz + dz;
                    if (cx < 0 || cx >= gridWidth || cz < 0 || cz >= gridHeight) continue;

                    int hash = cx + cz * gridWidth;
                    if (spatialHashMap.TryGetFirstValue(hash, out int ni, out var it))
                    {
                        do
                        {
                            if (ni != i && ni >= 0 && ni < allFish.Count)
                            {
                                float3 d = fish.position - fishDataArray[ni].position;
                                float dist = math.length(d);
                                if (dist <= neighborRadius)
                                {
                                    neighborCount++;
                                    closestDistance = math.min(closestDistance, dist);
                                }
                            }
                        }
                        while (spatialHashMap.TryGetNextValue(out ni, ref it));
                    }
                }
            }

            totalNeighbors += neighborCount;
            if (closestDistance < float.MaxValue)
            {
                minDistance = math.min(minDistance, closestDistance);
                if (closestDistance < separationRadius)
                {
                    clusteredFish++;
                }
            }
        }

        Debug.Log($"SEPARATION DEBUG - Fish: {allFish.Count}, Avg Neighbors: {totalNeighbors / (float)allFish.Count:F1}, " +
                  $"Min Distance: {minDistance:F2}, Clustered Fish: {clusteredFish}, Separation Radius: {separationRadius:F1}");
    }

    void ConsumeJobResults()
    {
        for (int i = 0; i < allFish.Count; i++)
        {
            allFish[i].UpdateFromJobResult(
                desiredDirections[i],
                smoothedDesiredDirections[i],
                accelerations[i],
                avoidingFlags[i],
                emergencyFlags[i],
                avoidanceMemoryTimers[i],
                avoidanceDirections[i],
                stableAvoidanceDirections[i],
                avoidanceDirectionTimers[i]
            );
        }
    }

    void BuildInputData()
    {
        for (int i = 0; i < allFish.Count; i++)
        {
            fishDataArray[i] = allFish[i].GetFishData();
        }

        spatialHashMap.Clear();
        for (int i = 0; i < allFish.Count; i++)
        {
            float3 p = fishDataArray[i].position;
            int gx = math.clamp((int)(p.x / cellSize), 0, gridWidth - 1);
            int gz = math.clamp((int)(p.z / cellSize), 0, gridHeight - 1);
            int hash = gx + gz * gridWidth;
            spatialHashMap.Add(hash, i);
        }
    }

    public void RegisterFish(OptimizedFishController fish)
    {
        if (allFish.Count < maxFishCount && !allFish.Contains(fish))
        {
            fish.fishIndex = allFish.Count;
            allFish.Add(fish);
        }
    }

    public void UnregisterFish(OptimizedFishController fish)
    {
        if (allFish.Remove(fish))
        {
            for (int i = 0; i < allFish.Count; i++)
                allFish[i].fishIndex = i;
        }
    }

    void DisposeArrays()
    {
        if (fishDataArray.IsCreated) fishDataArray.Dispose();
        if (desiredDirections.IsCreated) desiredDirections.Dispose();
        if (smoothedDesiredDirections.IsCreated) smoothedDesiredDirections.Dispose();
        if (accelerations.IsCreated) accelerations.Dispose();
        if (avoidanceDirections.IsCreated) avoidanceDirections.Dispose();
        if (avoidingFlags.IsCreated) avoidingFlags.Dispose();
        if (emergencyFlags.IsCreated) emergencyFlags.Dispose();
        if (avoidanceMemoryTimers.IsCreated) avoidanceMemoryTimers.Dispose();
        if (stableAvoidanceDirections.IsCreated) stableAvoidanceDirections.Dispose();
        if (avoidanceDirectionTimers.IsCreated) avoidanceDirectionTimers.Dispose();
        if (spatialHashMap.IsCreated) spatialHashMap.Dispose();
    }

    void OnDestroy()
    {
        if (jobsScheduled)
        {
            boidsJobHandle.Complete();
            obstacleJobHandle.Complete();
        }
        DisposeArrays();
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, Screen.height - 180, 400, 170));
        GUILayout.Box("Fish Manager Debug - FIXED VERSION");
        GUILayout.Label($"Fish Count: {allFish.Count}");
        GUILayout.Label($"Separation Radius: {separationRadius:F1}");
        GUILayout.Label($"Separation Weight: {separationWeight:F1}");
        GUILayout.Label($"Neighbor Radius: {neighborRadius:F1}");
        GUILayout.Label($"Cohesion Weight: {cohesionWeight:F1}");
        GUILayout.Label($"Alignment Weight: {alignmentWeight:F1}");
        GUILayout.Label($"Cell Size: {cellSize:F1}");
        GUILayout.Label($"Grid: {gridWidth}×{gridHeight}");
        GUILayout.EndArea();
    }
}