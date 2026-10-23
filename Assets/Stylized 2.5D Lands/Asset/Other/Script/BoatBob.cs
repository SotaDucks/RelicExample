using UnityEngine;

public class BoatBob : MonoBehaviour
{
    [Header("Movement Settings")]
    public float bobHeight = 0.5f;
    public float bobSpeed = 1.0f;
    public float rollAngle = 5.0f;
    public float rollSpeed = 0.8f;
    public float pitchAngle = 3.0f;
    public float pitchSpeed = 0.6f;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float randomOffset;

    void Start()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        randomOffset = Random.Range(0f, 100f);
    }

    void Update()
    {
        float time = Time.time + randomOffset;
        
        float vertical = Mathf.Sin(time * bobSpeed) * bobHeight;
        Vector3 newPos = startPosition;
        newPos.y += vertical;
        transform.position = newPos;
        
        float roll = Mathf.Sin(time * rollSpeed) * rollAngle;
        float pitch = Mathf.Cos(time * pitchSpeed) * pitchAngle;
        Quaternion targetRot = startRotation * Quaternion.Euler(pitch, 0, roll);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 2f);
    }
}