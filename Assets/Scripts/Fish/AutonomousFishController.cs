/*using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public enum FishSpecies
{
    Tuna,
    GoldenTrevally,
    ClownFish,
    YellowtailSnapper,
    Angelfish,
    Grouper,
    Shark
}

public class AutonomousFishController : MonoBehaviour
{
    [Header("Fish Species")]
    public FishSpecies species;
    public float fishSize = 1f;
    
    [Header("Target Following")]
    [HideInInspector] // Hide since it will be set by spawner
    public Transform target;
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
    public bool showDebugGUI = true;
    public bool showDebugGizmos = true;
    public bool showBoidsDebug = false;
    
    // Private variables
    private Vector3 velocity;
    private Vector3 desiredDirection;
    private Vector3 stableAvoidanceDirection;
    private float avoidanceDirectionTimer;
    private float currentAcceleration;
    private float angularAcceleration;
    private Material fishMaterial;
    private float movementIntensity;
    private float angularIntensity;
    private float smoothedSwimIntensity;
    private float smoothedSwimSpeed;
    private float swimIntensityVelocity;
    private float swimSpeedVelocity;
    private bool isAvoiding = false;
    private bool isInEmergency = false;
    
    // Boids variables
    private List<AutonomousFishController> neighbors = new List<AutonomousFishController>();
    private Vector3 separationForce;
    private Vector3 alignmentForce;
    private Vector3 cohesionForce;
    private static List<AutonomousFishController> allFish = new List<AutonomousFishController>();
    
    void Start()
    {
        fishMaterial = GetComponent<Renderer>().material;
        velocity = Vector3.zero;
        smoothedSwimSpeed = normalSwimSpeed;
        smoothedSwimIntensity = normalSwimIntensity;
        stableAvoidanceDirection = Vector3.zero;
        
        // Register this fish
        if (!allFish.Contains(this))
            allFish.Add(this);
            
        // No longer creating individual targets - will be set by spawner
    }
    
    void OnDestroy()
    {
        // Unregister this fish
        if (allFish.Contains(this))
            allFish.Remove(this);
    }
    
    void Update()
    {
        FindNeighbors();
        UpdateDesiredDirection();
        UpdateMovement();
        UpdateRotation();
        UpdateSwimmingAnimation();
    }
    
    // Set target from external source (spawner)
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    void FindNeighbors()
    {
        neighbors.Clear();
        
        for (int i = 0; i < allFish.Count && neighbors.Count < maxNeighbors; i++)
        {
            var otherFish = allFish[i];
            if (otherFish == this || otherFish == null) continue;
            
            float distance = Vector3.Distance(transform.position, otherFish.transform.position);
            if (distance <= neighborRadius)
            {
                // Check species compatibility
                if (onlySchoolWithSameSpecies && otherFish.species != species)
                {
                    // Only add different species if we should avoid larger ones
                    if (avoidLargerSpecies && otherFish.fishSize > fishSize)
                    {
                        neighbors.Add(otherFish);
                    }
                    continue;
                }
                
                neighbors.Add(otherFish);
            }
        }
    }
    
    Vector3 CalculateSeparation()
    {
        Vector3 separation = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            float distance = Vector3.Distance(transform.position, neighbor.transform.position);
            
            // Use different separation radius for different species
            float effectiveSeparationRadius = separationRadius;
            if (neighbor.species != species && neighbor.fishSize > fishSize)
            {
                effectiveSeparationRadius *= 1.5f; // Avoid larger species more
            }
            
            if (distance < effectiveSeparationRadius && distance > 0)
            {
                Vector3 diff = (transform.position - neighbor.transform.position).normalized;
                diff /= distance; // Weight by distance (closer = stronger)
                separation += diff;
                count++;
            }
        }
        
        if (count > 0)
        {
            separation /= count;
            separation = separation.normalized;
        }
        
        return separation;
    }
    
    Vector3 CalculateAlignment()
    {
        Vector3 alignment = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            // Only align with same species
            if (neighbor.species == species)
            {
                alignment += neighbor.velocity.normalized;
                count++;
            }
        }
        
        if (count > 0)
        {
            alignment /= count;
            alignment = alignment.normalized;
        }
        
        return alignment;
    }
    
    Vector3 CalculateCohesion()
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            // Only cohere with same species
            if (neighbor.species == species)
            {
                center += neighbor.transform.position;
                count++;
            }
        }
        
        if (count > 0)
        {
            center /= count;
            Vector3 cohesion = (center - transform.position).normalized;
            return cohesion;
        }
        
        return Vector3.zero;
    }
    
    void UpdateDesiredDirection()
    {
        // Calculate boids forces
        separationForce = CalculateSeparation();
        alignmentForce = CalculateAlignment();
        cohesionForce = CalculateCohesion();
        
        // Calculate target direction
        Vector3 targetDirection = Vector3.zero;
        if (target != null)
        {
            targetDirection = (target.position - transform.position).normalized;
        }
        
        // Calculate obstacle avoidance
        Vector3 avoidanceDirection = CalculateObstacleAvoidance();
        
        // Combine all forces
        Vector3 combinedDirection = Vector3.zero;
        
        // Obstacle avoidance has highest priority
        if (avoidanceDirection != Vector3.zero)
        {
            isAvoiding = true;
            
            if (stableAvoidanceDirection == Vector3.zero || avoidanceDirectionTimer <= 0f)
            {
                stableAvoidanceDirection = avoidanceDirection;
                avoidanceDirectionTimer = isInEmergency ? 0.3f : 0.8f;
            }
            
            avoidanceDirectionTimer -= Time.deltaTime;
            
            // In emergency, prioritize obstacle avoidance
            if (isInEmergency)
            {
                combinedDirection = stableAvoidanceDirection;
                currentAcceleration = boostAcceleration * 1.5f;
            }
            else
            {
                // Blend avoidance with boids behavior
                combinedDirection += stableAvoidanceDirection * 3f; // High weight for avoidance
                combinedDirection += separationForce * separationWeight;
                combinedDirection += targetDirection * (targetWeight * 0.5f); // Reduced target weight when avoiding
                currentAcceleration = boostAcceleration;
            }
        }
        else
        {
            isAvoiding = false;
            isInEmergency = false;
            stableAvoidanceDirection = Vector3.zero;
            avoidanceDirectionTimer = 0f;
            
            // Normal boids behavior
            combinedDirection += separationForce * separationWeight;
            combinedDirection += alignmentForce * alignmentWeight;
            combinedDirection += cohesionForce * cohesionWeight;
            combinedDirection += targetDirection * targetWeight;
            currentAcceleration = baseAcceleration;
            
            // Boost acceleration if separation is strong (fish trying to avoid collision)
            if (separationForce.magnitude > 0.5f)
            {
                currentAcceleration = Mathf.Lerp(baseAcceleration, boostAcceleration, separationForce.magnitude);
            }
        }
        
        desiredDirection = combinedDirection.normalized;
    }
    
    Vector3 CalculateObstacleAvoidance()
    {
        Vector3 currentForward = velocity.magnitude > 0.1f ? velocity.normalized : transform.forward;
        Vector3 bestAvoidanceDirection = Vector3.zero;
        float closestDistance = float.MaxValue;
        isInEmergency = false;
        
        // Check center ray first (most important)
        RaycastHit hit;
        if (Physics.Raycast(transform.position, currentForward, out hit, lookAheadDistance, obstacleLayer))
        {
            closestDistance = hit.distance;
            bestAvoidanceDirection = CalculateSimpleAvoidanceDirection(hit);
            
            if (hit.distance < emergencyDistance)
            {
                isInEmergency = true;
            }
        }
        
        // Check side rays only if we need more information
        if (bestAvoidanceDirection != Vector3.zero)
        {
            Vector3 leftDirection = Quaternion.AngleAxis(-raySpread / 2f, Vector3.up) * currentForward;
            Vector3 rightDirection = Quaternion.AngleAxis(raySpread / 2f, Vector3.up) * currentForward;
            
            bool leftBlocked = Physics.Raycast(transform.position, leftDirection, lookAheadDistance, obstacleLayer);
            bool rightBlocked = Physics.Raycast(transform.position, rightDirection, lookAheadDistance, obstacleLayer);
            
            if (!leftBlocked && rightBlocked)
            {
                bestAvoidanceDirection = Vector3.Lerp(bestAvoidanceDirection, -transform.right, 0.5f);
            }
            else if (leftBlocked && !rightBlocked)
            {
                bestAvoidanceDirection = Vector3.Lerp(bestAvoidanceDirection, transform.right, 0.5f);
            }
        }
        
        return bestAvoidanceDirection.normalized;
    }
    
    Vector3 CalculateSimpleAvoidanceDirection(RaycastHit hit)
    {
        Vector3 hitToFish = (transform.position - hit.point).normalized;
        Vector3 surfaceNormal = hit.normal;
        
        Vector3 avoidDirection = (hitToFish * 0.7f + surfaceNormal * 0.3f).normalized;
        
        Vector3 obstacleToFish = (transform.position - hit.collider.transform.position);
        obstacleToFish.y = 0;
        
        if (obstacleToFish.magnitude > 0.1f)
        {
            avoidDirection = Vector3.Lerp(avoidDirection, obstacleToFish.normalized, 0.3f);
        }
        
        return avoidDirection.normalized;
    }
    
    void UpdateMovement()
    {
        velocity -= velocity * waterFriction * Time.deltaTime;
        
        if (desiredDirection != Vector3.zero)
        {
            velocity += desiredDirection * currentAcceleration * Time.deltaTime;
        }
        
        float angularContribution = angularAcceleration * angularInfluenceOnMovement;
        if (isAvoiding)
        {
            angularContribution *= 0.5f;
        }
        velocity += transform.forward * angularContribution * Time.deltaTime;
        
        float currentMaxSpeed = isInEmergency ? maxSpeed * 1.3f : maxSpeed;
        if (velocity.magnitude > currentMaxSpeed)
        {
            velocity = velocity.normalized * currentMaxSpeed;
        }
        
        transform.position += velocity * Time.deltaTime;
        movementIntensity = currentAcceleration / boostAcceleration;
    }
    
    void UpdateRotation()
    {
        if (velocity.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
            float angleDifference = Quaternion.Angle(transform.rotation, targetRotation);
            
            float currentTurnSpeed = turnSpeed;
            if (isAvoiding)
            {
                currentTurnSpeed *= isInEmergency ? 2f : 1.5f;
            }
            
            angularAcceleration = angleDifference * currentTurnSpeed * Time.deltaTime;
            
            transform.rotation = Quaternion.Slerp(
                transform.rotation, 
                targetRotation, 
                currentTurnSpeed * Time.deltaTime
            );
            
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
        
        if (fishMaterial != null)
        {
            fishMaterial.SetFloat("_SwimSpeed", smoothedSwimSpeed);
            fishMaterial.SetFloat("_SwimIntensity", smoothedSwimIntensity);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;
        
        // Neighbor radius
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, neighborRadius);
        
        // Separation radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, separationRadius);
        
        // Look ahead distance
        Gizmos.color = Color.blue;
        Vector3 forward = velocity.magnitude > 0.1f ? velocity.normalized : transform.forward;
        Gizmos.DrawWireSphere(transform.position + forward * lookAheadDistance, 0.5f);
        
        // Emergency distance
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + forward * emergencyDistance, 0.3f);
        
        // Boids forces visualization
        if (showBoidsDebug && Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, separationForce * 2f);
            
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, alignmentForce * 2f);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, cohesionForce * 2f);
            
            // Draw connections to neighbors
            Gizmos.color = Color.white;
            foreach (var neighbor in neighbors)
            {
                if (neighbor != null)
                {
                    Color lineColor = neighbor.species == species ? Color.green : Color.magenta;
                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(transform.position, neighbor.transform.position);
                }
            }
        }
        
        // Target connection
        if (target != null)
        {
            Gizmos.color = isInEmergency ? Color.red : (isAvoiding ? Color.magenta : Color.green);
            Gizmos.DrawLine(transform.position, target.position);
        }
    }
    
    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 400, 280));
        GUILayout.Box("Fish Debug Info");
        GUILayout.Label($"Species: {species}");
        GUILayout.Label($"Size: {fishSize:F1}");
        GUILayout.Label($"Neighbors: {neighbors.Count}");
        GUILayout.Label($"Swim Speed: {smoothedSwimSpeed:F2}");
        GUILayout.Label($"Swim Intensity: {smoothedSwimIntensity:F2}");
        GUILayout.Label($"Current Speed: {velocity.magnitude:F2} / {maxSpeed:F2}");
        GUILayout.Label($"Is Avoiding: {isAvoiding}");
        GUILayout.Label($"Is Emergency: {isInEmergency}");
        GUILayout.Label($"Has Target: {target != null}");
        
        // Boids forces
        GUILayout.Label($"Separation: {separationForce.magnitude:F2}");
        GUILayout.Label($"Alignment: {alignmentForce.magnitude:F2}");
        GUILayout.Label($"Cohesion: {cohesionForce.magnitude:F2}");
        
        if (target != null)
        {
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            GUILayout.Label($"Distance to Target: {distanceToTarget:F2}");
        }
        
        GUILayout.EndArea();
    }
    
    // Public getters
    public float GetCurrentSpeed() => velocity.magnitude;
    public float GetSwimSpeed() => smoothedSwimSpeed;
    public float GetSwimIntensity() => smoothedSwimIntensity;
    public bool IsAvoiding() => isAvoiding;
    public bool IsInEmergency() => isInEmergency;
    public Vector3 GetVelocity() => velocity;
    public int GetNeighborCount() => neighbors.Count;
    public FishSpecies GetSpecies() => species;
}*/

/*
public class AutonomousFishController : MonoBehaviour
{
    [Header("Fish Species")]
    public FishSpecies species;
    public float fishSize = 1f;
    
    [Header("Target Following")]
    [HideInInspector]
    public Transform target;
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
    
    [Header("Animation Tuning - Shader Graph")]
    [Space]
    [Header("Swim Speed Mapping")]
    public float normalSwimSpeed = 2.0f;
    public float fastSwimSpeed = 4.0f;
    [Space]
    [Header("Swim Intensity Mapping")]  
    public float normalSwimIntensity = 5.0f;
    public float fastSwimIntensity = 10.0f;
    [Space]
    [Header("Swimming Parameters")]
    public float sideAmplitude = 0.01f;
    public float yawAmplitude = 0.01f;
    public float flagYawAmplitude = 0.01f;
    public float pivotOffset = 0.01f;
    [Space]
    [Header("Turn Animation")]
    public float turnSpeedBoost = 0.3f;
    public float turnIntensityBoost = 0.2f;
    public float animationSmoothTime = 0.25f;
    public float angularInfluenceOnMovement = 0.3f;
    
    [Header("Debug")]
    public bool showDebugGUI = true;
    public bool showDebugGizmos = true;
    public bool showBoidsDebug = false;
    
    // Private variables
    private Vector3 velocity;
    private Vector3 desiredDirection;
    private Vector3 stableAvoidanceDirection;
    private float avoidanceDirectionTimer;
    private float currentAcceleration;
    private float angularAcceleration;
    private Material fishMaterial;
    private float movementIntensity;
    private float angularIntensity;
    private float smoothedSwimIntensity;
    private float smoothedSwimSpeed;
    private float swimIntensityVelocity;
    private float swimSpeedVelocity;
    private bool isAvoiding = false;
    private bool isInEmergency = false;
    
    // Boids variables
    private List<AutonomousFishController> neighbors = new List<AutonomousFishController>();
    private Vector3 separationForce;
    private Vector3 alignmentForce;
    private Vector3 cohesionForce;
    private static List<AutonomousFishController> allFish = new List<AutonomousFishController>();
    
    void Start()
    {
        fishMaterial = GetComponent<Renderer>().material;
        velocity = Vector3.zero;
        smoothedSwimSpeed = normalSwimSpeed;
        smoothedSwimIntensity = normalSwimIntensity;
        stableAvoidanceDirection = Vector3.zero;
        
        // Register this fish
        if (!allFish.Contains(this))
            allFish.Add(this);
    }
    
    void OnDestroy()
    {
        // Unregister this fish
        if (allFish.Contains(this))
            allFish.Remove(this);
    }
    
    void Update()
    {
        FindNeighbors();
        UpdateDesiredDirection();
        UpdateMovement();
        UpdateRotation();
        UpdateSwimmingAnimation();
    }
    
    // Set target from external source (spawner)
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    void FindNeighbors()
    {
        neighbors.Clear();
        
        for (int i = 0; i < allFish.Count && neighbors.Count < maxNeighbors; i++)
        {
            var otherFish = allFish[i];
            if (otherFish == this || otherFish == null) continue;
            
            float distance = Vector3.Distance(transform.position, otherFish.transform.position);
            if (distance <= neighborRadius)
            {
                // Check species compatibility
                if (onlySchoolWithSameSpecies && otherFish.species != species)
                {
                    // Only add different species if we should avoid larger ones
                    if (avoidLargerSpecies && otherFish.fishSize > fishSize)
                    {
                        neighbors.Add(otherFish);
                    }
                    continue;
                }
                
                neighbors.Add(otherFish);
            }
        }
    }
    
    Vector3 CalculateSeparation()
    {
        Vector3 separation = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            float distance = Vector3.Distance(transform.position, neighbor.transform.position);
            
            // Use different separation radius for different species
            float effectiveSeparationRadius = separationRadius;
            if (neighbor.species != species && neighbor.fishSize > fishSize)
            {
                effectiveSeparationRadius *= 1.5f; // Avoid larger species more
            }
            
            if (distance < effectiveSeparationRadius && distance > 0)
            {
                Vector3 diff = (transform.position - neighbor.transform.position).normalized;
                diff /= distance; // Weight by distance (closer = stronger)
                separation += diff;
                count++;
            }
        }
        
        if (count > 0)
        {
            separation /= count;
            separation = separation.normalized;
        }
        
        return separation;
    }
    
    Vector3 CalculateAlignment()
    {
        Vector3 alignment = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            // Only align with same species
            if (neighbor.species == species)
            {
                alignment += neighbor.velocity.normalized;
                count++;
            }
        }
        
        if (count > 0)
        {
            alignment /= count;
            alignment = alignment.normalized;
        }
        
        return alignment;
    }
    
    Vector3 CalculateCohesion()
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            // Only cohere with same species
            if (neighbor.species == species)
            {
                center += neighbor.transform.position;
                count++;
            }
        }
        
        if (count > 0)
        {
            center /= count;
            Vector3 cohesion = (center - transform.position).normalized;
            return cohesion;
        }
        
        return Vector3.zero;
    }
    
    void UpdateDesiredDirection()
    {
        // Calculate boids forces
        separationForce = CalculateSeparation();
        alignmentForce = CalculateAlignment();
        cohesionForce = CalculateCohesion();
        
        // Calculate target direction
        Vector3 targetDirection = Vector3.zero;
        if (target != null)
        {
            targetDirection = (target.position - transform.position).normalized;
        }
        
        // Calculate obstacle avoidance
        Vector3 avoidanceDirection = CalculateObstacleAvoidance();
        
        // Combine all forces
        Vector3 combinedDirection = Vector3.zero;
        
        // Obstacle avoidance has highest priority
        if (avoidanceDirection != Vector3.zero)
        {
            isAvoiding = true;
            
            if (stableAvoidanceDirection == Vector3.zero || avoidanceDirectionTimer <= 0f)
            {
                stableAvoidanceDirection = avoidanceDirection;
                avoidanceDirectionTimer = isInEmergency ? 0.3f : 0.8f;
            }
            
            avoidanceDirectionTimer -= Time.deltaTime;
            
            // In emergency, prioritize obstacle avoidance
            if (isInEmergency)
            {
                combinedDirection = stableAvoidanceDirection;
                currentAcceleration = boostAcceleration * 1.5f;
            }
            else
            {
                // Blend avoidance with boids behavior
                combinedDirection += stableAvoidanceDirection * 3f; // High weight for avoidance
                combinedDirection += separationForce * separationWeight;
                combinedDirection += targetDirection * (targetWeight * 0.5f); // Reduced target weight when avoiding
                currentAcceleration = boostAcceleration;
            }
        }
        else
        {
            isAvoiding = false;
            isInEmergency = false;
            stableAvoidanceDirection = Vector3.zero;
            avoidanceDirectionTimer = 0f;
            
            // Normal boids behavior
            combinedDirection += separationForce * separationWeight;
            combinedDirection += alignmentForce * alignmentWeight;
            combinedDirection += cohesionForce * cohesionWeight;
            combinedDirection += targetDirection * targetWeight;
            currentAcceleration = baseAcceleration;
            
            // Boost acceleration if separation is strong (fish trying to avoid collision)
            if (separationForce.magnitude > 0.5f)
            {
                currentAcceleration = Mathf.Lerp(baseAcceleration, boostAcceleration, separationForce.magnitude);
            }
        }
        
        // Smooth the desired direction change to prevent jitter
        Vector3 newDesiredDirection = combinedDirection.normalized;
        if (desiredDirection != Vector3.zero)
        {
            // Smooth transition between direction changes
            float smoothingSpeed = isInEmergency ? 8f : 4f;
            desiredDirection = Vector3.Slerp(desiredDirection, newDesiredDirection, Time.deltaTime * smoothingSpeed);
        }
        else
        {
            desiredDirection = newDesiredDirection;
        }
    }
    
    Vector3 CalculateObstacleAvoidance()
    {
        Vector3 currentForward = velocity.magnitude > 0.1f ? velocity.normalized : transform.forward;
        Vector3 bestAvoidanceDirection = Vector3.zero;
        float closestDistance = float.MaxValue;
        isInEmergency = false;
        
        // Check center ray first (most important)
        RaycastHit hit;
        if (Physics.Raycast(transform.position, currentForward, out hit, lookAheadDistance, obstacleLayer))
        {
            closestDistance = hit.distance;
            bestAvoidanceDirection = CalculateSimpleAvoidanceDirection(hit);
            
            if (hit.distance < emergencyDistance)
            {
                isInEmergency = true;
            }
        }
        
        // Check side rays only if we need more information
        if (bestAvoidanceDirection != Vector3.zero)
        {
            Vector3 leftDirection = Quaternion.AngleAxis(-raySpread / 2f, Vector3.up) * currentForward;
            Vector3 rightDirection = Quaternion.AngleAxis(raySpread / 2f, Vector3.up) * currentForward;
            
            bool leftBlocked = Physics.Raycast(transform.position, leftDirection, lookAheadDistance, obstacleLayer);
            bool rightBlocked = Physics.Raycast(transform.position, rightDirection, lookAheadDistance, obstacleLayer);
            
            if (!leftBlocked && rightBlocked)
            {
                bestAvoidanceDirection = Vector3.Lerp(bestAvoidanceDirection, -transform.right, 0.5f);
            }
            else if (leftBlocked && !rightBlocked)
            {
                bestAvoidanceDirection = Vector3.Lerp(bestAvoidanceDirection, transform.right, 0.5f);
            }
        }
        
        return bestAvoidanceDirection.normalized;
    }
    
    Vector3 CalculateSimpleAvoidanceDirection(RaycastHit hit)
    {
        Vector3 hitToFish = (transform.position - hit.point).normalized;
        Vector3 surfaceNormal = hit.normal;
        
        Vector3 avoidDirection = (hitToFish * 0.7f + surfaceNormal * 0.3f).normalized;
        
        Vector3 obstacleToFish = (transform.position - hit.collider.transform.position);
        obstacleToFish.y = 0;
        
        if (obstacleToFish.magnitude > 0.1f)
        {
            avoidDirection = Vector3.Lerp(avoidDirection, obstacleToFish.normalized, 0.3f);
        }
        
        return avoidDirection.normalized;
    }
    
    void UpdateMovement()
    {
        velocity -= velocity * waterFriction * Time.deltaTime;
        
        if (desiredDirection != Vector3.zero)
        {
            velocity += desiredDirection * currentAcceleration * Time.deltaTime;
        }
        
        float angularContribution = angularAcceleration * angularInfluenceOnMovement;
        if (isAvoiding)
        {
            angularContribution *= 0.5f;
        }
        velocity += transform.forward * angularContribution * Time.deltaTime;
        
        float currentMaxSpeed = isInEmergency ? maxSpeed * 1.3f : maxSpeed;
        if (velocity.magnitude > currentMaxSpeed)
        {
            velocity = velocity.normalized * currentMaxSpeed;
        }
        
        transform.position += velocity * Time.deltaTime;
        movementIntensity = currentAcceleration / boostAcceleration;
    }
    
    void UpdateRotation()
    {
        if (velocity.magnitude > 0.1f)
        {
            // Smooth the desired rotation direction to prevent jitter
            Vector3 smoothedVelocity = Vector3.Slerp(transform.forward, velocity.normalized, Time.deltaTime * 3f);
            Quaternion targetRotation = Quaternion.LookRotation(smoothedVelocity);
            
            float angleDifference = Quaternion.Angle(transform.rotation, targetRotation);
            
            // Much more conservative turn speed - no aggressive multipliers
            float currentTurnSpeed = turnSpeed;
            if (isAvoiding)
            {
                currentTurnSpeed *= 1.2f; // Reduced from 1.5f-2f
            }
            
            // Smooth angular acceleration instead of direct angle calculation
            float targetAngularAccel = angleDifference * 0.1f; // Much smaller multiplier
            angularAcceleration = Mathf.Lerp(angularAcceleration, targetAngularAccel, Time.deltaTime * 5f);
            
            // Use RotateTowards instead of Slerp for more controlled rotation
            float maxRotationStep = currentTurnSpeed * Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, 
                targetRotation, 
                maxRotationStep
            );
            
            angularIntensity = Mathf.Clamp01(angleDifference / 90f);
        }
        else
        {
            // Smooth angular acceleration to zero
            angularAcceleration = Mathf.Lerp(angularAcceleration, 0f, Time.deltaTime * 5f);
            angularIntensity = Mathf.Lerp(angularIntensity, 0f, Time.deltaTime * 3f);
        }
    }
    
    void UpdateSwimmingAnimation()
    {
        // Simple calculation based on current velocity magnitude
        float currentSpeedNormalized = Mathf.Clamp01(velocity.magnitude / maxSpeed);
        
        // Calculate target values based on speed only
        float targetSwimSpeed = Mathf.Lerp(normalSwimSpeed, fastSwimSpeed, currentSpeedNormalized);
        float targetSwimIntensity = Mathf.Lerp(normalSwimIntensity, fastSwimIntensity, currentSpeedNormalized);
        
        // Smooth transitions
        smoothedSwimSpeed = Mathf.SmoothDamp(
            smoothedSwimSpeed, 
            targetSwimSpeed, 
            ref swimSpeedVelocity, 
            animationSmoothTime
        );
        
        smoothedSwimIntensity = Mathf.SmoothDamp(
            smoothedSwimIntensity, 
            targetSwimIntensity, 
            ref swimIntensityVelocity, 
            animationSmoothTime
        );
        
        // Update Shader Graph properties
        if (fishMaterial != null)
        {
            fishMaterial.SetFloat("_SwimSpeed", smoothedSwimSpeed);
            fishMaterial.SetFloat("_SwimIntensity", smoothedSwimIntensity);
            fishMaterial.SetFloat("_SideAmplitude", sideAmplitude);
            fishMaterial.SetFloat("_YawAmplitude", yawAmplitude);
            fishMaterial.SetFloat("_FlagYawAmplitude", flagYawAmplitude);
            fishMaterial.SetFloat("_PivotOffset", pivotOffset);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;
        
        // Neighbor radius
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, neighborRadius);
        
        // Separation radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, separationRadius);
        
        // Look ahead distance
        Gizmos.color = Color.blue;
        Vector3 forward = velocity.magnitude > 0.1f ? velocity.normalized : transform.forward;
        Gizmos.DrawWireSphere(transform.position + forward * lookAheadDistance, 0.5f);
        
        // Emergency distance
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + forward * emergencyDistance, 0.3f);
        
        // Boids forces visualization
        if (showBoidsDebug && Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, separationForce * 2f);
            
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, alignmentForce * 2f);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, cohesionForce * 2f);
            
            // Draw connections to neighbors
            Gizmos.color = Color.white;
            foreach (var neighbor in neighbors)
            {
                if (neighbor != null)
                {
                    Color lineColor = neighbor.species == species ? Color.green : Color.magenta;
                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(transform.position, neighbor.transform.position);
                }
            }
        }
        
        // Target connection
        if (target != null)
        {
            Gizmos.color = isInEmergency ? Color.red : (isAvoiding ? Color.magenta : Color.green);
            Gizmos.DrawLine(transform.position, target.position);
        }
    }
    
    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 400, 320));
        GUILayout.Box("Fish Debug Info");
        GUILayout.Label($"Species: {species}");
        GUILayout.Label($"Size: {fishSize:F1}");
        GUILayout.Label($"Neighbors: {neighbors.Count}");
        GUILayout.Label($"Swim Speed: {smoothedSwimSpeed:F2}");
        GUILayout.Label($"Swim Intensity: {smoothedSwimIntensity:F2}");
        GUILayout.Label($"Side Amplitude: {sideAmplitude:F3}");
        GUILayout.Label($"Yaw Amplitude: {yawAmplitude:F3}");
        GUILayout.Label($"Flag Amplitude: {flagYawAmplitude:F3}");
        GUILayout.Label($"Current Speed: {velocity.magnitude:F2} / {maxSpeed:F2}");
        GUILayout.Label($"Is Avoiding: {isAvoiding}");
        GUILayout.Label($"Is Emergency: {isInEmergency}");
        GUILayout.Label($"Has Target: {target != null}");
        
        // Boids forces
        GUILayout.Label($"Separation: {separationForce.magnitude:F2}");
        GUILayout.Label($"Alignment: {alignmentForce.magnitude:F2}");
        GUILayout.Label($"Cohesion: {cohesionForce.magnitude:F2}");
        
        if (target != null)
        {
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            GUILayout.Label($"Distance to Target: {distanceToTarget:F2}");
        }
        
        GUILayout.EndArea();
    }
    
    // Public getters
    public float GetCurrentSpeed() => velocity.magnitude;
    public float GetSwimSpeed() => smoothedSwimSpeed;
    public float GetSwimIntensity() => smoothedSwimIntensity;
    public bool IsAvoiding() => isAvoiding;
    public bool IsInEmergency() => isInEmergency;
    public Vector3 GetVelocity() => velocity;
    public int GetNeighborCount() => neighbors.Count;
    public FishSpecies GetSpecies() => species;
}*/
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public enum FishSpecies
{
    Tuna,
    GoldenTrevally,
    ClownFish,
    YellowtailSnapper,
    Angelfish,
    Grouper,
    Shark
}

public class AutonomousFishController : MonoBehaviour
{
    [Header("Fish Species")]
    public FishSpecies species;
    public float fishSize = 1f;
    
    [Header("Target Following")]
    [HideInInspector]
    public Transform target;
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
    public float avoidanceMemoryTime = 2f; // How long to remember avoidance direction
    public float avoidanceBlendSpeed = 2f; // How fast to blend avoidance directions
    public float clearPathCheckDistance = 8f; // Distance to check if path is clear
    
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
    
    [Header("Performance Settings")]
    public float neighborUpdateInterval = 0.1f;
    public float obstacleCheckInterval = 0.05f;
    
    [Header("Debug")]
    public bool showDebugGUI = true;
    public bool showDebugGizmos = true;
    public bool showBoidsDebug = false;
    
    // Private variables
    private Vector3 velocity;
    private Vector3 desiredDirection;
    private Vector3 smoothedDesiredDirection; // NEW: Smooth the desired direction
    
    // Enhanced avoidance system
    private Vector3 stableAvoidanceDirection;
    private float avoidanceDirectionTimer;
    private float avoidanceMemoryTimer; // NEW: Remember avoidance even after obstacle is gone
    private Vector3 lastKnownAvoidanceDirection; // NEW: Remember last avoidance direction
    private bool hasRecentAvoidanceMemory; // NEW: Flag for recent avoidance
    
    private float currentAcceleration;
    private float angularAcceleration;
    
    // Optimized rendering
    private MaterialPropertyBlock propertyBlock;
    private Renderer fishRenderer;
    private Material fishMaterial;
    
    // Animation state
    private float movementIntensity;
    private float angularIntensity;
    private float smoothedSwimIntensity;
    private float smoothedSwimSpeed;
    private float swimIntensityVelocity;
    private float swimSpeedVelocity;
    
    // Avoidance state
    private bool isAvoiding = false;
    private bool isInEmergency = false;
    
    // Performance timers
    private float neighborUpdateTimer;
    private float obstacleCheckTimer;
    private Vector3 cachedAvoidanceDirection;
    
    // Boids variables
    private List<AutonomousFishController> neighbors = new List<AutonomousFishController>();
    private Vector3 separationForce;
    private Vector3 alignmentForce;
    private Vector3 cohesionForce;
    private static List<AutonomousFishController> allFish = new List<AutonomousFishController>();
    
    void Start()
    {
        // Initialize rendering components
        fishRenderer = GetComponent<Renderer>();
        if (fishRenderer != null)
        {
            propertyBlock = new MaterialPropertyBlock();
            fishMaterial = fishRenderer.material;
        }
        
        // Initialize state
        velocity = Vector3.zero;
        smoothedSwimSpeed = normalSwimSpeed;
        smoothedSwimIntensity = normalSwimIntensity;
        stableAvoidanceDirection = Vector3.zero;
        smoothedDesiredDirection = Vector3.zero;
        lastKnownAvoidanceDirection = Vector3.zero;
        cachedAvoidanceDirection = Vector3.zero;
        hasRecentAvoidanceMemory = false;
        
        // Stagger timers to distribute load
        neighborUpdateTimer = Random.Range(0f, neighborUpdateInterval);
        obstacleCheckTimer = Random.Range(0f, obstacleCheckInterval);
    }
    
    void OnEnable()
    {
        if (!allFish.Contains(this))
            allFish.Add(this);
    }
    
    void OnDisable()
    {
        if (allFish.Contains(this))
            allFish.Remove(this);
    }
    
    void Update()
    {
        // Update neighbors less frequently
        neighborUpdateTimer += Time.deltaTime;
        if (neighborUpdateTimer >= neighborUpdateInterval)
        {
            FindNeighbors();
            neighborUpdateTimer = 0f;
        }
        
        // Update avoidance memory timer
        if (hasRecentAvoidanceMemory)
        {
            avoidanceMemoryTimer -= Time.deltaTime;
            if (avoidanceMemoryTimer <= 0f)
            {
                hasRecentAvoidanceMemory = false;
            }
        }
        
        UpdateDesiredDirection();
        UpdateMovement();
        UpdateRotation();
        UpdateSwimmingAnimation();
    }
    
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    void FindNeighbors()
    {
        neighbors.Clear();
        
        for (int i = 0; i < allFish.Count && neighbors.Count < maxNeighbors; i++)
        {
            var otherFish = allFish[i];
            if (otherFish == this || otherFish == null) continue;
            
            float distance = Vector3.Distance(transform.position, otherFish.transform.position);
            if (distance <= neighborRadius)
            {
                if (onlySchoolWithSameSpecies && otherFish.species != species)
                {
                    if (avoidLargerSpecies && otherFish.fishSize > fishSize)
                    {
                        neighbors.Add(otherFish);
                    }
                    continue;
                }
                
                neighbors.Add(otherFish);
            }
        }
    }
    
    Vector3 CalculateSeparation()
    {
        Vector3 separation = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            float distance = Vector3.Distance(transform.position, neighbor.transform.position);
            float effectiveSeparationRadius = separationRadius;
            
            if (neighbor.species != species && neighbor.fishSize > fishSize)
            {
                effectiveSeparationRadius *= 1.5f;
            }
            
            if (distance < effectiveSeparationRadius && distance > 0)
            {
                Vector3 diff = (transform.position - neighbor.transform.position).normalized;
                diff /= distance;
                separation += diff;
                count++;
            }
        }
        
        if (count > 0)
        {
            separation /= count;
            separation = separation.normalized;
        }
        
        return separation;
    }
    
    Vector3 CalculateAlignment()
    {
        Vector3 alignment = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            if (neighbor.species == species)
            {
                alignment += neighbor.velocity.normalized;
                count++;
            }
        }
        
        if (count > 0)
        {
            alignment /= count;
            alignment = alignment.normalized;
        }
        
        return alignment;
    }
    
    Vector3 CalculateCohesion()
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        
        foreach (var neighbor in neighbors)
        {
            if (neighbor.species == species)
            {
                center += neighbor.transform.position;
                count++;
            }
        }
        
        if (count > 0)
        {
            center /= count;
            Vector3 cohesion = (center - transform.position).normalized;
            return cohesion;
        }
        
        return Vector3.zero;
    }
    
    void UpdateDesiredDirection()
    {
        // Calculate boids forces
        separationForce = CalculateSeparation();
        alignmentForce = CalculateAlignment();
        cohesionForce = CalculateCohesion();
        
        Vector3 targetDirection = Vector3.zero;
        if (target != null)
        {
            targetDirection = (target.position - transform.position).normalized;
        }
        
        // Get current avoidance direction
        Vector3 currentAvoidanceDirection = CalculateObstacleAvoidance();
        
        // Enhanced avoidance system
        Vector3 finalAvoidanceDirection = Vector3.zero;
        bool shouldAvoid = false;
        
        if (currentAvoidanceDirection != Vector3.zero)
        {
            // We have a current obstacle to avoid
            shouldAvoid = true;
            
            // Update stable avoidance direction
            if (stableAvoidanceDirection == Vector3.zero || avoidanceDirectionTimer <= 0f)
            {
                stableAvoidanceDirection = currentAvoidanceDirection;
                avoidanceDirectionTimer = isInEmergency ? 0.5f : 1.2f; // Longer stability
            }
            else
            {
                // Blend current with stable for smoother transitions
                stableAvoidanceDirection = Vector3.Slerp(
                    stableAvoidanceDirection, 
                    currentAvoidanceDirection, 
                    Time.deltaTime * avoidanceBlendSpeed
                );
            }
            
            avoidanceDirectionTimer -= Time.deltaTime;
            finalAvoidanceDirection = stableAvoidanceDirection;
            
            // Store in memory
            lastKnownAvoidanceDirection = finalAvoidanceDirection;
            hasRecentAvoidanceMemory = true;
            avoidanceMemoryTimer = avoidanceMemoryTime;
        }
        else if (hasRecentAvoidanceMemory)
        {
            // No immediate obstacle, but we have recent avoidance memory
            // Check if the path towards target is clear
            bool pathToTargetClear = IsPathClear(targetDirection);
            
            if (!pathToTargetClear)
            {
                // Path still not clear, continue with remembered avoidance
                shouldAvoid = true;
                finalAvoidanceDirection = lastKnownAvoidanceDirection;
                
                // Gradually reduce the memory influence
                float memoryStrength = avoidanceMemoryTimer / avoidanceMemoryTime;
                finalAvoidanceDirection *= memoryStrength;
            }
        }
        
        Vector3 combinedDirection = Vector3.zero;
        
        if (shouldAvoid)
        {
            isAvoiding = true;
            
            if (isInEmergency)
            {
                // Emergency: Only avoidance matters
                combinedDirection = finalAvoidanceDirection;
                currentAcceleration = boostAcceleration * 1.5f;
            }
            else
            {
                // Normal avoidance: Heavily weighted but allow some boids behavior
                combinedDirection += finalAvoidanceDirection * 4f; // Increased weight
                combinedDirection += separationForce * separationWeight;
                
                // Drastically reduce target influence when avoiding
                if (hasRecentAvoidanceMemory)
                {
                    combinedDirection += targetDirection * (targetWeight * 0.1f); // Much less influence
                }
                
                currentAcceleration = boostAcceleration;
            }
        }
        else
        {
            // No avoidance needed - normal boids behavior
            isAvoiding = false;
            isInEmergency = false;
            stableAvoidanceDirection = Vector3.zero;
            
            combinedDirection += separationForce * separationWeight;
            combinedDirection += alignmentForce * alignmentWeight;
            combinedDirection += cohesionForce * cohesionWeight;
            combinedDirection += targetDirection * targetWeight;
            currentAcceleration = baseAcceleration;
            
            if (separationForce.magnitude > 0.5f)
            {
                currentAcceleration = Mathf.Lerp(baseAcceleration, boostAcceleration, separationForce.magnitude);
            }
        }
        
        // Smooth the desired direction to prevent jitter
        Vector3 newDesiredDirection = combinedDirection.normalized;
        if (smoothedDesiredDirection == Vector3.zero)
        {
            smoothedDesiredDirection = newDesiredDirection;
        }
        else
        {
            float smoothingSpeed = isInEmergency ? 6f : 3f;
            smoothedDesiredDirection = Vector3.Slerp(
                smoothedDesiredDirection, 
                newDesiredDirection, 
                Time.deltaTime * smoothingSpeed
            );
        }
        
        desiredDirection = smoothedDesiredDirection;
    }
    
    // NEW: Check if path is clear in a given direction
    bool IsPathClear(Vector3 direction)
    {
        if (direction == Vector3.zero) return true;
        
        return !Physics.Raycast(
            transform.position, 
            direction, 
            clearPathCheckDistance, 
            obstacleLayer
        );
    }
    
    Vector3 CalculateObstacleAvoidance()
    {
        obstacleCheckTimer += Time.deltaTime;
        
        if (obstacleCheckTimer >= obstacleCheckInterval || isInEmergency)
        {
            obstacleCheckTimer = 0f;
            cachedAvoidanceDirection = PerformObstacleRaycast();
        }
        
        return cachedAvoidanceDirection;
    }
    
    Vector3 PerformObstacleRaycast()
    {
        Vector3 currentForward = velocity.magnitude > 0.1f ? velocity.normalized : transform.forward;
        Vector3 bestAvoidanceDirection = Vector3.zero;
        isInEmergency = false;
        
        // Check center ray
        RaycastHit hit;
        if (Physics.Raycast(transform.position, currentForward, out hit, lookAheadDistance, obstacleLayer))
        {
            bestAvoidanceDirection = CalculateSimpleAvoidanceDirection(hit);
            
            if (hit.distance < emergencyDistance)
            {
                isInEmergency = true;
            }
        }
        
        // Enhanced side ray checking
        if (bestAvoidanceDirection != Vector3.zero)
        {
            Vector3 leftDirection = Quaternion.AngleAxis(-raySpread / 2f, Vector3.up) * currentForward;
            Vector3 rightDirection = Quaternion.AngleAxis(raySpread / 2f, Vector3.up) * currentForward;
            
            RaycastHit leftHit, rightHit;
            bool leftBlocked = Physics.Raycast(transform.position, leftDirection, out leftHit, lookAheadDistance, obstacleLayer);
            bool rightBlocked = Physics.Raycast(transform.position, rightDirection, out rightHit, lookAheadDistance, obstacleLayer);
            
            // Choose the side with more clearance
            if (!leftBlocked && rightBlocked)
            {
                bestAvoidanceDirection = Vector3.Slerp(bestAvoidanceDirection, -transform.right, 0.6f);
            }
            else if (leftBlocked && !rightBlocked)
            {
                bestAvoidanceDirection = Vector3.Slerp(bestAvoidanceDirection, transform.right, 0.6f);
            }
            else if (leftBlocked && rightBlocked)
            {
                // Both sides blocked, choose the one with more distance
                if (leftHit.distance > rightHit.distance)
                {
                    bestAvoidanceDirection = Vector3.Slerp(bestAvoidanceDirection, -transform.right, 0.4f);
                }
                else
                {
                    bestAvoidanceDirection = Vector3.Slerp(bestAvoidanceDirection, transform.right, 0.4f);
                }
            }
        }
        
        return bestAvoidanceDirection.normalized;
    }
    
    Vector3 CalculateSimpleAvoidanceDirection(RaycastHit hit)
    {
        Vector3 hitToFish = (transform.position - hit.point).normalized;
        Vector3 surfaceNormal = hit.normal;
        
        // More aggressive avoidance calculation
        Vector3 avoidDirection = (hitToFish * 0.8f + surfaceNormal * 0.2f).normalized;
        
        // Add upward component if hit is below fish
        if (hit.point.y < transform.position.y)
        {
            avoidDirection += Vector3.up * 0.3f;
        }
        
        return avoidDirection.normalized;
    }
    
    void UpdateMovement()
    {
        velocity -= velocity * waterFriction * Time.deltaTime;
        
        if (desiredDirection != Vector3.zero)
        {
            velocity += desiredDirection * currentAcceleration * Time.deltaTime;
        }
        
        float angularContribution = angularAcceleration * angularInfluenceOnMovement;
        if (isAvoiding)
        {
            angularContribution *= 0.3f; // Reduced influence when avoiding
        }
        velocity += transform.forward * angularContribution * Time.deltaTime;
        
        float currentMaxSpeed = isInEmergency ? maxSpeed * 1.3f : maxSpeed;
        if (velocity.magnitude > currentMaxSpeed)
        {
            velocity = velocity.normalized * currentMaxSpeed;
        }
        
        transform.position += velocity * Time.deltaTime;
        movementIntensity = currentAcceleration / boostAcceleration;
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
    
    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;
        
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, neighborRadius);
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, separationRadius);
        
        Gizmos.color = Color.blue;
        Vector3 forward = velocity.magnitude > 0.1f ? velocity.normalized : transform.forward;
        Gizmos.DrawWireSphere(transform.position + forward * lookAheadDistance, 0.5f);
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + forward * emergencyDistance, 0.3f);
        
        // Show clear path check
        if (target != null)
        {
            Vector3 targetDir = (target.position - transform.position).normalized;
            Gizmos.color = IsPathClear(targetDir) ? Color.green : Color.red;
            Gizmos.DrawRay(transform.position, targetDir * clearPathCheckDistance);
        }
        
        // Show avoidance memory
        if (hasRecentAvoidanceMemory)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, lastKnownAvoidanceDirection * 3f);
        }
        
        if (showBoidsDebug && Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, separationForce * 2f);
            
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, alignmentForce * 2f);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, cohesionForce * 2f);
            
            Gizmos.color = Color.white;
            foreach (var neighbor in neighbors)
            {
                if (neighbor != null)
                {
                    Color lineColor = neighbor.species == species ? Color.green : Color.magenta;
                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(transform.position, neighbor.transform.position);
                }
            }
        }
        
        if (target != null)
        {
            Gizmos.color = isInEmergency ? Color.red : (isAvoiding ? Color.magenta : Color.green);
            Gizmos.DrawLine(transform.position, target.position);
        }
    }
    
    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 400, 320));
        GUILayout.Box("Fish Debug Info");
        GUILayout.Label($"Species: {species}");
        GUILayout.Label($"Size: {fishSize:F1}");
        GUILayout.Label($"Neighbors: {neighbors.Count}");
        GUILayout.Label($"Swim Speed: {smoothedSwimSpeed:F2}");
        GUILayout.Label($"Swim Intensity: {smoothedSwimIntensity:F2}");
        GUILayout.Label($"Current Speed: {velocity.magnitude:F2} / {maxSpeed:F2}");
        GUILayout.Label($"Is Avoiding: {isAvoiding}");
        GUILayout.Label($"Is Emergency: {isInEmergency}");
        GUILayout.Label($"Has Target: {target != null}");
        GUILayout.Label($"Has Avoidance Memory: {hasRecentAvoidanceMemory}");
        GUILayout.Label($"Memory Timer: {avoidanceMemoryTimer:F2}");
        
        if (target != null)
        {
            Vector3 targetDir = (target.position - transform.position).normalized;
            bool pathClear = IsPathClear(targetDir);
            GUILayout.Label($"Path to Target Clear: {pathClear}");
            
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            GUILayout.Label($"Distance to Target: {distanceToTarget:F2}");
        }
        
        GUILayout.Label($"Separation: {separationForce.magnitude:F2}");
        GUILayout.Label($"Alignment: {alignmentForce.magnitude:F2}");
        GUILayout.Label($"Cohesion: {cohesionForce.magnitude:F2}");
        
        GUILayout.EndArea();
    }
    
    // Public getters
    public float GetCurrentSpeed() => velocity.magnitude;
    public float GetSwimSpeed() => smoothedSwimSpeed;
    public float GetSwimIntensity() => smoothedSwimIntensity;
    public bool IsAvoiding() => isAvoiding;
    public bool IsInEmergency() => isInEmergency;
    public Vector3 GetVelocity() => velocity;
    public int GetNeighborCount() => neighbors.Count;
    public FishSpecies GetSpecies() => species;
}