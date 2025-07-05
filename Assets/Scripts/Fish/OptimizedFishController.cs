// using UnityEngine;
// using Unity.Mathematics;
// using System.Collections.Generic;
//
// public class OptimizedFishController : MonoBehaviour
// {
//     [Header("Fish Species")]
//     public FishSpecies species;
//     public float fishSize = 1f;
//     
//     [Header("Target Following")]
//     public float arrivalDistance = 1f;
//     
//     [Header("Movement Parameters")]
//     public float maxSpeed = 5f;
//     public float baseAcceleration = 2f;
//     public float boostAcceleration = 8f;
//     public float waterFriction = 1f;
//     public float turnSpeed = 90f;
//     
//     [Header("Boids Behavior")]
//     public float neighborRadius = 3f;
//     public float separationRadius = 1.5f;
//     public LayerMask fishLayer = -1;
//     [Space]
//     public float separationWeight = 1.5f;
//     public float alignmentWeight = 1f;
//     public float cohesionWeight = 1f;
//     public float targetWeight = 2f;
//     [Space]
//     public int maxNeighbors = 20;
//     public bool onlySchoolWithSameSpecies = true;
//     public bool avoidLargerSpecies = true;
//     
//     [Header("Obstacle Avoidance")]
//     public float lookAheadDistance = 5f;
//     public float avoidanceRadius = 2f;
//     public float emergencyDistance = 1.5f;
//     public int rayCount = 3;
//     public float raySpread = 45f;
//     public LayerMask obstacleLayer = 1;
//     [Space]
//     [Header("Avoidance Stability")]
//     public float avoidanceMemoryTime = 2f;
//     public float avoidanceBlendSpeed = 2f;
//     public float clearPathCheckDistance = 8f;
//     
//     [Header("Animation Tuning")]
//     [Space]
//     [Header("Swim Speed Mapping")]
//     public float normalSwimSpeed = 3.5f;
//     public float fastSwimSpeed = 5.0f;
//     [Space]
//     [Header("Swim Intensity Mapping")]  
//     public float normalSwimIntensity = 0.6f;
//     public float fastSwimIntensity = 1.1f;
//     [Space]
//     [Header("Turn Animation")]
//     public float turnSpeedBoost = 0.5f;
//     public float turnIntensityBoost = 0.2f;
//     public float animationSmoothTime = 0.25f;
//     public float angularInfluenceOnMovement = 0.3f;
//     
//     [Header("Debug")]
//     public bool showDebugGUI = false;
//     public bool showDebugGizmos = true;
//     public bool showBoidsDebug = false;
//
//     // Job-visible fields
//     [System.NonSerialized] public int fishIndex;
//     [System.NonSerialized] public Vector3 position;
//     [System.NonSerialized] public Vector3 velocity;
//     [System.NonSerialized] public Transform target;
//
//     // Internal state
//     private Vector3 desiredDirection;
//     private Vector3 smoothedDesiredDirection;
//     private Vector3 stableAvoidanceDirection;
//     private float avoidanceDirectionTimer;
//     private float currentAcceleration;
//     private float angularAcceleration;
//     
//     private float avoidanceMemoryTimer;
//     private Vector3 lastKnownAvoidanceDirection;
//     private bool hasRecentAvoidanceMemory;
//     
//     private MaterialPropertyBlock propertyBlock;
//     private Renderer fishRenderer;
//     private Material fishMaterial;
//     
//     private float smoothedSwimSpeed;
//     private float smoothedSwimIntensity;
//     private float swimSpeedVelocity;
//     private float swimIntensityVelocity;
//
//     private bool isAvoiding = false;
//     private bool isInEmergency = false;
//     
//     // Job result placeholders
//     private Vector3 jobSeparationForce;
//     private Vector3 jobAlignmentForce;
//     private Vector3 jobCohesionForce;
//     private Vector3 jobAvoidanceDirection;
//     private float jobAvoidanceMemoryTimer;
//
//     static Vector3 SafeNormalize(Vector3 v)
//     {
//         return v.magnitude > 0.001f ? v.normalized : Vector3.zero;
//     }
//
//     void Start()
//     {
//         fishRenderer = GetComponent<Renderer>();
//         if (fishRenderer != null)
//         {
//             propertyBlock = new MaterialPropertyBlock();
//             fishMaterial = fishRenderer.material;
//         }
//
//         velocity = Vector3.zero;
//         smoothedSwimSpeed = normalSwimSpeed;
//         smoothedSwimIntensity = normalSwimIntensity;
//         stableAvoidanceDirection = Vector3.zero;
//         smoothedDesiredDirection = Vector3.zero;
//         lastKnownAvoidanceDirection = Vector3.zero;
//         hasRecentAvoidanceMemory = false;
//         
//         position = transform.position;
//
//         if (OptimizedFishManager.Instance != null)
//             OptimizedFishManager.Instance.RegisterFish(this);
//         else
//             Debug.LogError("OptimizedFishManager.Instance is null!");
//     }
//
//     void OnDestroy()
//     {
//         if (OptimizedFishManager.Instance != null)
//             OptimizedFishManager.Instance.UnregisterFish(this);
//     }
//
//     void Update()
//     {
//         // decay avoidance memory
//         if (hasRecentAvoidanceMemory)
//         {
//             avoidanceMemoryTimer -= Time.deltaTime;
//             if (avoidanceMemoryTimer <= 0f)
//                 hasRecentAvoidanceMemory = false;
//         }
//         
//         UpdateDesiredDirection();
//         UpdateMovement();
//         UpdateRotation();
//         UpdateSwimmingAnimation();
//     }
//
//     public void SetTarget(Transform newTarget)
//     {
//         target = newTarget;
//     }
//
//     void UpdateDesiredDirection()
//     {
//         Vector3 targetDir = Vector3.zero;
//         if (target != null)
//             targetDir = (target.position - transform.position).normalized;
//
//         Vector3 combined = Vector3.zero;
//         Vector3 avoidanceDir = jobAvoidanceDirection;
//         bool shouldAvoid = false;
//
//         // --- avoidance logic (same as before) ---
//         // check immediate avoidance
//         if (avoidanceDir != Vector3.zero)
//         {
//             shouldAvoid = true;
//             if (stableAvoidanceDirection == Vector3.zero || avoidanceDirectionTimer <= 0f)
//             {
//                 stableAvoidanceDirection = avoidanceDir;
//                 avoidanceDirectionTimer = isInEmergency ? 0.5f : 1.2f;
//             }
//             else
//             {
//                 stableAvoidanceDirection = Vector3.Slerp(
//                     stableAvoidanceDirection,
//                     avoidanceDir,
//                     Time.deltaTime * avoidanceBlendSpeed
//                 );
//             }
//             avoidanceDirectionTimer -= Time.deltaTime;
//             lastKnownAvoidanceDirection = stableAvoidanceDirection;
//             hasRecentAvoidanceMemory = true;
//             avoidanceMemoryTimer = avoidanceMemoryTime;
//         }
//         else if (hasRecentAvoidanceMemory)
//         {
//             // if path to target still blocked, keep using memory
//             if (!Physics.Raycast(transform.position, targetDir, clearPathCheckDistance, obstacleLayer))
//             {
//                 shouldAvoid = true;
//                 float memStr = avoidanceMemoryTimer / avoidanceMemoryTime;
//                 stableAvoidanceDirection *= memStr;
//             }
//         }
//
//         if (shouldAvoid)
//         {
//             isAvoiding = true;
//             if (isInEmergency)
//             {
//                 combined = stableAvoidanceDirection;
//                 currentAcceleration = boostAcceleration * 1.5f;
//             }
//             else
//             {
//                 combined += stableAvoidanceDirection * 4f;
//                 combined += jobSeparationForce * separationWeight;
//                 combined += targetDir * (targetWeight * 0.1f);
//                 currentAcceleration = boostAcceleration;
//             }
//         }
//         else
//         {
//             isAvoiding = false;
//             isInEmergency = false;
//             combined += jobSeparationForce * separationWeight;
//             combined += jobAlignmentForce * alignmentWeight;
//             combined += jobCohesionForce * cohesionWeight;
//             combined += targetDir * targetWeight;
//             currentAcceleration = baseAcceleration;
//             if (jobSeparationForce.magnitude > 0.5f)
//                 currentAcceleration = Mathf.Lerp(baseAcceleration, boostAcceleration, jobSeparationForce.magnitude);
//         }
//
//         Vector3 newDir = combined.normalized;
//         float smoothSpeed = isInEmergency ? 6f : 3f;
//         smoothedDesiredDirection = (smoothedDesiredDirection == Vector3.zero)
//             ? newDir
//             : Vector3.Slerp(smoothedDesiredDirection, newDir, Time.deltaTime * smoothSpeed);
//
//         desiredDirection = smoothedDesiredDirection;
//     }
//
//     void UpdateMovement()
//     {
//         velocity -= velocity * waterFriction * Time.deltaTime;
//         if (desiredDirection != Vector3.zero)
//             velocity += desiredDirection * currentAcceleration * Time.deltaTime;
//
//         float maxSpd = isInEmergency ? maxSpeed * 1.3f : maxSpeed;
//         if (velocity.magnitude > maxSpd)
//             velocity = velocity.normalized * maxSpd;
//
//         position += velocity * Time.deltaTime;
//         transform.position = position;
//     }
//
//     void UpdateRotation()
//     {
//         if (velocity.magnitude < 0.1f) return;
//         Quaternion tgt = Quaternion.LookRotation(velocity.normalized);
//         float turnMul = isAvoiding ? (isInEmergency ? 1.8f : 1.3f) : 1f;
//         transform.rotation = Quaternion.Slerp(transform.rotation, tgt, turnSpeed * turnMul * Time.deltaTime);
//     }
//
//     void UpdateSwimmingAnimation()
//     {
//         float t = Mathf.Clamp01(currentAcceleration / boostAcceleration);
//         float tgtSpeed = Mathf.Lerp(normalSwimSpeed, fastSwimSpeed, t);
//         float tgtInt   = Mathf.Lerp(normalSwimIntensity, fastSwimIntensity, t);
//
//         if (isInEmergency)
//         {
//             tgtSpeed = Mathf.Max(tgtSpeed, fastSwimSpeed * 1.1f);
//             tgtInt   = Mathf.Max(tgtInt,   fastSwimIntensity * 1.2f);
//         }
//         else if (isAvoiding)
//         {
//             tgtSpeed += 0.4f;
//             tgtInt   += 0.3f;
//         }
//
//         float smoothT = isInEmergency ? animationSmoothTime * 0.5f : animationSmoothTime;
//         smoothedSwimSpeed     = Mathf.SmoothDamp(smoothedSwimSpeed,     tgtSpeed, ref swimSpeedVelocity,     smoothT);
//         smoothedSwimIntensity = Mathf.SmoothDamp(smoothedSwimIntensity, tgtInt,   ref swimIntensityVelocity, smoothT);
//
//         if (fishRenderer != null && propertyBlock != null)
//         {
//             propertyBlock.SetFloat("_SwimSpeed",     smoothedSwimSpeed);
//             propertyBlock.SetFloat("_SwimIntensity", smoothedSwimIntensity);
//             fishRenderer.SetPropertyBlock(propertyBlock);
//         }
//
//         if (fishMaterial != null)
//         {
//             fishMaterial.SetFloat("_SwimSpeed",     smoothedSwimSpeed);
//             fishMaterial.SetFloat("_SwimIntensity", smoothedSwimIntensity);
//         }
//     }
//
//     // ←— **Updated** signature to match 7-arg call
//     public void UpdateFromJobResult(
//         float3 newDesiredDir,
//         float3 newSmoothedDir,
//         float newAccel,
//         bool avoiding,
//         bool emergency,
//         float memoryTimer,
//         float3 avoidanceDir)
//     {
//         desiredDirection = new Vector3(newDesiredDir.x, newDesiredDir.y, newDesiredDir.z);
//         smoothedDesiredDirection = new Vector3(newSmoothedDir.x, newSmoothedDir.y, newSmoothedDir.z);
//         currentAcceleration      = newAccel;
//         isAvoiding               = avoiding;
//         isInEmergency            = emergency;
//         avoidanceMemoryTimer     = memoryTimer;
//         lastKnownAvoidanceDirection =
//             new Vector3(avoidanceDir.x, avoidanceDir.y, avoidanceDir.z);
//     }
//
//     public FishData GetFishData()
//     {
//         return new FishData
//         {
//             position                = new float3(position.x, position.y, position.z),
//             velocity                = new float3(velocity.x, velocity.y, velocity.z),
//             species                 = (int)species,
//             fishSize                = fishSize,
//             targetPosition          = target != null
//                                         ? new float3(target.position.x,
//                                                      target.position.y,
//                                                      target.position.z)
//                                         : float3.zero,
//             hasTarget               = target != null,
//             lastAvoidanceDirection  = new float3(
//                                         lastKnownAvoidanceDirection.x,
//                                         lastKnownAvoidanceDirection.y,
//                                         lastKnownAvoidanceDirection.z),
//             avoidanceMemoryTimer    = avoidanceMemoryTimer,
//             isAvoiding              = isAvoiding,
//             isInEmergency           = isInEmergency,
//             smoothedDesiredDirection= new float3(
//                                         smoothedDesiredDirection.x,
//                                         smoothedDesiredDirection.y,
//                                         smoothedDesiredDirection.z)
//         };
//     }
// }
using UnityEngine;
using Unity.Mathematics;

public class OptimizedFishController : MonoBehaviour
{
    [Header("Fish Species")]
    public FishSpecies species;
    public float fishSize = 1f;
    
    [Header("Target Following")]
    public float arrivalDistance = 1f;
    
    [Header("Movement Parameters")]
    public float maxSpeed = 5f;
    public float baseAcceleration = 2f;
    public float boostAcceleration = 8f;
    public float waterFriction = 1f;
    public float turnSpeed = 90f;
    
    [Header("Boids Behavior")]
    public float neighborRadius = 3f;
    public float separationRadius = 1.5f;
    public LayerMask fishLayer = -1;
    [Space]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;
    public float targetWeight = 2f;
    [Space]
    public int maxNeighbors = 20;
    public bool onlySchoolWithSameSpecies = true;
    public bool avoidLargerSpecies = true;
    
    [Header("Obstacle Avoidance")]
    public float lookAheadDistance = 5f;
    public float avoidanceRadius = 2f;
    public float emergencyDistance = 1.5f;
    public int rayCount = 3;
    public float raySpread = 45f;
    public LayerMask obstacleLayer = 1;
    [Space]
    [Header("Avoidance Stability")]
    public float avoidanceMemoryTime = 2f;
    public float avoidanceBlendSpeed = 2f;
    public float clearPathCheckDistance = 8f;
    
    [Header("Animation Tuning")]
    [Space]
    [Header("Swim Speed Mapping")]
    public float normalSwimSpeed = 3.5f;
    public float fastSwimSpeed = 5.0f;
    [Space]
    [Header("Swim Intensity Mapping")]  
    public float normalSwimIntensity = 0.6f;
    public float fastSwimIntensity = 1.1f;
    [Space]
    [Header("Turn Animation")]
    public float turnSpeedBoost = 0.5f;
    public float turnIntensityBoost = 0.2f;
    public float animationSmoothTime = 0.25f;
    public float angularInfluenceOnMovement = 0.3f;
    
    [Header("Debug")]
    public bool showDebugGUI = false;
    public bool showDebugGizmos = true;
    public bool showBoidsDebug = false;

    // Job-visible fields
    [System.NonSerialized] public int fishIndex;
    [System.NonSerialized] public Vector3 position;
    [System.NonSerialized] public Vector3 velocity;
    [System.NonSerialized] public Transform target;

    // Internal state
    private Vector3 desiredDirection;
    private Vector3 smoothedDesiredDirection;
    private Vector3 stableAvoidanceDirection;
    private float avoidanceDirectionTimer;
    private Vector3 lastKnownAvoidanceDirection;
    private bool hasRecentAvoidanceMemory;
    private float avoidanceMemoryTimer;
    
    private float currentAcceleration;
    private float angularAcceleration;
    
    private MaterialPropertyBlock propertyBlock;
    private Renderer fishRenderer;
    private Material fishMaterial;
    
    private float smoothedSwimSpeed;
    private float smoothedSwimIntensity;
    private float swimSpeedVelocity;
    private float swimIntensityVelocity;

    private bool isAvoiding = false;
    private bool isInEmergency = false;
    
    private float angularIntensity;

    void Start()
    {
        fishRenderer = GetComponent<Renderer>();
        if (fishRenderer != null)
        {
            propertyBlock = new MaterialPropertyBlock();
            fishMaterial = fishRenderer.material;
        }
        
        velocity = Vector3.zero;
        smoothedSwimSpeed = normalSwimSpeed;
        smoothedSwimIntensity = normalSwimIntensity;
        stableAvoidanceDirection = Vector3.zero;
        smoothedDesiredDirection = Vector3.zero;
        lastKnownAvoidanceDirection = Vector3.zero;
        hasRecentAvoidanceMemory = false;
        
        position = transform.position;

        if (OptimizedFishManager.Instance != null)
            OptimizedFishManager.Instance.RegisterFish(this);
    }

    void OnDestroy()
    {
        if (OptimizedFishManager.Instance != null)
            OptimizedFishManager.Instance.UnregisterFish(this);
    }

    void Update()
    {
        position = transform.position;
        UpdateMovement();
        UpdateRotation();
        UpdateSwimmingAnimation();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
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
        float dirTimer)
    {
        desiredDirection = new Vector3(newDesiredDir.x, newDesiredDir.y, newDesiredDir.z);
        smoothedDesiredDirection = new Vector3(newSmoothedDir.x, newSmoothedDir.y, newSmoothedDir.z);
        currentAcceleration = newAccel;
        isAvoiding = avoiding;
        isInEmergency = emergency;
        avoidanceMemoryTimer = memoryTimer;
        lastKnownAvoidanceDirection = new Vector3(avoidanceDir.x, avoidanceDir.y, avoidanceDir.z);
        stableAvoidanceDirection = new Vector3(stableAvoidDir.x, stableAvoidDir.y, stableAvoidDir.z);
        avoidanceDirectionTimer = dirTimer;
        hasRecentAvoidanceMemory = memoryTimer > 0f;
    }

    void UpdateMovement()
    {
        velocity -= velocity * waterFriction * Time.deltaTime;
    
        if (smoothedDesiredDirection != Vector3.zero)
        {
            velocity += smoothedDesiredDirection * currentAcceleration * Time.deltaTime;
        }
    
        float angularContribution = angularAcceleration * angularInfluenceOnMovement;
        if (isAvoiding)
        {
            angularContribution *= 0.3f;
        }
        velocity += transform.forward * angularContribution * Time.deltaTime;
    
        // REMOVED Y dampening - fish need to follow targets up and down!
    
        float currentMaxSpeed = isInEmergency ? maxSpeed * 1.3f : maxSpeed;
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
            
            float currentTurnSpeed = turnSpeed;
            if (isAvoiding)
            {
                currentTurnSpeed *= isInEmergency ? 1.8f : 1.3f;
            }
            
            transform.rotation = Quaternion.Slerp(
                transform.rotation, 
                targetRotation, 
                currentTurnSpeed * Time.deltaTime
            );
            
            float angleDifference = Quaternion.Angle(transform.rotation, targetRotation);
            angularAcceleration = angleDifference * 0.1f;
            angularIntensity = Mathf.Clamp01(angleDifference / 90f);
        }
        else
        {
            angularAcceleration = 0f;
            angularIntensity = 0f;
        }
    }

    void UpdateSwimmingAnimation()
    {
        float speedMultiplier = Mathf.Clamp01(currentAcceleration / boostAcceleration);
        
        float targetSwimSpeed = Mathf.Lerp(normalSwimSpeed, fastSwimSpeed, speedMultiplier);
        float targetSwimIntensity = Mathf.Lerp(normalSwimIntensity, fastSwimIntensity, speedMultiplier);
        
        if (angularIntensity > 0.1f && !isInEmergency)
        {
            targetSwimSpeed += turnSpeedBoost * angularIntensity * 0.5f;
            targetSwimIntensity += turnIntensityBoost * angularIntensity * 0.5f;
        }
        
        if (isInEmergency)
        {
            targetSwimSpeed = Mathf.Max(targetSwimSpeed, fastSwimSpeed * 1.1f);
            targetSwimIntensity = Mathf.Max(targetSwimIntensity, fastSwimIntensity * 1.2f);
        }
        else if (isAvoiding)
        {
            targetSwimSpeed += 0.4f;
            targetSwimIntensity += 0.3f;
        }
        
        targetSwimSpeed = Mathf.Clamp(targetSwimSpeed, normalSwimSpeed * 0.8f, fastSwimSpeed * 1.2f);
        targetSwimIntensity = Mathf.Clamp(targetSwimIntensity, normalSwimIntensity * 0.7f, fastSwimIntensity * 1.3f);
        
        float smoothTime = isInEmergency ? animationSmoothTime * 0.5f : animationSmoothTime;
        
        smoothedSwimSpeed = Mathf.SmoothDamp(
            smoothedSwimSpeed, 
            targetSwimSpeed, 
            ref swimSpeedVelocity, 
            smoothTime
        );
        
        smoothedSwimIntensity = Mathf.SmoothDamp(
            smoothedSwimIntensity, 
            targetSwimIntensity, 
            ref swimIntensityVelocity, 
            smoothTime
        );
        
        if (fishRenderer != null && propertyBlock != null)
        {
            propertyBlock.SetFloat("_SwimSpeed", smoothedSwimSpeed);
            propertyBlock.SetFloat("_SwimIntensity", smoothedSwimIntensity);
            fishRenderer.SetPropertyBlock(propertyBlock);
        }
        
        if (fishMaterial != null)
        {
            fishMaterial.SetFloat("_SwimSpeed", smoothedSwimSpeed);
            fishMaterial.SetFloat("_SwimIntensity", smoothedSwimIntensity);
        }
    }

    public FishData GetFishData()
    {
        return new FishData
        {
            position = new float3(position.x, position.y, position.z),
            velocity = new float3(velocity.x, velocity.y, velocity.z),
            species = (int)species,
            fishSize = fishSize,
            targetPosition = target != null
                ? new float3(target.position.x, target.position.y, target.position.z)
                : float3.zero,
            hasTarget = target != null,
            lastAvoidanceDirection = new float3(
                lastKnownAvoidanceDirection.x,
                lastKnownAvoidanceDirection.y,
                lastKnownAvoidanceDirection.z),
            avoidanceMemoryTimer = avoidanceMemoryTimer,
            hasRecentAvoidanceMemory = hasRecentAvoidanceMemory,
            stableAvoidanceDirection = new float3(
                stableAvoidanceDirection.x,
                stableAvoidanceDirection.y,
                stableAvoidanceDirection.z),
            avoidanceDirectionTimer = avoidanceDirectionTimer,
            smoothedDesiredDirection = new float3(
                smoothedDesiredDirection.x,
                smoothedDesiredDirection.y,
                smoothedDesiredDirection.z),
            isAvoiding = isAvoiding,
            isInEmergency = isInEmergency
        };
    }

    public float GetCurrentSpeed() => velocity.magnitude;
    public float GetSwimSpeed() => smoothedSwimSpeed;
    public float GetSwimIntensity() => smoothedSwimIntensity;
    public bool IsAvoiding() => isAvoiding;
    public bool IsInEmergency() => isInEmergency;
    public Vector3 GetVelocity() => velocity;
    public FishSpecies GetSpecies() => species;
}