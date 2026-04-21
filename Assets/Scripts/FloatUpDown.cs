using UnityEngine;

public class FloatUpDown : MonoBehaviour
{
    [SerializeField] private float amplitude = 0.05f;
    [SerializeField] private float speed = 2f;

    private Vector3 startPos;

    private void Start()
    {
        startPos = transform.position;
    }

    private void Update()
    {
        float yOffset = Mathf.Sin(Time.time * speed) * amplitude;
        transform.position = startPos + Vector3.up * yOffset;
    }
}