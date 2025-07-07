using UnityEngine;
using Unity.Mathematics;

public class DragonController : MonoBehaviour
{
    [Header("Dragon Identity")]
    public FishSpecies species = FishSpecies.Dragon;
    public float dragonSize = 8f;
    
    [Header("Dragon Behavior")]
    public float aggressionLevel = 0.8f;
    public float huntingRadius = 15f;
    public float disruptionStrength = 3f;
    public float energyDecayRate = 0.1f;
    public float restDuration = 5f;
    public float huntingDuration = 10f;
    
    [Header("Dragon Movement")]
    public float maxSpeed = 8f;
    public float baseAcceleration = 4f;
    public float huntingAcceleration = 12f;
    public float waterFriction = 0.5f;
    public float turnSpeed = 120f;
    
    [Header("Dragon Targeting")]
    public float targetSwitchDistance = 20f;
    public float loseInterestDistance = 30f;
    public LayerMask preyLayer = -1;
    
    [Header("Animation")]
    public float normalSwimSpeed = 4f;
    public float huntingSwimSpeed = 8f;
    public float normalSwimIntensity = 0.8f;
    public float huntingSwimIntensity = 1.5f;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showDebugGizmos = true;

    // Internal state
    [System.NonSerialized] public int fishIndex;
    [System.NonSerialized] public Vector3 position;
    [System.NonSerialized] public Vector3 velocity;
    [System.NonSerialized] public Transform target;
    
    private Vector3 huntTarget;
    private bool isHunting;
    private float energyLevel = 1f;
    private float restTimer;
    private float huntTimer;
    private bool isResting;
    
    private Vector3 desiredDirection;
    private Vector3 smoothedDesiredDirection;
    private float currentAcceleration;
    
    private MaterialPropertyBlock propertyBlock;
    private Renderer dragonRenderer;
    private Material dragonMaterial;
    
    private float smoothedSwimSpeed;
    private float smoothedSwimIntensity;
    private float swimSpeedVelocity;
    private float swimIntensityVelocity;
    
    void Start()
    {
        dragonRenderer = GetComponent<Renderer>();
        if (dragonRenderer != null)
        {
            propertyBlock = new MaterialPropertyBlock();
            dragonMaterial = dragonRenderer.material;
        }
        
        velocity = Vector3.zero;
        position = transform.position;
        energyLevel = 1f;
        smoothedDesiredDirection = Vector3.zero;
        
        // Register with the fish manager
        if (OptimizedFishManager.Instance != null)
            OptimizedFishManager.Instance.RegisterDragon(this);
    }
    
    void OnDestroy()
    {
        if (OptimizedFishManager.Instance != null)
            OptimizedFishManager.Instance.UnregisterDragon(this);
    }
    
    void Update()
    {
        position = transform.position;
        UpdateDragonBehavior();
        UpdateMovement();
        UpdateRotation();
        UpdateSwimmingAnimation();
    }
    
    void UpdateDragonBehavior()
    {
        // Energy management
        if (isHunting)
        {
            energyLevel -= energyDecayRate * Time.deltaTime;
            huntTimer -= Time.deltaTime;
            
            if (energyLevel <= 0.2f || huntTimer <= 0f)
            {
                EnterRestState();
            }
        }
        else if (isResting)
        {
            restTimer -= Time.deltaTime;
            energyLevel += energyDecayRate * 0.5f * Time.deltaTime;
            
            if (restTimer <= 0f || energyLevel >= 1f)
            {
                ExitRestState();
            }
        }
        else
        {
            energyLevel = Mathf.Clamp01(energyLevel + energyDecayRate * 0.3f * Time.deltaTime);
        }
        
        // Hunting behavior
        if (!isResting && energyLevel > 0.3f)
        {
            FindHuntTarget();
        }
    }
    
    void FindHuntTarget()
    {
        // Find nearby fish to hunt
        var nearbyFish = FindObjectsOfType<OptimizedFishController>();
        float closestDistance = float.MaxValue;
        Vector3 closestFishPos = Vector3.zero;
        bool foundTarget = false;
        
        foreach (var fish in nearbyFish)
        {
            if (fish.GetSpecies() == FishSpecies.Dragon) continue;
            
            float distance = Vector3.Distance(position, fish.transform.position);
            if (distance < huntingRadius && distance < closestDistance)
            {
                closestDistance = distance;
                closestFishPos = fish.transform.position;
                foundTarget = true;
            }
        }
        
        if (foundTarget && !isHunting)
        {
            huntTarget = closestFishPos;
            isHunting = true;
            huntTimer = huntingDuration;
        }
        else if (isHunting && Vector3.Distance(position, huntTarget) > loseInterestDistance)
        {
            isHunting = false;
            huntTimer = 0f;
        }
    }
    
    void EnterRestState()
    {
        isResting = true;
        isHunting = false;
        huntTimer = 0f;
        restTimer = restDuration;
    }
    
    void ExitRestState()
    {
        isResting = false;
        restTimer = 0f;
    }
    
    public void UpdateFromJobResult(
        float3 newDesiredDir,
        float3 newSmoothedDir,
        float newAccel,
        bool avoiding,
        bool emergency,
        float memoryTimer,
        float3 avoidanceDir,
        float3 stableAvoidDir,
        float dirTimer,
        float newFearLevel)
    {
        desiredDirection = new Vector3(newDesiredDir.x, newDesiredDir.y, newDesiredDir.z);
        smoothedDesiredDirection = new Vector3(newSmoothedDir.x, newSmoothedDir.y, newSmoothedDir.z);
        currentAcceleration = newAccel;
        
        // Dragons override with their own behavior
        if (isHunting && !isResting)
        {
            Vector3 huntDir = (huntTarget - position).normalized;
            desiredDirection = Vector3.Lerp(desiredDirection, huntDir, 0.8f);
            smoothedDesiredDirection = Vector3.Slerp(smoothedDesiredDirection, huntDir, Time.deltaTime * 6f);
            currentAcceleration = huntingAcceleration;
        }
        else if (isResting)
        {
            // Gentle wandering when resting
            desiredDirection = Vector3.Lerp(desiredDirection, Vector3.zero, 0.5f);
            smoothedDesiredDirection = Vector3.Slerp(smoothedDesiredDirection, desiredDirection, Time.deltaTime * 2f);
            currentAcceleration = baseAcceleration * 0.3f;
        }
    }
    
    void UpdateMovement()
    {
        velocity -= velocity * waterFriction * Time.deltaTime;
        
        if (smoothedDesiredDirection != Vector3.zero)
        {
            velocity += smoothedDesiredDirection * currentAcceleration * Time.deltaTime;
        }
        
        float currentMaxSpeed = isHunting ? maxSpeed * 1.2f : maxSpeed;
        if (velocity.magnitude > currentMaxSpeed)
        {
            velocity = velocity.normalized * currentMaxSpeed;
        }
        
        position += velocity * Time.deltaTime;
        transform.position = position;
    }
    
    void UpdateRotation()
    {
        if (velocity.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
            float currentTurnSpeed = isHunting ? turnSpeed * 1.5f : turnSpeed;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, currentTurnSpeed * Time.deltaTime);
        }
    }
    
    void UpdateSwimmingAnimation()
    {
        float targetSpeed = isHunting ? huntingSwimSpeed : normalSwimSpeed;
        float targetIntensity = isHunting ? huntingSwimIntensity : normalSwimIntensity;
        
        if (isResting)
        {
            targetSpeed *= 0.4f;
            targetIntensity *= 0.3f;
        }
        
        smoothedSwimSpeed = Mathf.SmoothDamp(smoothedSwimSpeed, targetSpeed, ref swimSpeedVelocity, 0.3f);
        smoothedSwimIntensity = Mathf.SmoothDamp(smoothedSwimIntensity, targetIntensity, ref swimIntensityVelocity, 0.3f);
        
        if (dragonRenderer != null && propertyBlock != null)
        {
            propertyBlock.SetFloat("_SwimSpeed", smoothedSwimSpeed);
            propertyBlock.SetFloat("_SwimIntensity", smoothedSwimIntensity);
            propertyBlock.SetFloat("_AggressionLevel", aggressionLevel);
            propertyBlock.SetFloat("_EnergyLevel", energyLevel);
            propertyBlock.SetFloat("_IsHunting", isHunting ? 1f : 0f);
            dragonRenderer.SetPropertyBlock(propertyBlock);
        }
        
        if (dragonMaterial != null)
        {
            dragonMaterial.SetFloat("_SwimSpeed", smoothedSwimSpeed);
            dragonMaterial.SetFloat("_SwimIntensity", smoothedSwimIntensity);
            dragonMaterial.SetFloat("_AggressionLevel", aggressionLevel);
            dragonMaterial.SetFloat("_EnergyLevel", energyLevel);
            dragonMaterial.SetFloat("_IsHunting", isHunting ? 1f : 0f);
        }
    }
    
    public FishData GetFishData()
    {
        return new FishData
        {
            position = new float3(position.x, position.y, position.z),
            velocity = new float3(velocity.x, velocity.y, velocity.z),
            species = (int)species,
            fishSize = dragonSize,
            targetPosition = target != null ? new float3(target.position.x, target.position.y, target.position.z) : float3.zero,
            hasTarget = target != null,
            
            aggressionLevel = aggressionLevel,
            huntingRadius = huntingRadius,
            disruptionStrength = disruptionStrength,
            isHunting = isHunting,
            huntTarget = new float3(huntTarget.x, huntTarget.y, huntTarget.z),
            energyLevel = energyLevel,
            restTimer = restTimer,
            isResting = isResting,
            
            smoothedDesiredDirection = new float3(smoothedDesiredDirection.x, smoothedDesiredDirection.y, smoothedDesiredDirection.z),
            isAvoiding = false,
            isInEmergency = false,
            fearLevel = 0f,
            
            // Default fish values
            lastAvoidanceDirection = float3.zero,
            avoidanceMemoryTimer = 0f,
            hasRecentAvoidanceMemory = false,
            stableAvoidanceDirection = float3.zero,
            avoidanceDirectionTimer = 0f
        };
    }
    
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    public FishSpecies GetSpecies() => species;
    public float GetCurrentSpeed() => velocity.magnitude;
    public Vector3 GetVelocity() => velocity;
    public bool IsHunting() => isHunting;
    public bool IsResting() => isResting;
    public float GetEnergyLevel() => energyLevel;
    public float GetAggressionLevel() => aggressionLevel;
    
    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        // Draw hunting radius
        Gizmos.color = isHunting ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, huntingRadius);
        
        // Draw hunt target
        if (isHunting)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(huntTarget, 1f);
            Gizmos.DrawLine(transform.position, huntTarget);
        }
        
        // Draw energy level
        Gizmos.color = Color.Lerp(Color.red, Color.green, energyLevel);
        Vector3 energyBarStart = transform.position + Vector3.up * 3f;
        Vector3 energyBarEnd = energyBarStart + Vector3.right * (energyLevel * 3f);
        Gizmos.DrawLine(energyBarStart, energyBarEnd);
        
        // Draw state indicators
        if (isResting)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 2f, Vector3.one * 0.5f);
        }
    }
    
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2f);
        if (screenPos.z > 0)
        {
            GUI.Label(new Rect(screenPos.x, Screen.height - screenPos.y, 200, 80), 
                $"Dragon\nEnergy: {energyLevel:F2}\nHunting: {isHunting}\nResting: {isResting}");
        }
    }
}