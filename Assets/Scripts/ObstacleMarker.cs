using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ObstacleMarker : MonoBehaviour
{
    [Header("Obstacle Settings")]
    public float influenceRadius = 3f;
    
    void Start()
    {
        // Ensure this obstacle is on the correct layer
        if (gameObject.layer == 0)
        {
            Debug.LogWarning($"Obstacle {gameObject.name} is on Default layer. Consider moving to Obstacle layer.");
        }
    }
    
    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, influenceRadius);
    }
}