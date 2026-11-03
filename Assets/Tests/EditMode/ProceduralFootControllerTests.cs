using NUnit.Framework;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Edit-mode unit tests for ProceduralFootController.
/// Validates state machine transitions, arc trajectories, threshold checks,
/// planted position locking, and null guard behavior.
/// </summary>
public class ProceduralFootControllerTests
{
    GameObject testGO;
    ProceduralFootController controller;

    Transform footTfm;
    Transform upperLegTfm;
    Transform lowerLegTfm;

    [SetUp]
    public void SetUpController()
    {
        testGO = new GameObject("TestFootController");

        // Create transform hierarchy for foot, upper leg, lower leg
        footTfm = new GameObject("Foot").transform;
        footTfm.SetParent(testGO.transform);
        footTfm.position = new Vector3(0f, 0f, 0f);
        footTfm.localRotation = Quaternion.identity;

        upperLegTfm = new GameObject("UpperLeg").transform;
        upperLegTfm.SetParent(testGO.transform);
        upperLegTfm.position = new Vector3(0f, 0.3f, 0f);
        upperLegTfm.localRotation = Quaternion.identity;

        lowerLegTfm = new GameObject("LowerLeg").transform;
        lowerLegTfm.SetParent(testGO.transform);
        lowerLegTfm.position = new Vector3(0f, 0.15f, 0f);
        lowerLegTfm.localRotation = Quaternion.identity;

        // Add the controller — Awake() calls Initialize() which fails null checks,
        // sets enabled=false, but does NOT set initialized=true (allowing retry)
        controller = testGO.AddComponent<ProceduralFootController>();
        controller.side = ProceduralFootController.FootSide.Left;

        // Assign transforms AFTER AddComponent (Awake already ran with nulls)
        controller.footTransform = footTfm;
        controller.upperLegTransform = upperLegTfm;
        controller.lowerLegTransform = lowerLegTfm;

        // Re-enable the component and re-initialize — now transforms are set
        controller.enabled = true;
        controller.Initialize(); // Passes null checks, sets initialized=true

        controller.SetGroundY(0f);
    }

    [TearDown]
    public void TearDownController()
    {
        if (testGO != null)
            Object.DestroyImmediate(testGO);
    }

    [Test]
    public void TestAwakeInitialization()
    {
        // Verify state is Planted after Awake
        Assert.AreEqual(ProceduralFootController.FootState.Planted, controller.GetState(),
            "State should be Planted after Awake");

        // Verify foot position was captured
        Vector3 pos = controller.GetFootWorldPosition();
        Assert.AreEqual(0f, pos.x, 0.001f, "X should match initial foot position");
        Assert.AreEqual(0f, pos.z, 0.001f, "Z should match initial foot position");
    }

    [Test]
    public void TestPlantedStateHoldsXZ()
    {
        // Call UpdateStateWithDelta multiple times — X,Z should never change
        // Use deterministic delta time instead of Time.fixedDeltaTime
        Vector3 initialPos = controller.GetFootWorldPosition();

        for (int i = 0; i < 10; i++)
        {
            controller.UpdateStateWithDelta(0.02f);
            Vector3 currentPos = controller.GetFootWorldPosition();
            Assert.AreEqual(initialPos.x, currentPos.x, 0.001f,
                $"X drifted on iteration {i}");
            Assert.AreEqual(initialPos.z, currentPos.z, 0.001f,
                $"Z drifted on iteration {i}");
        }
    }

    [Test]
    public void TestPlantedStateClampY()
    {
        // Set groundY to a non-zero value and verify Y matches
        // Use UpdateStateWithDelta for deterministic behavior in edit mode
        controller.SetGroundY(2.5f);
        controller.UpdateStateWithDelta(0.02f);

        Vector3 pos = controller.GetFootWorldPosition();
        Assert.AreEqual(2.5f, pos.y, 0.001f,
            $"Foot Y {pos.y:F4} should equal groundY 2.5f in Planted state");
    }

    [Test]
    public void TestRequestStepAccepted()
    {
        // Request a step with distance >= minStepDistance (default 0.05m)
        Vector3 target = new Vector3(0f, 0f, 0.5f);  // 0.5m forward
        bool accepted = controller.RequestStep(target);

        Assert.IsTrue(accepted, "Step should be accepted for distance >= minStepDistance");
        Assert.AreEqual(ProceduralFootController.FootState.Lifting, controller.GetState(),
            "State should transition to Lifting");
    }

    [Test]
    public void TestRequestStepRejectedMicroStep()
    {
        // Request a step with distance < minStepDistance (default 0.05m)
        Vector3 target = new Vector3(0f, 0f, 0.02f);  // 0.02m forward — too short
        bool accepted = controller.RequestStep(target);

        Assert.IsFalse(accepted, "Step should be rejected for distance < minStepDistance");
        Assert.AreEqual(ProceduralFootController.FootState.Planted, controller.GetState(),
            "State should remain Planted");
    }

    [Test]
    public void TestRequestStepRejectedFromLifting()
    {
        // First enter Lifting state
        Vector3 firstTarget = new Vector3(0f, 0f, 0.5f);
        bool firstAccepted = controller.RequestStep(firstTarget);
        Assert.IsTrue(firstAccepted, "First step should be accepted");

        // Now try to request another step while in Lifting
        Vector3 secondTarget = new Vector3(0f, 0f, 1.0f);
        bool secondAccepted = controller.RequestStep(secondTarget);

        Assert.IsFalse(secondAccepted, "Step should be rejected from Lifting state");
        Assert.AreEqual(ProceduralFootController.FootState.Lifting, controller.GetState(),
            "State should still be Lifting");
    }

    [Test]
    public void TestFullStepCycle()
    {
        Vector3 target = new Vector3(0f, 0f, 0.6f);
        bool accepted = controller.RequestStep(target);
        Assert.IsTrue(accepted, "Step should be accepted");

        // Drive through Lifting phase (StepPhaseDuration default is 0.4s)
        // Advance in small increments to simulate FixedUpdate calls
        float phaseDuration = controller.StepPhaseDuration;
        float delta = phaseDuration / 10f;

        for (int i = 0; i < 10; i++)
        {
            controller.UpdateStateWithDelta(delta);
        }

        // After completing Lifting phase, should be in Placing
        Assert.AreEqual(ProceduralFootController.FootState.Placing, controller.GetState(),
            "State should be Placing after Lifting phase completes");

        // Drive through Placing phase
        for (int i = 0; i < 10; i++)
        {
            controller.UpdateStateWithDelta(delta);
        }

        // After completing Placing phase, should be back to Planted
        Assert.AreEqual(ProceduralFootController.FootState.Planted, controller.GetState(),
            "State should be Planted after Placing phase completes");

        // Verify plantedPosition updated to the new target
        Vector3 finalPos = controller.GetFootWorldPosition();
        Assert.AreEqual(target.x, finalPos.x, 0.001f,
            $"Planted X {finalPos.x:F4} should equal target X {target.x:F4}");
        Assert.AreEqual(target.z, finalPos.z, 0.001f,
            $"Planted Z {finalPos.z:F4} should equal target Z {target.z:F4}");
        Assert.AreEqual(0f, finalPos.y, 0.001f,
            $"Planted Y {finalPos.y:F4} should equal groundY");
    }

    [Test]
    public void TestArcTrajectory()
    {
        Vector3 target = new Vector3(0f, 0f, 0.6f);
        controller.RequestStep(target);

        // Advance to exactly t=0.5 (midpoint of Lifting phase)
        float phaseDuration = controller.StepPhaseDuration;
        float halfDuration = phaseDuration * 0.5f;

        // Use UpdateStateWithDelta to jump directly to midpoint
        controller.UpdateStateWithDelta(halfDuration);

        Vector3 midPos = controller.GetFootWorldPosition();

        // At t=0.5, Sin(0.5 * PI) = 1.0, so Y should be groundY + arcLiftHeight
        float expectedY = 0f + controller.ArcLiftHeight;
        Assert.AreEqual(expectedY, midPos.y, 0.001f,
            $"Foot Y {midPos.y:F4} at midpoint should equal groundY + arcLiftHeight {expectedY:F4}");

        // X,Z should be halfway between planted and target
        Assert.AreEqual(0f, midPos.x, 0.001f, "X should be halfway (both start and target are 0)");
        Assert.AreEqual(0.3f, midPos.z, 0.001f,
            "Z should be halfway between 0 and 0.6");
    }

    [Test]
    public void TestNullFootTransform()
    {
        // Create a controller without assigning footTransform — should disable itself
        GameObject badGO = new GameObject("BadFootController");
        var badController = badGO.AddComponent<ProceduralFootController>();
        // Leave footTransform null — don't assign it

        // After Awake runs, the component should be disabled
        Assert.IsFalse(badController.enabled,
            "Controller should disable itself when footTransform is null");

        // Calling methods on a disabled controller should be safe
        bool result = badController.RequestStep(Vector3.zero);
        Assert.IsFalse(result, "RequestStep should return false when disabled");

        // Cleanup
        Object.DestroyImmediate(badGO);
    }
}