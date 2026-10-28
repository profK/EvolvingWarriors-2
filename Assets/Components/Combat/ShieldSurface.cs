using UnityEngine;

/// <summary>
/// Marks a transform as a shield surface (e.g., shield, armored plate).
/// Uses a trigger collider to detect contacts from attack surfaces.
/// 
/// Shield surfaces are INDEPENDENT from damage surfaces:
/// - Hitting a shield does NOT count as hitting a damage surface
/// - Shield hits reward the defender, not the attacker
/// </summary>
public class ShieldSurface : MonoBehaviour
{
    [Tooltip("Logical identifier for this shield surface (e.g., \"shield\", \"arm\").")]
    [SerializeField] string surfaceId = "shield";

    /// <summary>Logical identifier for this shield surface.</summary>
    public string SurfaceId => surfaceId;

    /// <summary>The warrior agent that owns this shield surface.</summary>
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
            Debug.LogError($"ShieldSurface on {name} has no owning WarriorAgent.", this);
            enabled = false;
        }
    }
}
