using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

public struct FishData
{
    public float3 position;
    public float3 velocity;
    public int species;
    public float fishSize;
    public float3 targetPosition;
    public bool hasTarget;
    
    public float3 lastAvoidanceDirection;
    public float avoidanceMemoryTimer;
    public bool hasRecentAvoidanceMemory;
    public float3 stableAvoidanceDirection;
    public float avoidanceDirectionTimer;
    public float3 smoothedDesiredDirection;
    public bool isAvoiding;
    public bool isInEmergency;
    
    // Dragon-specific properties
    public float aggressionLevel;
    public float huntingRadius;
    public float disruptionStrength;
    public bool isHunting;
    public float3 huntTarget;
    public float energyLevel;
    public float restTimer;
    public bool isResting;
    public float fearLevel;
}

[BurstCompile]
public struct ObstacleAvoidanceJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<FishData> fishData;
    [ReadOnly] public float lookAheadDistance;
    [ReadOnly] public float emergencyDistance;
    [ReadOnly] public float avoidanceMemoryTime;
    [ReadOnly] public float clearPathCheckDistance;
    [ReadOnly] public float deltaTime;
    [ReadOnly] public float avoidanceBlendSpeed;
    
    // Configurable boundary settings
    [ReadOnly] public bool enableBoundaries;
    [ReadOnly] public float3 boundaryCenter;
    [ReadOnly] public float3 boundarySize;
    [ReadOnly] public float emergencyBoundaryOffset;
    
    public NativeArray<float3> avoidanceDirections;
    public NativeArray<bool> avoidingFlags;
    public NativeArray<bool> emergencyFlags;
    public NativeArray<float> avoidanceMemoryTimers;
    public NativeArray<float3> stableAvoidanceDirections;
    public NativeArray<float> avoidanceDirectionTimers;

    public void Execute(int index)
    {
        FishData fish = fishData[index];
        
        // Skip processing for virtual fish (obstacles) and dragons handle their own avoidance
        if (fish.species >= 998 || fish.species == 997)
        {
            avoidanceDirections[index] = float3.zero;
            avoidingFlags[index] = false;
            emergencyFlags[index] = false;
            avoidanceMemoryTimers[index] = 0f;
            stableAvoidanceDirections[index] = float3.zero;
            avoidanceDirectionTimers[index] = 0f;
            return;
        }
        
        float dirTimer = fish.avoidanceDirectionTimer;
        if (dirTimer > 0f) dirTimer -= deltaTime;
        
        float memoryTimer = fish.avoidanceMemoryTimer;
        bool hasMemory = fish.hasRecentAvoidanceMemory;
        if (hasMemory)
        {
            memoryTimer -= deltaTime;
            if (memoryTimer <= 0f) hasMemory = false;
        }
        
        // Calculate boundary avoidance (now configurable and much more lenient)
        float3 currentAvoidanceDirection = enableBoundaries ? 
            CalculateConfigurableBoundaryAvoidance(fish.position) : 
            float3.zero;
        
        bool isAvoiding = false;
        bool isEmergency = false;
        float3 finalAvoidanceDirection = float3.zero;
        float3 stableAvoidance = fish.stableAvoidanceDirection;
        
        if (!currentAvoidanceDirection.Equals(float3.zero))
        {
            isAvoiding = true;
            isEmergency = enableBoundaries && IsInEmergencyDistance(fish.position);
            
            if (stableAvoidance.Equals(float3.zero) || dirTimer <= 0f)
            {
                stableAvoidance = currentAvoidanceDirection;
                dirTimer = isEmergency ? 0.5f : 1.2f;
            }
            else
            {
                float blendFactor = deltaTime * avoidanceBlendSpeed;
                stableAvoidance = math.normalize(
                    math.lerp(stableAvoidance, currentAvoidanceDirection, blendFactor));
            }
            
            finalAvoidanceDirection = stableAvoidance;
            hasMemory = true;
            memoryTimer = avoidanceMemoryTime;
        }
        else if (hasMemory)
        {
            float3 targetDir = float3.zero;
            if (fish.hasTarget)
            {
                targetDir = math.normalize(fish.targetPosition - fish.position);
            }
            
            bool pathToTargetClear = IsPathClear(fish.position, targetDir);
            
            if (!pathToTargetClear)
            {
                isAvoiding = true;
                finalAvoidanceDirection = fish.lastAvoidanceDirection;
                float memoryStrength = memoryTimer / avoidanceMemoryTime;
                finalAvoidanceDirection *= memoryStrength;
            }
        }
        
        avoidanceDirections[index] = finalAvoidanceDirection;
        avoidingFlags[index] = isAvoiding;
        emergencyFlags[index] = isEmergency;
        avoidanceMemoryTimers[index] = memoryTimer;
        stableAvoidanceDirections[index] = stableAvoidance;
        avoidanceDirectionTimers[index] = dirTimer;
    }
    
    float3 CalculateConfigurableBoundaryAvoidance(float3 position)
    {
        float3 avoidDir = float3.zero;
        
        // Calculate relative position from boundary center
        float3 relativePos = position - boundaryCenter;
        
        // Half sizes for boundary checking
        float3 halfSize = boundarySize * 0.5f;
        
        // Check X boundary
        if (relativePos.x > halfSize.x)
            avoidDir.x = -1f;
        else if (relativePos.x < -halfSize.x)
            avoidDir.x = 1f;
        
        // Check Z boundary
        if (relativePos.z > halfSize.z)
            avoidDir.z = -1f;
        else if (relativePos.z < -halfSize.z)
            avoidDir.z = 1f;
        
        // Check Y boundary
        if (relativePos.y > halfSize.y)
            avoidDir.y = -1f;
        else if (relativePos.y < -halfSize.y)
            avoidDir.y = 1f;
        
        return math.lengthsq(avoidDir) > 0f ? math.normalize(avoidDir) : float3.zero;
    }
    
    bool IsInEmergencyDistance(float3 position)
    {
        // Calculate relative position from boundary center
        float3 relativePos = position - boundaryCenter;
        
        // Half sizes minus emergency offset
        float3 emergencyHalfSize = (boundarySize * 0.5f) - emergencyBoundaryOffset;
        
        return math.abs(relativePos.x) > emergencyHalfSize.x || 
               math.abs(relativePos.z) > emergencyHalfSize.z ||
               math.abs(relativePos.y) > emergencyHalfSize.y;
    }
    
    bool IsPathClear(float3 position, float3 direction)
    {
        if (direction.Equals(float3.zero) || !enableBoundaries) return true;
        
        float3 checkPos = position + direction * clearPathCheckDistance;
        
        // Calculate relative position from boundary center
        float3 relativePos = checkPos - boundaryCenter;
        float3 halfSize = boundarySize * 0.5f;
        
        return math.abs(relativePos.x) < halfSize.x && 
               math.abs(relativePos.z) < halfSize.z && 
               math.abs(relativePos.y) < halfSize.y;
    }
}

[BurstCompile]
public struct EnhancedBoidsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<FishData> fishData;
    [ReadOnly] public NativeArray<float3> avoidanceDirections;
    [ReadOnly] public NativeArray<bool> avoidingFlags;
    [ReadOnly] public NativeArray<bool> emergencyFlags;
    [ReadOnly] public NativeArray<float3> stableAvoidanceDirections;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> spatialHashMap;
    
    [ReadOnly] public float neighborRadius;
    [ReadOnly] public float separationRadius;
    [ReadOnly] public float separationWeight;
    [ReadOnly] public float alignmentWeight;
    [ReadOnly] public float cohesionWeight;
    [ReadOnly] public float targetWeight;
    [ReadOnly] public float baseAcceleration;
    [ReadOnly] public float boostAcceleration;
    [ReadOnly] public int maxNeighbors;
    [ReadOnly] public bool onlySchoolWithSameSpecies;
    [ReadOnly] public bool avoidLargerSpecies;
    [ReadOnly] public float cellSize;
    [ReadOnly] public int gridWidth;
    [ReadOnly] public int gridHeight;
    [ReadOnly] public float deltaTime;
    
    public NativeArray<float3> desiredDirections;
    public NativeArray<float> accelerations;
    public NativeArray<float3> smoothedDesiredDirections;
    public NativeArray<float> fearLevels;

    static float3 SafeNormalize(float3 v)
    {
        float lenSq = math.lengthsq(v);
        return lenSq > 1e-8f ? v * math.rsqrt(lenSq) : float3.zero;
    }

    public void Execute(int index)
    {
        FishData fish = fishData[index];
        
        // Skip processing for virtual fish (obstacles)
        if (fish.species >= 998)
        {
            desiredDirections[index] = float3.zero;
            accelerations[index] = 0f;
            smoothedDesiredDirections[index] = float3.zero;
            fearLevels[index] = 0f;
            return;
        }
        
        bool isAvoiding = avoidingFlags[index];
        bool isEmergency = emergencyFlags[index];
        float3 avoidanceDir = avoidanceDirections[index];
        bool isDragon = fish.species == 997;
        
        // Calculate boids forces
        float3 sep = CalculateSeparation(fish, index);
        float3 ali = CalculateAlignment(fish, index);
        float3 coh = CalculateCohesion(fish, index);
        float3 tgtDir = float3.zero;
        
        if (fish.hasTarget)
        {
            tgtDir = SafeNormalize(fish.targetPosition - fish.position);
        }

        float3 combined = float3.zero;
        float accel = baseAcceleration;
        float currentFear = 0f;

        if (isDragon)
        {
            // Dragon behavior
            combined = CalculateDragonBehavior(fish, sep, ali, coh, tgtDir, out accel);
        }
        else
        {
            // Normal fish behavior with dragon fear
            float3 dragonFear = CalculateDragonFear(fish, index, out currentFear);
            
            if (isAvoiding)
            {
                if (isEmergency)
                {
                    combined = avoidanceDir;
                    accel = boostAcceleration * 1.5f;
                }
                else
                {
                    combined += avoidanceDir * 3f;
                    combined += sep * separationWeight;
                    combined += dragonFear * 5f;
                    combined += tgtDir * (targetWeight * 0.5f);
                    accel = boostAcceleration;
                }
            }
            else
            {
                float sepMagnitude = math.length(sep);
                float dragonFearMagnitude = math.length(dragonFear);
                
                if (dragonFearMagnitude > 0.1f)
                {
                    // Dragon nearby - panic mode
                    combined += dragonFear * 6f;
                    combined += sep * separationWeight * 1.5f;
                    combined += ali * alignmentWeight * 0.8f;
                    combined += coh * cohesionWeight * 0.3f; // Less cohesion when panicking
                    accel = boostAcceleration * 1.5f;
                }
                else if (sepMagnitude > 0.4f)
                {
                    combined += sep * (separationWeight * 2.2f);
                    combined += ali * (alignmentWeight * 0.8f);
                    combined += coh * (cohesionWeight * 0.6f);
                    combined += tgtDir * targetWeight;
                    accel = baseAcceleration * 1.4f;
                }
                else
                {
                    combined += sep * separationWeight;
                    combined += ali * alignmentWeight;
                    combined += coh * cohesionWeight;
                    combined += tgtDir * targetWeight;
                    accel = baseAcceleration;
                }
            }
        }

        // Smooth the desired direction
        float3 newDesiredDir = SafeNormalize(combined);
        float3 currentSmoothed = fish.smoothedDesiredDirection;
        
        if (math.lengthsq(currentSmoothed) < 1e-8f)
        {
            currentSmoothed = newDesiredDir;
        }
        else
        {
            float smoothingSpeed = isDragon ? (fish.isHunting ? 8f : 4f) : 
                                  (isEmergency || currentFear > 0.5f ? 8f : 4f);
            float t = deltaTime * smoothingSpeed;
            
            if (math.lengthsq(newDesiredDir) > 1e-8f && math.lengthsq(currentSmoothed) > 1e-8f)
            {
                float dot = math.dot(currentSmoothed, newDesiredDir);
                dot = math.clamp(dot, -1f, 1f);
                
                if (dot > 0.9999f)
                {
                    currentSmoothed = newDesiredDir;
                }
                else
                {
                    float angle = math.acos(dot);
                    float slerpt = math.min(t, 1f);
                    float sinAngle = math.sin(angle);
                    
                    if (sinAngle > 1e-6f)
                    {
                        float a = math.sin((1f - slerpt) * angle) / sinAngle;
                        float b = math.sin(slerpt * angle) / sinAngle;
                        currentSmoothed = math.normalize(a * currentSmoothed + b * newDesiredDir);
                    }
                    else
                    {
                        currentSmoothed = newDesiredDir;
                    }
                }
            }
            else
            {
                currentSmoothed = newDesiredDir;
            }
        }

        desiredDirections[index] = newDesiredDir;
        smoothedDesiredDirections[index] = currentSmoothed;
        accelerations[index] = accel;
        fearLevels[index] = currentFear;
    }

    float3 CalculateDragonBehavior(FishData dragon, float3 sep, float3 ali, float3 coh, float3 tgtDir, out float accel)
    {
        float3 combined = float3.zero;
        
        if (dragon.isHunting && dragon.energyLevel > 0.3f)
        {
            // Hunting mode
            float3 huntDir = SafeNormalize(dragon.huntTarget - dragon.position);
            combined += huntDir * 5f;
            combined += sep * 0.5f; // Dragons care less about separation
            accel = boostAcceleration * 1.8f;
        }
        else if (dragon.isResting)
        {
            // Resting mode - gentle movement
            combined += sep * 0.3f;
            combined += ali * 0.2f;
            combined += tgtDir * 0.5f;
            accel = baseAcceleration * 0.4f;
        }
        else
        {
            // Patrol mode
            combined += sep * 0.7f;
            combined += ali * 0.5f;
            combined += coh * 0.3f;
            combined += tgtDir * 1.2f;
            accel = baseAcceleration * 1.3f;
        }
        
        return combined;
    }

    float3 CalculateDragonFear(FishData fish, int selfIdx, out float fearLevel)
    {
        float3 result = float3.zero;
        int count = 0;
        float maxFear = 0f;
        
        var neighbors = GetNeighbors(fish.position, selfIdx);
        for (int i = 0; i < neighbors.Length; i++)
        {
            int ni = neighbors[i];
            if (ni < 0) break;
            
            FishData neighbor = fishData[ni];
            
            // Only fear dragons
            if (neighbor.species == 997)
            {
                float3 diff = fish.position - neighbor.position;
                float dist = math.length(diff);
                
                float fearRadius = neighbor.huntingRadius;
                if (dist < fearRadius && dist > 0f)
                {
                    float3 normalized = math.normalize(diff);
                    float panicMultiplier = neighbor.isHunting ? 4f : 2f;
                    float distanceFactor = 1f - (dist / fearRadius);
                    
                    normalized *= panicMultiplier * distanceFactor;
                    result += normalized;
                    count++;
                    
                    maxFear = math.max(maxFear, distanceFactor * panicMultiplier);
                }
            }
        }
        
        neighbors.Dispose();
        
        fearLevel = math.clamp(maxFear, 0f, 1f);
        return count > 0 ? math.normalize(result) : float3.zero;
    }

    float3 CalculateSeparation(FishData fish, int selfIdx)
    {
        // Skip separation calculation for virtual fish
        if (fish.species >= 998) return float3.zero;
        
        float3 result = float3.zero;
        int count = 0;
        bool isDragon = fish.species == 997;

        var neighbors = GetNeighbors(fish.position, selfIdx);
        for (int i = 0; i < neighbors.Length && count < maxNeighbors; i++)
        {
            int ni = neighbors[i];
            if (ni < 0) break;

            FishData n = fishData[ni];
            float3 diff = fish.position - n.position;
            float dist = math.length(diff);

            float effRad = separationRadius;
            float forceMultiplier = 1f;
            
            // Dragon separation behavior
            if (isDragon)
            {
                if (n.species == 997) // Dragon to dragon
                {
                    effRad = separationRadius * 2f;
                    forceMultiplier = 0.8f;
                }
                else if (n.species >= 998) // Dragon to virtual fish
                {
                    effRad = separationRadius * 1.5f;
                    forceMultiplier = 2f;
                }
                else // Dragon to normal fish (less separation)
                {
                    effRad = separationRadius * 0.5f;
                    forceMultiplier = 0.3f;
                }
            }
            else
            {
                // Normal fish separation
                if (n.species == 999) // Exterior virtual fish
                {
                    effRad = separationRadius * 2.5f;
                    forceMultiplier = 3f;
                }
                else if (n.species == 998) // Interior virtual fish
                {
                    effRad = separationRadius * 3f;
                    forceMultiplier = 5f;
                }
                else if (n.species == 997) // Fear of dragons handled separately
                {
                    continue;
                }
                else
                {
                    if (avoidLargerSpecies && n.species != fish.species && n.fishSize > fish.fishSize)
                        effRad *= 1.5f;
                }
            }

            if (dist < effRad && dist > 0f)
            {
                float3 normalized = SafeNormalize(diff);
                
                if (n.species >= 998) // Virtual fish
                {
                    normalized /= (dist * 0.3f);
                    result += normalized * forceMultiplier;
                }
                else
                {
                    normalized /= dist;
                    result += normalized * forceMultiplier;
                }
                count++;
            }
        }

        neighbors.Dispose();
        
        if (count > 0)
        {
            result /= count;
            result = SafeNormalize(result);
        }
        
        return result;
    }

    float3 CalculateAlignment(FishData fish, int selfIdx)
    {
        // Skip for virtual fish
        if (fish.species >= 998) return float3.zero;
        
        float3 result = float3.zero;
        int count = 0;
        bool isDragon = fish.species == 997;

        var neighbors = GetNeighbors(fish.position, selfIdx);
        for (int i = 0; i < neighbors.Length && count < maxNeighbors; i++)
        {
            int ni = neighbors[i];
            if (ni < 0) break;

            FishData n = fishData[ni];
            
            // Dragons align with dragons, fish align with fish
            if (isDragon && n.species == 997)
            {
                result += SafeNormalize(n.velocity);
                count++;
            }
            else if (!isDragon && n.species < 997 && (!onlySchoolWithSameSpecies || n.species == fish.species))
            {
                result += SafeNormalize(n.velocity);
                count++;
            }
        }

        neighbors.Dispose();
        return count > 0 ? SafeNormalize(result / count) : float3.zero;
    }

    float3 CalculateCohesion(FishData fish, int selfIdx)
    {
        // Skip for virtual fish
        if (fish.species >= 998) return float3.zero;
        
        float3 center = float3.zero;
        int count = 0;
        bool isDragon = fish.species == 997;

        var neighbors = GetNeighbors(fish.position, selfIdx);
        for (int i = 0; i < neighbors.Length && count < maxNeighbors; i++)
        {
            int ni = neighbors[i];
            if (ni < 0) break;

            FishData n = fishData[ni];
            
            // Dragons cohere with dragons, fish cohere with fish
            if (isDragon && n.species == 997)
            {
                center += n.position;
                count++;
            }
            else if (!isDragon && n.species < 997 && (!onlySchoolWithSameSpecies || n.species == fish.species))
            {
                center += n.position;
                count++;
            }
        }

        neighbors.Dispose();
        if (count == 0) return float3.zero;

        center /= count;
        return SafeNormalize(center - fish.position);
    }

    NativeArray<int> GetNeighbors(float3 pos, int selfIdx)
    {
        var arr = new NativeArray<int>(maxNeighbors, Allocator.Temp);
        for (int i = 0; i < maxNeighbors; i++) arr[i] = -1;

        int found = 0;
        int gx = math.clamp((int)(pos.x / cellSize), 0, gridWidth - 1);
        int gz = math.clamp((int)(pos.z / cellSize), 0, gridHeight - 1);

        for (int dx = -1; dx <= 1 && found < maxNeighbors; dx++)
        {
            for (int dz = -1; dz <= 1 && found < maxNeighbors; dz++)
            {
                int cx = gx + dx;
                int cz = gz + dz;
                if (cx < 0 || cx >= gridWidth || cz < 0 || cz >= gridHeight)
                    continue;

                int hash = cx + cz * gridWidth;
                if (spatialHashMap.TryGetFirstValue(hash, out int ni, out var it))
                {
                    do
                    {
                        if (ni != selfIdx && ni >= 0 && ni < fishData.Length)
                        {
                            float3 d = pos - fishData[ni].position;
                            if (math.lengthsq(d) <= neighborRadius * neighborRadius)
                            {
                                arr[found++] = ni;
                                if (found >= maxNeighbors) break;
                            }
                        }
                    }
                    while (spatialHashMap.TryGetNextValue(out ni, ref it) && found < maxNeighbors);
                }
            }
        }

        return arr;
    }
}