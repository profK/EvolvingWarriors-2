using UnityEngine;

/// <summary>
/// Marks a transform as a damage surface (e.g., head, torso).
/// Uses a trigger collider to detect contacts from attack surfaces.
/// </summary>
public class DamageSurface : MonoBehaviour
{
    [Tooltip("Logical identifier for this damage surface (e.g., \"head\", \"torso\").")]
    [SerializeField] string surfaceId = "head";

    /// <summary>Logical identifier for this damage surface.</summary>
    public string SurfaceId => surfaceId;

    /// <summary>The warrior agent that owns this damage surface.</summary>
    public WarriorAgent OwnerAgent { get; private set; }

    void Awake()
    {
        // Walk up to find the owning WarriorAgent
        Transform t = transform;
        while (t != null)
        {
            OwnerAgent = t.GetComponent<WarriorAgent>();
            if (OwnerAgent != null) break;
            t = t.parent;
        }

        if (OwnerAgent == null)
        {
            Debug.LogError($"DamageSurface on {name} has no owning WarriorAgent.", this);
            enabled = false;
        }
    }
}
