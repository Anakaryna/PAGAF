using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;

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
    public int maxVirtualFish = 500; // Reserve space for virtual fish

    [Header("Boids Parameters - NORMAL SPREAD SCHOOL")]
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

    [Header("Obstacle Avoidance as Virtual Fish")]
    public LayerMask obstacleLayer = 1;
    public string obstacleTag = "Obstacle";
    public float obstacleDetectionRadius = 15f;
    public float virtualFishRadius = 2.5f;
    public int virtualFishRings = 3; // Multiple rings around obstacles
    public int virtualFishPerRing = 12; // More fish per ring
    public float ringSpacing = 1.5f; // Distance between rings

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showObstacleDebug = true;
    public bool showVirtualFish = false; // Gizmo display

    readonly List<OptimizedFishController> allFish = new List<OptimizedFishController>();
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

        int totalCapacity = maxFishCount + maxVirtualFish;
        
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
        spatialHashMap = new NativeParallelMultiHashMap<int, int>(totalCapacity * 4, Allocator.Persistent);
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

        // Update obstacles and create virtual fish
        UpdateObstaclesAndVirtualFish();
        
        BuildInputData();

        if (showObstacleDebug && Time.frameCount % 120 == 0)
        {
            DebugObstacleSystem();
        }

        int totalFishCount = allFish.Count + virtualFishData.Count;

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
            smoothedDesiredDirections = smoothedDesiredDirections
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
            
            // Check if any real fish are near this obstacle
            bool fishNearby = false;
            Vector3 obstaclePos = obstacle.transform.position;
            
            for (int i = 0; i < allFish.Count; i++)
            {
                float distance = Vector3.Distance(allFish[i].position, obstaclePos);
                if (distance < obstacleDetectionRadius)
                {
                    fishNearby = true;
                    break;
                }
            }
            
            if (fishNearby)
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
            int fishInThisRing = virtualFishPerRing + (ring * 4); // More fish in outer rings
            
            // Create virtual fish in current ring
            for (int i = 0; i < fishInThisRing; i++)
            {
                float angle = (i / (float)fishInThisRing) * 2f * Mathf.PI;
                
                // Add some randomness to avoid perfect circles
                float radiusVariation = UnityEngine.Random.Range(-0.3f, 0.3f);
                float actualRadius = currentRadius + radiusVariation;
                
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * actualRadius,
                    center.y + UnityEngine.Random.Range(-bounds.size.y * 0.4f, bounds.size.y * 0.4f),
                    Mathf.Sin(angle) * actualRadius
                );
                
                Vector3 virtualPos = center + offset;
                
                // Make virtual fish size based on ring (inner = larger)
                float virtualSize = 6f - (ring * 1f); // Inner ring = size 6, outer = size 4
                
                FishData virtualFish = new FishData
                {
                    position = new float3(virtualPos.x, virtualPos.y, virtualPos.z),
                    velocity = float3.zero,
                    species = 999, // Virtual fish species
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
                    isInEmergency = false
                };
                
                virtualFishData.Add(virtualFish);
                
                // Safety check to avoid too many virtual fish
                if (virtualFishData.Count >= maxVirtualFish - 20) return;
            }
        }
        
        // Add virtual fish INSIDE the obstacle for extra safety
        CreateInteriorVirtualFish(obstacle, bounds, center);
    }

    void CreateInteriorVirtualFish(GameObject obstacle, Bounds bounds, Vector3 center)
    {
        // Add virtual fish inside the obstacle bounds as a last resort barrier
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
                species = 998, // Different species for interior virtual fish
                fishSize = 8f, // Very large for strong repulsion
                targetPosition = float3.zero,
                hasTarget = false,
                lastAvoidanceDirection = float3.zero,
                avoidanceMemoryTimer = 0f,
                hasRecentAvoidanceMemory = false,
                stableAvoidanceDirection = float3.zero,
                avoidanceDirectionTimer = 0f,
                smoothedDesiredDirection = float3.zero,
                isAvoiding = false,
                isInEmergency = false
            };
            
            virtualFishData.Add(interiorVirtualFish);
            
            // Safety check
            if (virtualFishData.Count >= maxVirtualFish) return;
        }
    }

    void DebugObstacleSystem()
    {
        Debug.Log($"OBSTACLE DEBUG - Real Fish: {allFish.Count}, Virtual Fish: {virtualFishData.Count}, " +
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
        // Only update real fish, not virtual ones
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
        // Combine real fish and virtual fish
        int totalFishCount = allFish.Count + virtualFishData.Count;
        
        // Real fish data
        for (int i = 0; i < allFish.Count; i++)
        {
            fishDataArray[i] = allFish[i].GetFishData();
        }
        
        // Add virtual fish data
        for (int i = 0; i < virtualFishData.Count; i++)
        {
            fishDataArray[allFish.Count + i] = virtualFishData[i];
        }

        // Clear and rebuild spatial hash with all fish (real + virtual)
        spatialHashMap.Clear();
        for (int i = 0; i < totalFishCount; i++)
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

    void OnDrawGizmos()
    {
        if (!showVirtualFish || !Application.isPlaying) return;
        
        // Draw virtual fish as small spheres
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

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, Screen.height - 200, 450, 190));
        GUILayout.Box("Fish Manager Debug - ENHANCED OBSTACLE AVOIDANCE");
        GUILayout.Label($"Real Fish: {allFish.Count}");
        GUILayout.Label($"Virtual Fish: {virtualFishData.Count} / {maxVirtualFish}");
        GUILayout.Label($"Active Obstacles: {detectedObstacles.Count}");
        GUILayout.Label($"Separation Radius: {separationRadius:F1}");
        GUILayout.Label($"Separation Weight: {separationWeight:F1}");
        GUILayout.Label($"Virtual Fish Rings: {virtualFishRings}");
        GUILayout.Label($"Fish per Ring: {virtualFishPerRing}");
        GUILayout.Label($"Detection Radius: {obstacleDetectionRadius:F1}");
        GUILayout.EndArea();
    }
}