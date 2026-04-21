using UnityEngine;

public class FloatUpDown : MonoBehaviour
{
    [SerializeField] private float amplitude = 0.05f;
    [SerializeField] private float speed = 2f;

    private Vector3 startLocalPos;

    private void Start()
    {
        startLocalPos = transform.localPosition;
    }

    private void Update()
    {
        float yOffset = Mathf.Sin(Time.time * speed) * amplitude;
        if (transform.parent == null)
        {
            transform.localPosition = startLocalPos + Vector3.up * yOffset;
            return;
        }

        transform.localPosition = startLocalPos + transform.parent.InverseTransformVector(Vector3.up * yOffset);
    }
}
