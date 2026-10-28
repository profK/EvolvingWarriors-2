using UnityEngine;

/// <summary>
/// Marks a transform as an attack surface (e.g., sword).
/// Uses a trigger collider to detect contacts with damage surfaces.
/// </summary>
public class AttackSurface : MonoBehaviour
{
    [Tooltip("Logical identifier for this attack surface (e.g., \"sword\").")]
    [SerializeField] string surfaceId = "sword";

    /// <summary>Logical identifier for this attack surface.</summary>
    public string SurfaceId => surfaceId;

    /// <summary>The warrior agent that owns this attack surface.</summary>
    WarriorAgent ownerAgent;

    void Awake()
    {
        // Walk up to find the owning WarriorAgent
        Transform t = transform;
        while (t != null)
        {
            ownerAgent = t.GetComponent<WarriorAgent>();
            if (ownerAgent != null) break;
            t = t.parent;
        }

        if (ownerAgent == null)
        {
            Debug.LogError($"AttackSurface on {name} has no owning WarriorAgent.", this);
            enabled = false;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (ownerAgent == null) return;

        WarriorCombatScorer scorer = ownerAgent.GetComponent<WarriorCombatScorer>();
        if (scorer == null) return;

        // Check for damage surface
        DamageSurface damageSurface = other.GetComponent<DamageSurface>();
        if (damageSurface != null)
        {
            scorer.OnContactBegin(damageSurface.SurfaceId, damageSurface.OwnerAgent);
            return;
        }

        // Check for shield surface (separate from damage)
        ShieldSurface shieldSurface = other.GetComponent<ShieldSurface>();
        if (shieldSurface != null)
        {
            scorer.OnShieldContactBegin(shieldSurface.SurfaceId, shieldSurface.OwnerAgent);
            return;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (ownerAgent == null) return;

        WarriorCombatScorer scorer = ownerAgent.GetComponent<WarriorCombatScorer>();
        if (scorer == null) return;

        // Check for damage surface
        DamageSurface damageSurface = other.GetComponent<DamageSurface>();
        if (damageSurface != null)
        {
            scorer.OnContactEnd(damageSurface.SurfaceId);
            return;
        }

        // Check for shield surface (separate from damage)
        ShieldSurface shieldSurface = other.GetComponent<ShieldSurface>();
        if (shieldSurface != null)
        {
            scorer.OnShieldContactEnd(shieldSurface.SurfaceId);
            return;
        }
    }
}
