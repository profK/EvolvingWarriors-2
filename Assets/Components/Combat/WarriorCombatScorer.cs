using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks combat contacts and manages scoring for a warrior.
/// 
/// Contact state tracking:
/// - Contact begins: +1 (opponent) or -1 (self)
/// - Contact continues: no additional scoring
/// - Contact ends: clear state
/// - Only scores against assigned opponent
/// </summary>
public class WarriorCombatScorer : MonoBehaviour
{
    /// <summary>The warrior agent this scorer belongs to.</summary>
    public WarriorAgent Agent { get; set; }

    /// <summary>Whether scoring is currently enabled.</summary>
    public bool ScoringEnabled { get; private set; }

    /// <summary>Set of active logical damage surface IDs being contacted by this warrior's sword.</summary>
    readonly HashSet<string> activeContacts = new System.Collections.Generic.HashSet<string>();

    /// <summary>Enables scoring.</summary>
    public void EnableScoring() => ScoringEnabled = true;

    /// <summary>Disables scoring and clears contact state.</summary>
    public void DisableScoring()
    {
        ScoringEnabled = false;
        activeContacts.Clear();
    }

    /// <summary>
    /// Called when this warrior's sword enters a damage surface.
    /// </summary>
    /// <param name="surfaceId">Logical surface identifier (e.g. "head", "torso").</param>
    /// <param name="owner">The warrior agent that owns this damage surface.</param>
    public void OnContactBegin(string surfaceId, WarriorAgent owner)
    {
        if (!ScoringEnabled) return;
        if (activeContacts.Contains(surfaceId)) return; // Already in contact

        activeContacts.Add(surfaceId);

        if (owner == Agent)
        {
            // Self-inflicted contact
            Agent.AddScore(-1);
        }
        else if (owner == Agent.Opponent)
        {
            // Valid attack on opponent
            Agent.AddScore(1);
        }
        // Contact with unrelated warrior — no scoring
    }

    /// <summary>
    /// Called when this warrior's sword exits a damage surface.
    /// </summary>
    public void OnContactEnd(string surfaceId)
    {
        activeContacts.Remove(surfaceId);
    }
}
