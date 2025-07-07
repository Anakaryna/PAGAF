using UnityEngine;
using System.Collections;

public class FishSpawnerBoids : MonoBehaviour
{
    [Header("Spawning")]
    public OptimizedFishController fishPrefab;
    public int fishCount = 20;
    public float spawnRadius = 5f;
    public FishSpecies species;
    public float fishSize = 1f;
    
    [Header("Spawn Timing")]
    public bool spawnOnStart = true;
    public float spawnDelay = 0.01f;
    public bool randomizeSpecies = false;
    
    [Header("Shared Target")]
    public Transform sharedTarget;
    public bool createDefaultTarget = true;
    
    [Header("Boids Settings")]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;
    public float targetWeight = 2f;
    
    [Header("Movement Settings")]
    public float neighborRadius = 3f;
    public float separationRadius = 1.5f;
    public float maxSpeed = 5f;
    public float baseAcceleration = 2f;
    public float boostAcceleration = 8f;
    
    [Header("Debug")]
    public bool showSpawnArea = true;
    
    void Start()
    {
        if (spawnOnStart)
        {
            if (spawnDelay > 0f)
            {
                StartCoroutine(SpawnSchoolGradually());
            }
            else
            {
                SpawnSchoolImmediate();
            }
        }
    }
    
    void CreateDefaultTarget()
    {
        GameObject targetObj = new GameObject($"{species} School Target");
        targetObj.transform.position = transform.position + Vector3.forward * 10f;
        
        // Add TargetAnimator component for movement if you have it
        var animator = targetObj.GetComponent<TargetAnimator>();
        if (animator == null && System.Type.GetType("TargetAnimator") != null)
        {
            animator = targetObj.AddComponent<TargetAnimator>();
            // Set default values if the component exists
            var animatorType = animator.GetType();
            var patternField = animatorType.GetField("pattern");
            var speedField = animatorType.GetField("speed");
            var radiusField = animatorType.GetField("radius");
            var randomizeField = animatorType.GetField("randomizeOnStart");
            
            if (patternField != null) patternField.SetValue(animator, 1); // Circle pattern
            if (speedField != null) speedField.SetValue(animator, 1f);
            if (radiusField != null) radiusField.SetValue(animator, 8f);
            if (randomizeField != null) randomizeField.SetValue(animator, true);
        }
        
        sharedTarget = targetObj.transform;
    }
    
    IEnumerator SpawnSchoolGradually()
    {
        // Wait for manager to be ready
        while (OptimizedFishManager.Instance == null)
        {
            yield return null;
        }
        
        // Create default target if none assigned and option is enabled
        if (sharedTarget == null && createDefaultTarget)
        {
            CreateDefaultTarget();
        }
        
        if (fishPrefab == null)
        {
            Debug.LogError("Fish prefab is not assigned!");
            yield break;
        }

        if (sharedTarget == null)
        {
            Debug.LogError("No target assigned and createDefaultTarget is false!");
            yield break;
        }

        Debug.Log($"Starting to spawn {fishCount} {species} fish...");
        
        for (int i = 0; i < fishCount; i++)
        {
            SpawnSingleFish(i);
            
            // Wait before spawning next fish
            if (spawnDelay > 0f)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }
        
        Debug.Log($"Finished spawning {fishCount} {species} fish!");
    }
    
    void SpawnSchoolImmediate()
    {
        // Create default target if none assigned and option is enabled
        if (sharedTarget == null && createDefaultTarget)
        {
            CreateDefaultTarget();
        }
        
        if (fishPrefab == null)
        {
            Debug.LogError("Fish prefab is not assigned!");
            return;
        }

        if (sharedTarget == null)
        {
            Debug.LogError("No target assigned and createDefaultTarget is false!");
            return;
        }
        
        for (int i = 0; i < fishCount; i++)
        {
            SpawnSingleFish(i);
        }
    }
    
    void SpawnSingleFish(int index)
    {
        // Spawn in a sphere formation
        Vector3 randomPos = Random.insideUnitSphere * spawnRadius;
        randomPos += transform.position;
        
        Quaternion randomRot = Random.rotation;
        
        GameObject fish = Instantiate(fishPrefab.gameObject, randomPos, randomRot);
        OptimizedFishController controller = fish.GetComponent<OptimizedFishController>();
        
        if (controller != null)
        {
            // Set species and size
            if (randomizeSpecies)
            {
                controller.species = (FishSpecies)Random.Range(0, 7); // Exclude dragon
                controller.fishSize = Random.Range(0.8f, 1.2f);
            }
            else
            {
                controller.species = species;
                controller.fishSize = fishSize;
            }
            
            // Set boids parameters
            controller.separationWeight = separationWeight;
            controller.alignmentWeight = alignmentWeight;
            controller.cohesionWeight = cohesionWeight;
            controller.targetWeight = targetWeight;
            controller.neighborRadius = neighborRadius;
            controller.separationRadius = separationRadius;
            controller.maxSpeed = maxSpeed;
            controller.baseAcceleration = baseAcceleration;
            controller.boostAcceleration = boostAcceleration;
            
            // Set the shared target
            controller.SetTarget(sharedTarget);
            
            // Add some initial velocity for more natural spawning
            controller.velocity = new Vector3(
                Random.Range(-2f, 2f),
                Random.Range(-1f, 1f),
                Random.Range(-2f, 2f)
            );
            
            // Name the fish for easier debugging
            fish.name = $"{controller.species} Fish {index + 1}";
        }
    }
    
    // Call this if you want to change the target for all fish in this school
    public void ChangeTarget(Transform newTarget)
    {
        sharedTarget = newTarget;
        
        // Update all existing fish from this spawner
        OptimizedFishController[] allFish = FindObjectsOfType<OptimizedFishController>();
        foreach (var fish in allFish)
        {
            if (fish.GetSpecies() == species)
            {
                fish.SetTarget(newTarget);
            }
        }
    }
    
    [ContextMenu("Spawn Fish Now")]
    public void SpawnFishNow()
    {
        if (spawnDelay > 0f)
        {
            StartCoroutine(SpawnSchoolGradually());
        }
        else
        {
            SpawnSchoolImmediate();
        }
    }

    [ContextMenu("Clear All Fish")]
    public void ClearAllFish()
    {
        var allFish = FindObjectsOfType<OptimizedFishController>();
        for (int i = allFish.Length - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
            {
                Destroy(allFish[i].gameObject);
            }
            else
            {
                DestroyImmediate(allFish[i].gameObject);
            }
        }
        Debug.Log($"Cleared {allFish.Length} fish from the scene");
    }
    
    void OnDrawGizmos()
    {
        if (!showSpawnArea) return;

        // Draw spawn area
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        
        // Draw connection to target
        if (sharedTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, sharedTarget.position);
            Gizmos.DrawWireCube(sharedTarget.position, Vector3.one);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showSpawnArea) return;

        Gizmos.color = new Color(1, 1, 0, 0.1f);
        Gizmos.DrawSphere(transform.position, spawnRadius);
    }
}