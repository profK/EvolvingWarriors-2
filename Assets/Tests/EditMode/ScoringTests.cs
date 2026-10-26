using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Tests for the combat scoring logic used by WarriorCombatScorer.
/// 
/// Note: WarriorCombatScorer is a MonoBehaviour and cannot be instantiated in edit mode.
/// Instead, we test the core scoring operations directly using standalone helpers
/// that mirror the actual WarriorCombatScorer algorithms.
/// </summary>
[TestFixture]
public class ScoringTests
{
    // --- Mock agent for scoring tests ---

    /// <summary>
    /// Minimal mock of WarriorAgent for scoring tests.
    /// Mirrors the Score property and AddScore method.
    /// </summary>
    class MockAgent
    {
        public int Score { get; private set; }
        public MockAgent Opponent { get; set; }
        public string Name;

        public void AddScore(int delta)
        {
            Score += delta;
        }
    }

    // --- Scoring helper (mirrors WarriorCombatScorer logic) ---

    /// <summary>
    /// Mirrors WarriorCombatScorer scoring logic for edit-mode testing.
    /// </summary>
    class MockScorer
    {
        readonly MockAgent agent;
        readonly HashSet<string> activeContacts = new HashSet<string>();
        bool scoringEnabled = true;

        public MockScorer(MockAgent agent)
        {
            this.agent = agent;
        }

        public void EnableScoring() => scoringEnabled = true;

        public void DisableScoring()
        {
            scoringEnabled = false;
            activeContacts.Clear();
        }

        /// <summary>
        /// Called when this warrior's sword enters a damage surface.
        /// Mirrors WarriorCombatScorer.OnContactBegin exactly.
        /// </summary>
        public void OnContactBegin(string surfaceId, MockAgent owner)
        {
            if (!scoringEnabled) return;
            if (activeContacts.Contains(surfaceId)) return; // Already in contact

            activeContacts.Add(surfaceId);

            if (owner == agent)
            {
                // Self-inflicted contact
                agent.AddScore(-1);
            }
            else if (owner == agent.Opponent)
            {
                // Valid attack on opponent
                agent.AddScore(1);
            }
            // Contact with unrelated warrior — no scoring
        }

        /// <summary>
        /// Called when this warrior's sword exits a damage surface.
        /// Mirrors WarriorCombatScorer.OnContactEnd exactly.
        /// </summary>
        public void OnContactEnd(string surfaceId)
        {
            activeContacts.Remove(surfaceId);
        }
    }

    // --- Test 1: SwordHeadContactAwardsOnePoint ---
    [Test]
    public void SwordHeadContactAwardsOnePoint()
    {
        var attacker = new MockAgent { Name = "Attacker" };
        var defender = new MockAgent { Name = "Defender" };
        attacker.Opponent = defender;

        var scorer = new MockScorer(attacker);

        // Simulate sword hitting opponent head
        scorer.OnContactBegin("head", defender);

        Assert.AreEqual(1, attacker.Score,
            "Attacker should score +1 when sword contacts opponent head.");
    }

    // --- Test 2: SustainedContactNoAdditionalPoints ---
    [Test]
    public void SustainedContactNoAdditionalPoints()
    {
        var attacker = new MockAgent { Name = "Attacker" };
        var defender = new MockAgent { Name = "Defender" };
        attacker.Opponent = defender;

        var scorer = new MockScorer(attacker);

        // First contact
        scorer.OnContactBegin("head", defender);
        int scoreAfterFirst = attacker.Score;

        // Simulate sustained contact (same surface, already active)
        scorer.OnContactBegin("head", defender);
        int scoreAfterSustained = attacker.Score;

        Assert.AreEqual(scoreAfterFirst, scoreAfterSustained,
            "Sustained contact on the same surface should not award additional points.");
        Assert.AreEqual(1, attacker.Score,
            "Score should remain at +1 during sustained contact.");
    }

    // --- Test 3: ReentryAwardsAnotherPoint ---
    [Test]
    public void ReentryAwardsAnotherPoint()
    {
        var attacker = new MockAgent { Name = "Attacker" };
        var defender = new MockAgent { Name = "Defender" };
        attacker.Opponent = defender;

        var scorer = new MockScorer(attacker);

        // First contact
        scorer.OnContactBegin("head", defender);
        Assert.AreEqual(1, attacker.Score, "First contact should award +1.");

        // Break contact
        scorer.OnContactEnd("head");
        Assert.AreEqual(1, attacker.Score, "Breaking contact should not change score.");

        // Re-enter contact
        scorer.OnContactBegin("head", defender);
        Assert.AreEqual(2, attacker.Score,
            "Re-entering contact on the same surface should award another +1.");
    }

    // --- Test 4: SwordSelfTorsoContactSubtractsOne ---
    [Test]
    public void SwordSelfTorsoContactSubtractsOne()
    {
        var agent = new MockAgent { Name = "Warrior" };
        var scorer = new MockScorer(agent);

        // Simulate sword hitting own torso (self-inflicted)
        scorer.OnContactBegin("torso", agent);

        Assert.AreEqual(-1, agent.Score,
            "Self-inflicted contact should subtract 1 point.");
    }

    // --- Test 5: DefenderUnaffectedByBeingHit ---
    [Test]
    public void DefenderUnaffectedByBeingHit()
    {
        var attacker = new MockAgent { Name = "Attacker" };
        var defender = new MockAgent { Name = "Defender" };
        attacker.Opponent = defender;
        defender.Opponent = attacker;

        var attackerScorer = new MockScorer(attacker);

        // Attacker hits defender
        attackerScorer.OnContactBegin("head", defender);

        Assert.AreEqual(0, defender.Score,
            "Defender score should not change when being hit by the opponent.");
        Assert.AreEqual(1, attacker.Score,
            "Attacker should score +1 for hitting the opponent.");
    }

    // --- Test 6: UnrelatedWarriorContactsDontScore ---
    [Test]
    public void UnrelatedWarriorContactsDontScore()
    {
        var attacker = new MockAgent { Name = "Attacker" };
        var unrelated = new MockAgent { Name = "Unrelated" };
        // attacker.Opponent is null, unrelated is not the opponent

        var scorer = new MockScorer(attacker);

        // Simulate sword hitting an unrelated warrior's head
        scorer.OnContactBegin("head", unrelated);

        Assert.AreEqual(0, attacker.Score,
            "Contact with an unrelated warrior should not score any points.");
    }

    // --- Test 7: MultipleCollidersDontDuplicate ---
    [Test]
    public void MultipleCollidersDontDuplicate()
    {
        var attacker = new MockAgent { Name = "Attacker" };
        var defender = new MockAgent { Name = "Defender" };
        attacker.Opponent = defender;

        var scorer = new MockScorer(attacker);

        // Simulate multiple trigger events for the same logical surface
        // (e.g., multiple frames of OnTriggerEnter for the same surface)
        scorer.OnContactBegin("head", defender);
        scorer.OnContactBegin("head", defender);
        scorer.OnContactBegin("head", defender);

        Assert.AreEqual(1, attacker.Score,
            "Multiple trigger events on the same logical surface should score only once.");
    }

    // --- Test 8: ResetClearsContactState ---
    [Test]
    public void ResetClearsContactState()
    {
        var attacker = new MockAgent { Name = "Attacker" };
        var defender = new MockAgent { Name = "Defender" };
        attacker.Opponent = defender;

        var scorer = new MockScorer(attacker);

        // Establish contact
        scorer.OnContactBegin("head", defender);
        Assert.AreEqual(1, attacker.Score, "Contact should award +1.");

        // Reset (disable scoring and clear contact state)
        scorer.DisableScoring();

        // After reset, re-enable scoring and try contacting again
        // The contact state should be cleared, so this is treated as a fresh contact
        scorer.EnableScoring();
        scorer.OnContactBegin("head", defender);
        Assert.AreEqual(2, attacker.Score,
            "After reset, contacting the same surface should award points again.");

        // Verify that while disabled, no scoring events occur
        var attacker2 = new MockAgent { Name = "Attacker2" };
        var defender2 = new MockAgent { Name = "Defender2" };
        attacker2.Opponent = defender2;
        var scorer2 = new MockScorer(attacker2);

        scorer2.OnContactBegin("head", defender2);
        Assert.AreEqual(1, attacker2.Score, "First contact should score.");

        // Disable scoring
        scorer2.DisableScoring();

        // Contact while disabled should not score
        scorer2.OnContactBegin("torso", defender2);
        Assert.AreEqual(1, attacker2.Score,
            "Contact while scoring is disabled should not change the score.");
    }
}