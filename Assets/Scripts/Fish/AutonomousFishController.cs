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
    
    [Header("Performance Settings")]
    public float neighborUpdateInterval = 0.1f; // Update neighbors every 100ms
    public float obstacleCheckInterval = 0.05f; // Check obstacles every 50ms
    public float shaderUpdateThreshold = 0.1f; // Only update shader when change is significant
    
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
    
    // Optimized rendering
    private MaterialPropertyBlock propertyBlock;
    private Renderer fishRenderer;
    private Material fishMaterial; // Kept for backwards compatibility
    
    // Animation state
    private float movementIntensity;
    private float angularIntensity;
    private float smoothedSwimIntensity;
    private float smoothedSwimSpeed;
    private float swimIntensityVelocity;
    private float swimSpeedVelocity;
    private float lastSwimSpeed;
    private float lastSwimIntensity;
    
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
            fishMaterial = fishRenderer.material; // Backwards compatibility
        }
        
        // Initialize state
        velocity = Vector3.zero;
        smoothedSwimSpeed = normalSwimSpeed;
        smoothedSwimIntensity = normalSwimIntensity;
        lastSwimSpeed = normalSwimSpeed;
        lastSwimIntensity = normalSwimIntensity;
        stableAvoidanceDirection = Vector3.zero;
        cachedAvoidanceDirection = Vector3.zero;
        
        // Initialize timers
        neighborUpdateTimer = Random.Range(0f, neighborUpdateInterval); // Stagger updates
        obstacleCheckTimer = Random.Range(0f, obstacleCheckInterval);
    }
    
    void OnEnable()
    {
        // Register this fish
        if (!allFish.Contains(this))
            allFish.Add(this);
    }
    
    void OnDisable()
    {
        // Unregister this fish
        if (allFish.Contains(this))
            allFish.Remove(this);
    }
    
    void Update()
    {
        // Update neighbors periodically for performance
        neighborUpdateTimer += Time.deltaTime;
        if (neighborUpdateTimer >= neighborUpdateInterval)
        {
            FindNeighbors();
            neighborUpdateTimer = 0f;
        }
        
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
        
        // Calculate obstacle avoidance with performance optimization
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
        obstacleCheckTimer += Time.deltaTime;
        
        // Only raycast periodically unless in emergency
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
        
        // Optimized shader parameter updates - only when values change significantly
        if (fishRenderer != null && propertyBlock != null)
        {
            if (Mathf.Abs(smoothedSwimSpeed - lastSwimSpeed) > shaderUpdateThreshold ||
                Mathf.Abs(smoothedSwimIntensity - lastSwimIntensity) > shaderUpdateThreshold)
            {
                propertyBlock.SetFloat("_SwimSpeed", smoothedSwimSpeed);
                propertyBlock.SetFloat("_SwimIntensity", smoothedSwimIntensity);
                fishRenderer.SetPropertyBlock(propertyBlock);
                
                lastSwimSpeed = smoothedSwimSpeed;
                lastSwimIntensity = smoothedSwimIntensity;
            }
        }
        
        // Backwards compatibility - also update material if it exists
        if (fishMaterial != null)
        {
            if (Mathf.Abs(smoothedSwimSpeed - lastSwimSpeed) > shaderUpdateThreshold ||
                Mathf.Abs(smoothedSwimIntensity - lastSwimIntensity) > shaderUpdateThreshold)
            {
                fishMaterial.SetFloat("_SwimSpeed", smoothedSwimSpeed);
                fishMaterial.SetFloat("_SwimIntensity", smoothedSwimIntensity);
            }
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
        GUILayout.Label($"Current Speed: {velocity.magnitude:F2} / {maxSpeed:F2}");
        GUILayout.Label($"Is Avoiding: {isAvoiding}");
        GUILayout.Label($"Is Emergency: {isInEmergency}");
        GUILayout.Label($"Has Target: {target != null}");
        
        // Performance info
        GUILayout.Label($"Neighbor Update Timer: {neighborUpdateTimer:F2}");
        GUILayout.Label($"Obstacle Check Timer: {obstacleCheckTimer:F2}");
        
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
}