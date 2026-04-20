using UnityEngine;

[DisallowMultipleComponent]
public sealed class CollisionEventCache : MonoBehaviour
{
    private const int Capacity = 8;

    private readonly PhysicsLensCollisionEvent[] _events = new PhysicsLensCollisionEvent[Capacity];
    private int _head;
    private int _count;

    public PhysicsLensCollisionEvent Latest { get; private set; }

    public static CollisionEventCache GetOrAdd(Rigidbody rb)
    {
        if (rb == null)
            return null;

        var cache = rb.GetComponent<CollisionEventCache>();
        if (cache == null)
            cache = rb.gameObject.AddComponent<CollisionEventCache>();
        return cache;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
        Latest = default;
    }

    public int CopyEvents(PhysicsLensCollisionEvent[] destination, float sinceTime)
    {
        if (destination == null || destination.Length == 0 || _count == 0)
            return 0;

        var copied = 0;
        var oldest = _head - _count;
        if (oldest < 0)
            oldest += Capacity;

        for (var i = 0; i < _count && copied < destination.Length; i++)
        {
            var index = (oldest + i) % Capacity;
            var evt = _events[index];
            if (evt.IsValid && evt.Time >= sinceTime)
                destination[copied++] = evt;
        }

        return copied;
    }

    private void OnCollisionEnter(Collision collision)
    {
        Record(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.impulse.sqrMagnitude > 0.0001f)
            Record(collision);
    }

    private void Record(Collision collision)
    {
        if (collision == null)
            return;

        var impulse = collision.impulse.magnitude;
        if (impulse <= 0.0001f)
            return;

        var evt = new PhysicsLensCollisionEvent
        {
            IsValid = true,
            Time = Time.time,
            ImpulseMagnitude = impulse,
            Point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position,
            PartnerName = ResolvePartnerName(collision)
        };

        _events[_head] = evt;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
            _count++;

        Latest = evt;
    }

    private static string ResolvePartnerName(Collision collision)
    {
        if (collision.rigidbody != null)
        {
            var placeable = collision.rigidbody.GetComponentInParent<PlaceableAsset>();
            if (placeable != null && !string.IsNullOrEmpty(placeable.AssetDisplayName))
                return placeable.AssetDisplayName;

            return collision.rigidbody.name;
        }

        if (collision.collider != null)
            return collision.collider.name;

        return "Scene";
    }
}
