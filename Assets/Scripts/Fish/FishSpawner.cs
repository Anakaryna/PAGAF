// using UnityEngine;
//
// public class FishSpawner : MonoBehaviour
// {
//     [Header("Spawning")]
//     public GameObject fishPrefab;
//     public int fishCount = 20;
//     public float spawnRadius = 5f;
//     public FishSpecies species;
//     public float fishSize = 1f;
//     
//     [Header("Shared Target")]
//     public Transform sharedTarget; // Assign your TargetAnimator object here
//     public bool createDefaultTarget = true; // If no target is assigned, create a simple one
//     
//     [Header("Boids Settings")]
//     public float separationWeight = 1.5f;
//     public float alignmentWeight = 1f;
//     public float cohesionWeight = 1f;
//     public float targetWeight = 2f;
//     
//     [Header("Movement Settings")]
//     public float neighborRadius = 3f;
//     public float separationRadius = 1.5f;
//     
//     void Start()
//     {
//         // Create default target if none assigned and option is enabled
//         if (sharedTarget == null && createDefaultTarget)
//         {
//             CreateDefaultTarget();
//         }
//         
//         SpawnSchool();
//     }
//     
//     void CreateDefaultTarget()
//     {
//         GameObject targetObj = new GameObject($"{species} School Target");
//         targetObj.transform.position = transform.position + Vector3.forward * 10f;
//         
//         // Add TargetAnimator component for movement
//         TargetAnimator animator = targetObj.AddComponent<TargetAnimator>();
//         animator.pattern = TargetAnimator.MovementPattern.Circle;
//         animator.speed = 1f;
//         animator.radius = 8f;
//         animator.randomizeOnStart = true;
//         
//         sharedTarget = targetObj.transform;
//     }
//     
//     void SpawnSchool()
//     {
//         for (int i = 0; i < fishCount; i++)
//         {
//             // Spawn in a sphere formation
//             Vector3 randomPos = Random.insideUnitSphere * spawnRadius;
//             randomPos += transform.position;
//             
//             Quaternion randomRot = Random.rotation;
//             
//             GameObject fish = Instantiate(fishPrefab, randomPos, randomRot);
//             AutonomousFishController controller = fish.GetComponent<AutonomousFishController>();
//             
//             if (controller != null)
//             {
//                 // Set species and size
//                 controller.species = species;
//                 controller.fishSize = fishSize;
//                 
//                 // Set boids parameters
//                 controller.separationWeight = separationWeight;
//                 controller.alignmentWeight = alignmentWeight;
//                 controller.cohesionWeight = cohesionWeight;
//                 controller.targetWeight = targetWeight;
//                 controller.neighborRadius = neighborRadius;
//                 controller.separationRadius = separationRadius;
//                 
//                 // Set the shared target
//                 controller.SetTarget(sharedTarget);
//                 
//                 // Name the fish for easier debugging
//                 fish.name = $"{species} Fish {i + 1}";
//             }
//         }
//     }
//     
//     // Call this if you want to change the target for all fish in this school
//     public void ChangeTarget(Transform newTarget)
//     {
//         sharedTarget = newTarget;
//         
//         // Update all existing fish from this spawner
//         AutonomousFishController[] allFish = FindObjectsOfType<AutonomousFishController>();
//         foreach (var fish in allFish)
//         {
//             if (fish.GetSpecies() == species)
//             {
//                 fish.SetTarget(newTarget);
//             }
//         }
//     }
//     
//     void OnDrawGizmosSelected()
//     {
//         // Draw spawn area
//         Gizmos.color = Color.yellow;
//         Gizmos.DrawWireSphere(transform.position, spawnRadius);
//         
//         // Draw connection to target
//         if (sharedTarget != null)
//         {
//             Gizmos.color = Color.green;
//             Gizmos.DrawLine(transform.position, sharedTarget.position);
//             Gizmos.DrawWireCube(sharedTarget.position, Vector3.one);
//         }
//     }
// }