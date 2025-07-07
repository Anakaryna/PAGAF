using UnityEngine;
using System.Collections;

public class DragonSpawner : MonoBehaviour
{
    [Header("Dragon Spawning")]
    public DragonController dragonPrefab;
    public int dragonCount = 2;
    public float spawnRadius = 20f;
    public float spawnHeight = 10f;
    
    [Header("Dragon Settings")]
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
    public float turnSpeed = 120f;
    
    [Header("Patrol Settings")]
    public Transform[] patrolPoints;
    public bool createPatrolRoute = true;
    public float patrolRadius = 50f;
    public int patrolPointsToCreate = 4;
    
    [Header("Spawn Timing")]
    public bool spawnOnStart = true;
    public float spawnDelay = 1f;
    
    [Header("Debug")]
    public bool showSpawnArea = true;
    public bool showPatrolRoutes = true;
    
    void Start()
    {
        if (spawnOnStart)
        {
            if (spawnDelay > 0f)
            {
                StartCoroutine(SpawnDragonsGradually());
            }
            else
            {
                SpawnDragons();
            }
        }
    }
    
    IEnumerator SpawnDragonsGradually()
    {
        // Wait for manager to be ready
        while (OptimizedFishManager.Instance == null)
        {
            yield return null;
        }
        
        for (int i = 0; i < dragonCount; i++)
        {
            SpawnSingleDragon(i);
            
            if (spawnDelay > 0f)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }
        
        Debug.Log($"Spawned {dragonCount} dragons!");
    }
    
    void SpawnDragons()
    {
        for (int i = 0; i < dragonCount; i++)
        {
            SpawnSingleDragon(i);
        }
    }
    
    void SpawnSingleDragon(int index)
    {
        Vector3 spawnPos = transform.position + Random.insideUnitSphere * spawnRadius;
        spawnPos.y = transform.position.y + spawnHeight;
        
        GameObject dragon = Instantiate(dragonPrefab.gameObject, spawnPos, Random.rotation);
        DragonController controller = dragon.GetComponent<DragonController>();
        
        if (controller != null)
        {
            // Set dragon properties
            controller.aggressionLevel = aggressionLevel;
            controller.huntingRadius = huntingRadius;
            controller.disruptionStrength = disruptionStrength;
            controller.energyDecayRate = energyDecayRate;
            controller.restDuration = restDuration;
            controller.huntingDuration = huntingDuration;
            
            // Set movement properties
            controller.maxSpeed = maxSpeed;
            controller.baseAcceleration = baseAcceleration;
            controller.huntingAcceleration = huntingAcceleration;
            controller.turnSpeed = turnSpeed;
            
            // Set patrol target
            Transform patrolTarget = GetPatrolTarget(index);
            if (patrolTarget != null)
            {
                controller.SetTarget(patrolTarget);
            }
            
            // Add some initial velocity
            controller.velocity = Random.insideUnitSphere * 2f;
            
            dragon.name = $"Dragon {index + 1}";
        }
    }
    
    Transform GetPatrolTarget(int dragonIndex)
    {
        if (patrolPoints.Length > 0)
        {
            return patrolPoints[dragonIndex % patrolPoints.Length];
        }
        else if (createPatrolRoute)
        {
            return CreatePatrolPoint(dragonIndex);
        }
        
        return null;
    }
    
    Transform CreatePatrolPoint(int dragonIndex)
    {
        GameObject patrolPoint = new GameObject($"Dragon {dragonIndex + 1} Patrol Point");
        
        // Create patrol points in a circle around the spawner
        float angle = (dragonIndex / (float)dragonCount) * 360f * Mathf.Deg2Rad;
        Vector3 patrolPos = transform.position + new Vector3(
            Mathf.Cos(angle) * patrolRadius,
            Random.Range(-5f, 5f),
            Mathf.Sin(angle) * patrolRadius
        );
        
        patrolPoint.transform.position = patrolPos;
        
        // Add a simple patrol movement script if desired
        var patrolAnimator = patrolPoint.AddComponent<SimplePatrolAnimator>();
        patrolAnimator.radius = patrolRadius * 0.3f;
        patrolAnimator.speed = Random.Range(0.5f, 1.5f);
        
        return patrolPoint.transform;
    }
    
    [ContextMenu("Spawn Dragons Now")]
    public void SpawnDragonsNow()
    {
        if (spawnDelay > 0f)
        {
            StartCoroutine(SpawnDragonsGradually());
        }
        else
        {
            SpawnDragons();
        }
    }
    
    [ContextMenu("Clear All Dragons")]
    public void ClearAllDragons()
    {
        var allDragons = FindObjectsOfType<DragonController>();
        for (int i = allDragons.Length - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
            {
                Destroy(allDragons[i].gameObject);
            }
            else
            {
                DestroyImmediate(allDragons[i].gameObject);
            }
        }
        Debug.Log($"Cleared {allDragons.Length} dragons from the scene");
    }
    
    [ContextMenu("Create Patrol Points")]
    public void CreatePatrolPoints()
    {
        // Clear existing patrol points
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(patrolPoints[i].gameObject);
                }
                else
                {
                    DestroyImmediate(patrolPoints[i].gameObject);
                }
            }
        }
        
        // Create new patrol points
        patrolPoints = new Transform[patrolPointsToCreate];
        for (int i = 0; i < patrolPointsToCreate; i++)
        {
            patrolPoints[i] = CreatePatrolPoint(i);
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showSpawnArea) return;
        
        // Draw spawn area
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        
        // Draw spawn height
        Gizmos.color = Color.magenta;
        Vector3 heightPos = transform.position + Vector3.up * spawnHeight;
        Gizmos.DrawWireSphere(heightPos, spawnRadius);
        
        if (showPatrolRoutes)
        {
            // Draw patrol radius
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, patrolRadius);
            
            // Draw patrol points
            if (patrolPoints != null)
            {
                Gizmos.color = Color.cyan;
                foreach (var point in patrolPoints)
                {
                    if (point != null)
                    {
                        Gizmos.DrawWireCube(point.position, Vector3.one * 2f);
                        Gizmos.DrawLine(transform.position, point.position);
                    }
                }
            }
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showSpawnArea) return;
        
        Gizmos.color = new Color(1, 0, 0, 0.1f);
        Gizmos.DrawSphere(transform.position, spawnRadius);
        
        Gizmos.color = new Color(1, 0.5f, 0, 0.1f);
        Vector3 heightPos = transform.position + Vector3.up * spawnHeight;
        Gizmos.DrawSphere(heightPos, spawnRadius);
    }
}

// Simple patrol animator for dragon patrol points
public class SimplePatrolAnimator : MonoBehaviour
{
    public float radius = 10f;
    public float speed = 1f;
    
    private Vector3 centerPoint;
    private float currentAngle;
    
    void Start()
    {
        centerPoint = transform.position;
        currentAngle = Random.Range(0f, 360f);
    }
    
    void Update()
    {
        currentAngle += speed * Time.deltaTime * 10f;
        
        Vector3 offset = new Vector3(
            Mathf.Cos(currentAngle * Mathf.Deg2Rad) * radius,
            Mathf.Sin(currentAngle * Mathf.Deg2Rad * 0.5f) * radius * 0.3f,
            Mathf.Sin(currentAngle * Mathf.Deg2Rad) * radius
        );
        
        transform.position = centerPoint + offset;
    }
}