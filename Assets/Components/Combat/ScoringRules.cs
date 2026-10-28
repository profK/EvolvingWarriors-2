using UnityEngine;

/// <summary>
/// Configurable scoring rules for combat encounters.
/// 
/// Holds six independent score values grouped by surface type:
/// - Self Hit: score applied to the attacker when hitting their own body
/// - Damage Hit: score applied to the attacker when hitting the opponent's damage surface
/// - Shield Hit: score applied to the defender when the opponent's attack hits a shield
/// 
/// This is a ScriptableObject so the values can be configured in an asset
/// and shared across all warriors in the simulation.
/// </summary>
[CreateAssetMenu(fileName = "ScoringRules", menuName = "EvolvingWarriors/Scoring Rules")]
public class ScoringRules : ScriptableObject
{
    [Header("Self Hit")]
    [Tooltip("Score applied to the attacker when their attack surface hits their OWN body. Typically negative.")]
    [SerializeField] int selfHitScore = -1;
    public int SelfHitScore => selfHitScore;

    [Header("Damage Hit")]
    [Tooltip("Score applied to the attacker when their attack surface hits the OPPONENT's damage surface. Typically positive.")]
    [SerializeField] int damageHitAttackerScore = 1;
    public int DamageHitAttackerScore => damageHitAttackerScore;

    [Tooltip("Score applied to the defender when their damage surface is hit. Typically negative or zero.")]
    [SerializeField] int damageHitDefenderScore = 0;
    public int DamageHitDefenderScore => damageHitDefenderScore;

    [Header("Shield Hit")]
    [Tooltip("Score applied to the attacker when their attack surface hits the OPPONENT's shield. Can be positive, negative, or zero.")]
    [SerializeField] int shieldHitAttackerScore = 0;
    public int ShieldHitAttackerScore => shieldHitAttackerScore;

    [Tooltip("Score applied to the defender when their shield is hit. Typically positive.")]
    [SerializeField] int shieldHitDefenderScore = 1;
    public int ShieldHitDefenderScore => shieldHitDefenderScore;
}
