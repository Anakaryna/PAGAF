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
    public int maxVirtualFish = 500;
    public int maxDragons = 10;

    [Header("Boids Parameters")]
    public float neighborRadius = 5f;
    public float separationRadius = 2.2f;
    public float separationWeight = 2.0f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 0.8f;
    public float targetWeight = 2.5f;
    public float baseAcceleration = 2f;
    public float boostAcceleration = 8f;
    public int maxNeighbors = 15;
    public bool onlySchoolWithSameSpecies = true;
    public bool avoidLargerSpecies = true;

    [Header("Boundary Settings")]
    public bool enableBoundaries = false;  // Disabled by default
    public Vector3 boundaryCenter = Vector3.zero;
    public Vector3 boundarySize = new Vector3(2000f, 1000f, 2000f);  // Much larger boundaries
    public float emergencyBoundaryOffset = 50f;

    [Header("Obstacle Avoidance as Virtual Fish")]
    public LayerMask obstacleLayer = 1;
    public string obstacleTag = "Obstacle";
    public float obstacleDetectionRadius = 15f;
    public float virtualFishRadius = 2.5f;
    public int virtualFishRings = 3;
    public int virtualFishPerRing = 12;
    public float ringSpacing = 1.5f;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showObstacleDebug = true;
    public bool showVirtualFish = false;
    public bool showBoundaries = true;

    readonly List<OptimizedFishController> allFish = new List<OptimizedFishController>();
    readonly List<DragonController> allDragons = new List<DragonController>();
    private List<GameObject> detectedObstacles = new List<GameObject>();
    private List<FishData> virtualFishData = new List<FishData>();

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
    NativeArray<float> fearLevels;
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

        int totalCapacity = maxFishCount + maxVirtualFish + maxDragons;
        
        fishDataArray = new NativeArray<FishData>(totalCapacity, Allocator.Persistent);
        desiredDirections = new NativeArray<float3>(totalCapacity, Allocator.Persistent);
        smoothedDesiredDirections = new NativeArray<float3>(totalCapacity, Allocator.Persistent);
        accelerations = new NativeArray<float>(totalCapacity, Allocator.Persistent);
        avoidanceDirections = new NativeArray<float3>(totalCapacity, Allocator.Persistent);
        avoidingFlags = new NativeArray<bool>(totalCapacity, Allocator.Persistent);
        emergencyFlags = new NativeArray<bool>(totalCapacity, Allocator.Persistent);
        avoidanceMemoryTimers = new NativeArray<float>(totalCapacity, Allocator.Persistent);
        stableAvoidanceDirections = new NativeArray<float3>(totalCapacity, Allocator.Persistent);
        avoidanceDirectionTimers = new NativeArray<float>(totalCapacity, Allocator.Persistent);
        fearLevels = new NativeArray<float>(totalCapacity, Allocator.Persistent);
        spatialHashMap = new NativeParallelMultiHashMap<int, int>(totalCapacity * 4, Allocator.Persistent);
    }

    void Update()
    {
        int totalEntities = allFish.Count + allDragons.Count;
        if (totalEntities == 0) return;

        if (jobsScheduled)
        {
            boidsJobHandle.Complete();
            obstacleJobHandle.Complete();
            ConsumeJobResults();
        }

        // Update obstacles and create virtual fish
        UpdateObstaclesAndVirtualFish();
        
        BuildInputData();

        if (showObstacleDebug && Time.frameCount % 120 == 0)
        {
            DebugObstacleSystem();
        }

        int totalFishCount = allFish.Count + allDragons.Count + virtualFishData.Count;

        var obstacleJob = new ObstacleAvoidanceJob
        {
            fishData = fishDataArray,
            lookAheadDistance = 5f,
            emergencyDistance = 1.5f,
            avoidanceMemoryTime = 2f,
            clearPathCheckDistance = 8f,
            deltaTime = Time.deltaTime,
            avoidanceBlendSpeed = 2f,
            
            // Add boundary parameters
            enableBoundaries = enableBoundaries,
            boundaryCenter = new float3(boundaryCenter.x, boundaryCenter.y, boundaryCenter.z),
            boundarySize = new float3(boundarySize.x, boundarySize.y, boundarySize.z),
            emergencyBoundaryOffset = emergencyBoundaryOffset,
            
            avoidanceDirections = avoidanceDirections,
            avoidingFlags = avoidingFlags,
            emergencyFlags = emergencyFlags,
            avoidanceMemoryTimers = avoidanceMemoryTimers,
            stableAvoidanceDirections = stableAvoidanceDirections,
            avoidanceDirectionTimers = avoidanceDirectionTimers
        };
        obstacleJobHandle = obstacleJob.Schedule(totalFishCount, jobBatchSize);

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
            smoothedDesiredDirections = smoothedDesiredDirections,
            fearLevels = fearLevels
        };
        boidsJobHandle = boidsJob.Schedule(totalFishCount, jobBatchSize, obstacleJobHandle);

        jobsScheduled = true;
    }

    void UpdateObstaclesAndVirtualFish()
    {
        detectedObstacles.Clear();
        virtualFishData.Clear();

        // Find all obstacles in scene
        GameObject[] obstacles = GameObject.FindGameObjectsWithTag(obstacleTag);
        
        foreach (var obstacle in obstacles)
        {
            if (obstacle == null) continue;
            
            // Check if any real entities are near this obstacle
            bool entitiesNearby = false;
            Vector3 obstaclePos = obstacle.transform.position;
            
            for (int i = 0; i < allFish.Count; i++)
            {
                float distance = Vector3.Distance(allFish[i].position, obstaclePos);
                if (distance < obstacleDetectionRadius)
                {
                    entitiesNearby = true;
                    break;
                }
            }
            
            if (!entitiesNearby)
            {
                for (int i = 0; i < allDragons.Count; i++)
                {
                    float distance = Vector3.Distance(allDragons[i].position, obstaclePos);
                    if (distance < obstacleDetectionRadius)
                    {
                        entitiesNearby = true;
                        break;
                    }
                }
            }
            
            if (entitiesNearby)
            {
                detectedObstacles.Add(obstacle);
                CreateVirtualFishAroundObstacle(obstacle);
            }
        }
    }

    void CreateVirtualFishAroundObstacle(GameObject obstacle)
    {
        Vector3 center = obstacle.transform.position;
        Collider obstacleCollider = obstacle.GetComponent<Collider>();
        if (obstacleCollider == null) return;
        
        Bounds bounds = obstacleCollider.bounds;
        float baseRadius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
        
        // Create multiple rings of virtual fish for complete coverage
        for (int ring = 0; ring < virtualFishRings; ring++)
        {
            float currentRadius = baseRadius + virtualFishRadius + (ring * ringSpacing);
            int fishInThisRing = virtualFishPerRing + (ring * 4);
            
            for (int i = 0; i < fishInThisRing; i++)
            {
                float angle = (i / (float)fishInThisRing) * 2f * Mathf.PI;
                
                float radiusVariation = UnityEngine.Random.Range(-0.3f, 0.3f);
                float actualRadius = currentRadius + radiusVariation;
                
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * actualRadius,
                    center.y + UnityEngine.Random.Range(-bounds.size.y * 0.4f, bounds.size.y * 0.4f),
                    Mathf.Sin(angle) * actualRadius
                );
                
                Vector3 virtualPos = center + offset;
                
                float virtualSize = 6f - (ring * 1f);
                
                FishData virtualFish = new FishData
                {
                    position = new float3(virtualPos.x, virtualPos.y, virtualPos.z),
                    velocity = float3.zero,
                    species = 999,
                    fishSize = virtualSize,
                    targetPosition = float3.zero,
                    hasTarget = false,
                    lastAvoidanceDirection = float3.zero,
                    avoidanceMemoryTimer = 0f,
                    hasRecentAvoidanceMemory = false,
                    stableAvoidanceDirection = float3.zero,
                    avoidanceDirectionTimer = 0f,
                    smoothedDesiredDirection = float3.zero,
                    isAvoiding = false,
                    isInEmergency = false,
                    aggressionLevel = 0f,
                    huntingRadius = 0f,
                    disruptionStrength = 0f,
                    isHunting = false,
                    huntTarget = float3.zero,
                    energyLevel = 0f,
                    restTimer = 0f,
                    isResting = false,
                    fearLevel = 0f
                };
                
                virtualFishData.Add(virtualFish);
                
                if (virtualFishData.Count >= maxVirtualFish - 20) return;
            }
        }
        
        CreateInteriorVirtualFish(obstacle, bounds, center);
    }

    void CreateInteriorVirtualFish(GameObject obstacle, Bounds bounds, Vector3 center)
    {
        int interiorFish = 8;
        for (int i = 0; i < interiorFish; i++)
        {
            Vector3 randomInteriorPos = center + new Vector3(
                UnityEngine.Random.Range(-bounds.size.x * 0.3f, bounds.size.x * 0.3f),
                UnityEngine.Random.Range(-bounds.size.y * 0.3f, bounds.size.y * 0.3f),
                UnityEngine.Random.Range(-bounds.size.z * 0.3f, bounds.size.z * 0.3f)
            );
            
            FishData interiorVirtualFish = new FishData
            {
                position = new float3(randomInteriorPos.x, randomInteriorPos.y, randomInteriorPos.z),
                velocity = float3.zero,
                species = 998,
                fishSize = 8f,
                targetPosition = float3.zero,
                hasTarget = false,
                lastAvoidanceDirection = float3.zero,
                avoidanceMemoryTimer = 0f,
                hasRecentAvoidanceMemory = false,
                stableAvoidanceDirection = float3.zero,
                avoidanceDirectionTimer = 0f,
                smoothedDesiredDirection = float3.zero,
                isAvoiding = false,
                isInEmergency = false,
                aggressionLevel = 0f,
                huntingRadius = 0f,
                disruptionStrength = 0f,
                isHunting = false,
                huntTarget = float3.zero,
                energyLevel = 0f,
                restTimer = 0f,
                isResting = false,
                fearLevel = 0f
            };
            
            virtualFishData.Add(interiorVirtualFish);
            
            if (virtualFishData.Count >= maxVirtualFish) return;
        }
    }

    void DebugObstacleSystem()
    {
        Debug.Log($"OBSTACLE DEBUG - Real Fish: {allFish.Count}, Dragons: {allDragons.Count}, Virtual Fish: {virtualFishData.Count}, " +
                  $"Active Obstacles: {detectedObstacles.Count}, Max Virtual: {maxVirtualFish}");
        
        int exteriorVirtual = 0;
        int interiorVirtual = 0;
        
        foreach (var vf in virtualFishData)
        {
            if (vf.species == 999) exteriorVirtual++;
            else if (vf.species == 998) interiorVirtual++;
        }
        
        Debug.Log($"Virtual Fish Types - Exterior (999): {exteriorVirtual}, Interior (998): {interiorVirtual}");
    }

    void ConsumeJobResults()
    {
        // Update real fish
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
                avoidanceDirectionTimers[i],
                fearLevels[i]
            );
        }
        
        // Update dragons
        for (int i = 0; i < allDragons.Count; i++)
        {
            int dragonIndex = allFish.Count + i;
            allDragons[i].UpdateFromJobResult(
                desiredDirections[dragonIndex],
                smoothedDesiredDirections[dragonIndex],
                accelerations[dragonIndex],
                avoidingFlags[dragonIndex],
                emergencyFlags[dragonIndex],
                avoidanceMemoryTimers[dragonIndex],
                avoidanceDirections[dragonIndex],
                stableAvoidanceDirections[dragonIndex],
                avoidanceDirectionTimers[dragonIndex],
                fearLevels[dragonIndex]
            );
        }
    }

    void BuildInputData()
    {
        // Combine all entities
        int totalEntityCount = allFish.Count + allDragons.Count + virtualFishData.Count;
        
        // Real fish data
        for (int i = 0; i < allFish.Count; i++)
        {
            fishDataArray[i] = allFish[i].GetFishData();
        }
        
        // Dragon data
        for (int i = 0; i < allDragons.Count; i++)
        {
            fishDataArray[allFish.Count + i] = allDragons[i].GetFishData();
        }
        
        // Virtual fish data
        for (int i = 0; i < virtualFishData.Count; i++)
        {
            fishDataArray[allFish.Count + allDragons.Count + i] = virtualFishData[i];
        }

        // Clear and rebuild spatial hash with all entities
        spatialHashMap.Clear();
        for (int i = 0; i < totalEntityCount; i++)
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

    public void RegisterDragon(DragonController dragon)
    {
        if (allDragons.Count < maxDragons && !allDragons.Contains(dragon))
        {
            dragon.fishIndex = allDragons.Count;
            allDragons.Add(dragon);
        }
    }

    public void UnregisterDragon(DragonController dragon)
    {
        if (allDragons.Remove(dragon))
        {
            for (int i = 0; i < allDragons.Count; i++)
                allDragons[i].fishIndex = i;
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
        if (fearLevels.IsCreated) fearLevels.Dispose();
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

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        
        // Draw boundaries if enabled
        if (enableBoundaries && showBoundaries)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(boundaryCenter, boundarySize);
            
            // Draw emergency boundary
            Gizmos.color = Color.red;
            Vector3 emergencySize = boundarySize - Vector3.one * (emergencyBoundaryOffset * 2);
            Gizmos.DrawWireCube(boundaryCenter, emergencySize);
        }
        
        // Draw virtual fish as small spheres
        if (showVirtualFish)
        {
            foreach (var vf in virtualFishData)
            {
                Vector3 pos = new Vector3(vf.position.x, vf.position.y, vf.position.z);
                
                if (vf.species == 999) // Exterior virtual fish
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(pos, 0.3f);
                }
                else if (vf.species == 998) // Interior virtual fish
                {
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawSphere(pos, 0.2f);
                }
            }
        }
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, Screen.height - 280, 450, 270));
        GUILayout.Box("Fish Manager Debug - ENHANCED WITH DRAGONS");
        GUILayout.Label($"Real Fish: {allFish.Count}");
        GUILayout.Label($"Dragons: {allDragons.Count}");
        GUILayout.Label($"Virtual Fish: {virtualFishData.Count} / {maxVirtualFish}");
        GUILayout.Label($"Active Obstacles: {detectedObstacles.Count}");
        GUILayout.Label($"Separation Radius: {separationRadius:F1}");
        GUILayout.Label($"Separation Weight: {separationWeight:F1}");
        GUILayout.Label($"Virtual Fish Rings: {virtualFishRings}");
        GUILayout.Label($"Fish per Ring: {virtualFishPerRing}");
        GUILayout.Label($"Detection Radius: {obstacleDetectionRadius:F1}");
        
        // Boundary info
        GUILayout.Label($"Boundaries: {(enableBoundaries ? "ENABLED" : "DISABLED")}");
        if (enableBoundaries)
        {
            GUILayout.Label($"Boundary Center: {boundaryCenter}");
            GUILayout.Label($"Boundary Size: {boundarySize}");
        }
        
        // Dragon stats
        int huntingDragons = 0;
        int restingDragons = 0;
        foreach (var dragon in allDragons)
        {
            if (dragon.IsHunting()) huntingDragons++;
            if (dragon.IsResting()) restingDragons++;
        }
        GUILayout.Label($"Hunting Dragons: {huntingDragons}");
        GUILayout.Label($"Resting Dragons: {restingDragons}");
        
        GUILayout.EndArea();
    }
}