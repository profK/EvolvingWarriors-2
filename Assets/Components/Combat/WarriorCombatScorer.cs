using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Tracks combat contacts and manages scoring for a warrior.
/// 
/// Contact state tracking:
/// - Damage contact begins: self-hit or damage-hit scored
/// - Shield contact begins: shield-hit scored (defender rewarded)
/// - Contact continues: no additional scoring
/// - Contact ends: clear state
/// - Only scores against assigned opponent
/// - All score values come from a shared ScoringRules asset
/// </summary>
public class WarriorCombatScorer : MonoBehaviour
{
    /// <summary>The warrior agent this scorer belongs to.</summary>
    public WarriorAgent Agent { get; set; }

    /// <summary>Whether scoring is currently enabled.</summary>
    public bool ScoringEnabled { get; private set; }

    /// <summary>Configurable scoring rules reference. Set by the simulation manager.</summary>
    public ScoringRules Rules { get; set; }

    /// <summary>Set of active logical damage surface IDs being contacted by this warrior's sword.</summary>
    /// Keys are prefixed as "damage:surfaceId" to distinguish from shield contacts.
    readonly HashSet<string> activeDamageContacts = new HashSet<string>();

    /// <summary>Set of active logical shield surface IDs being contacted by this warrior's sword.</summary>
    /// Keys are prefixed as "shield:surfaceId" to distinguish from damage contacts.
    readonly HashSet<string> activeShieldContacts = new HashSet<string>();

    /// <summary>Enables scoring.</summary>
    public void EnableScoring() => ScoringEnabled = true;

    /// <summary>Disables scoring and clears contact state.</summary>
    public void DisableScoring()
    {
        ScoringEnabled = false;
        activeDamageContacts.Clear();
        activeShieldContacts.Clear();
    }

    /// <summary>
    /// Called when this warrior's sword enters a damage surface.
    /// </summary>
    /// <param name="surfaceId">Logical surface identifier (e.g. "head", "torso").</param>
    /// <param name="owner">The warrior agent that owns this damage surface.</param>
    public void OnContactBegin(string surfaceId, WarriorAgent owner)
    {
        if (!ScoringEnabled) return;

        string key = "damage:" + surfaceId;
        if (activeDamageContacts.Contains(key)) return; // Already in contact

        activeDamageContacts.Add(key);

        if (owner == Agent)
        {
            // Self-inflicted contact
            StringBuilder logstr = new StringBuilder("Sword Self hit: ");
            logstr.Append("Warrior=").Append(Agent);
            logstr.Append(" Prescore=").Append(Agent.Score);
            Agent.AddScore(Rules?.SelfHitScore ?? -1);
            logstr.Append(" Postscore=").Append(Agent.Score);
            Debug.Log(logstr);
        }
        else if (owner == Agent.Opponent)
        {
            // Valid attack on opponent's damage surface — score for both sides
            StringBuilder logstr = new StringBuilder("Sword opponent hit: ");
            logstr.Append("Warrior=").Append(Agent);
            logstr.Append(" Prescore=").Append(Agent.Score);
            Agent.AddScore(Rules?.DamageHitAttackerScore ?? 1);
            logstr.Append(" Postscore=").Append(Agent.Score);
            Debug.Log(logstr);
        }
        // Contact with unrelated warrior — no scoring
    }

    /// <summary>
    /// Called when this warrior's sword exits a damage surface.
    /// </summary>
    public void OnContactEnd(string surfaceId)
    {
        activeDamageContacts.Remove("damage:" + surfaceId);
    }

    /// <summary>
    /// Called when this warrior's sword enters a shield surface.
    /// </summary>
    /// <param name="surfaceId">Logical surface identifier (e.g. "shield").</param>
    /// <param name="owner">The warrior agent that owns this shield surface (the defender).</param>
    public void OnShieldContactBegin(string surfaceId, WarriorAgent owner)
    {
        if (!ScoringEnabled) return;

        string key = "shield:" + surfaceId;
        if (activeShieldContacts.Contains(key)) return; // Already in contact

        activeShieldContacts.Add(key);

        if (owner == Agent)
        {
            // Hitting own shield — treat as self-hit
            StringBuilder logstr = new StringBuilder("Shield self hit: ");
            logstr.Append("Warrior=").Append(Agent);
            logstr.Append(" Prescore=").Append(Agent.Score);
            Agent.AddScore(Rules?.SelfHitScore ?? -1);
            logstr.Append(" Postscore=").Append(Agent.Score);
            Debug.Log(logstr);
            
        }
        else if (owner == Agent.Opponent)
        {
            // Hit opponent's shield — score for defender
            StringBuilder logstr = new StringBuilder("Shield other hit: ");
            logstr.Append("Warrior=").Append(Agent.Opponent);
            logstr.Append(" Prescore=").Append(Agent.Opponent.Score);
            owner.AddScore(Rules?.ShieldHitDefenderScore ?? 1);
            logstr.Append(" Postscore=").Append(Agent.Score);
            Debug.Log(logstr);
        }
        // Contact with unrelated warrior — no scoring
    }

    /// <summary>
    /// Called when this warrior's sword exits a shield surface.
    /// </summary>
    public void OnShieldContactEnd(string surfaceId)
    {
        activeShieldContacts.Remove("shield:" + surfaceId);
    }
}
