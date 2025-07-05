using UnityEngine;

public class TargetAnimator : MonoBehaviour
{
    public enum MovementPattern
    {
        StraightLine,
        Circle,
        Square,
        Figure8
    }
    
    [Header("Movement Pattern")]
    public MovementPattern pattern = MovementPattern.Circle;
    public float speed = 2f;
    public float radius = 5f;
    public bool randomizeOnStart = true;
    
    private Vector3 startPosition;
    private float time;
    private float randomOffset;
    
    void Start()
    {
        startPosition = transform.position;
        if (randomizeOnStart)
        {
            randomOffset = Random.Range(0f, Mathf.PI * 2f);
        }
    }
    
    void Update()
    {
        time += Time.deltaTime * speed;
        
        switch (pattern)
        {
            case MovementPattern.StraightLine:
                MoveStraightLine();
                break;
            case MovementPattern.Circle:
                MoveCircle();
                break;
            case MovementPattern.Square:
                MoveSquare();
                break;
            case MovementPattern.Figure8:
                MoveFigure8();
                break;
        }
    }
    
    void MoveStraightLine()
    {
        float x = Mathf.PingPong(time + randomOffset, radius * 2) - radius;
        transform.position = startPosition + Vector3.right * x;
    }
    
    void MoveCircle()
    {
        float x = Mathf.Cos(time + randomOffset) * radius;
        float z = Mathf.Sin(time + randomOffset) * radius;
        transform.position = startPosition + new Vector3(x, 0, z);
    }
    
    void MoveSquare()
    {
        float normalizedTime = ((time + randomOffset) % (4f)) / 4f;
        Vector3 offset = Vector3.zero;
        
        if (normalizedTime < 0.25f)
        {
            offset = Vector3.Lerp(Vector3.zero, Vector3.right * radius, normalizedTime * 4);
        }
        else if (normalizedTime < 0.5f)
        {
            offset = Vector3.Lerp(Vector3.right * radius, Vector3.right * radius + Vector3.forward * radius, (normalizedTime - 0.25f) * 4);
        }
        else if (normalizedTime < 0.75f)
        {
            offset = Vector3.Lerp(Vector3.right * radius + Vector3.forward * radius, Vector3.forward * radius, (normalizedTime - 0.5f) * 4);
        }
        else
        {
            offset = Vector3.Lerp(Vector3.forward * radius, Vector3.zero, (normalizedTime - 0.75f) * 4);
        }
        
        transform.position = startPosition + offset;
    }
    
    void MoveFigure8()
    {
        float x = Mathf.Sin(time + randomOffset) * radius;
        float z = Mathf.Sin(2 * (time + randomOffset)) * radius * 0.5f;
        transform.position = startPosition + new Vector3(x, 0, z);
    }
}