using System.Collections.Generic;
using UnityEngine;

public sealed class PhysicsLensSpringMetadata : MonoBehaviour
{
    private static readonly List<PhysicsLensSpringMetadata> Scratch = new List<PhysicsLensSpringMetadata>(4);

    [SerializeField] private SpringJoint springJoint;
    [SerializeField] private float restLength = -1f;

    public SpringJoint SpringJoint => springJoint;
    public bool HasRestLength => restLength > 0f;
    public float RestLength => restLength;

    public void Initialize(SpringJoint joint, float inferredRestLength)
    {
        springJoint = joint;
        restLength = Mathf.Max(0.001f, inferredRestLength);
    }

    public static PhysicsLensSpringMetadata GetOrCreate(SpringJoint joint, float inferredRestLength)
    {
        if (joint == null)
            return null;

        Scratch.Clear();
        joint.GetComponents(Scratch);
        for (var i = 0; i < Scratch.Count; i++)
        {
            var metadata = Scratch[i];
            if (metadata != null && metadata.springJoint == joint)
            {
                if (!metadata.HasRestLength)
                    metadata.Initialize(joint, inferredRestLength);
                Scratch.Clear();
                return metadata;
            }
        }

        var created = joint.gameObject.AddComponent<PhysicsLensSpringMetadata>();
        created.Initialize(joint, inferredRestLength);
        Scratch.Clear();
        return created;
    }
}
