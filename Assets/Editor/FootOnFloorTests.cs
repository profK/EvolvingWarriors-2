using NUnit.Framework;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Edit-mode integration tests ensuring feet stay on the floor.
/// Covers TwoBoneIK ground clamping and ProceduralFootController state machine.
/// </summary>
public class FootOnFloorTests
{
    /// <summary>
    /// Creates a leg hierarchy matching TwoBoneIKTests pattern.
    /// Uses SetParent() and returns direct transform references to avoid Transform.Find() issues.
    /// Bone geometry: root at Y=1.0, hip at Y=1.3, knee at Y=1.6, ankle at Y=1.1.
    /// upperLegLen=0.3m, lowerLegLen=0.5m, maxReach=0.8m.
    /// </summary>
    GameObject CreateLegHierarchy(out Transform rootTfm, out Transform upperLeg, out Transform lowerLeg, out Transform foot)
    {
        var rootGO = new GameObject("TestRoot");
        rootTfm = rootGO.transform;
        rootTfm.position = new Vector3(0f, 1f, 0f);
        rootTfm.rotation = Quaternion.identity;

        upperLeg = new GameObject("UpperLeg").transform;
        upperLeg.SetParent(rootTfm);
        upperLeg.localPosition = Vector3.up * 0.3f;
        upperLeg.localRotation = Quaternion.identity;

        lowerLeg = new GameObject("LowerLeg").transform;
        lowerLeg.SetParent(upperLeg);
        lowerLeg.localPosition = Vector3.up * 0.3f;
        lowerLeg.localRotation = Quaternion.identity;

        foot = new GameObject("Foot").transform;
        foot.SetParent(lowerLeg);
        foot.localPosition = new Vector3(0f, -0.5f, 0f);
        foot.localRotation = Quaternion.identity;

        return rootGO;
    }

    [Test]
    public void TestFootStaysAtGroundY_WhenTargetIsOnGround()
    {
        // Hip at world Y=1.3. Target at same Y, 0.5m forward. Distance = 0.5m < maxReach 0.8m.
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis (forward).
        var rootGO = CreateLegHierarchy(out var rootTfm, out var upperLeg, out var lowerLeg, out var foot);
        Vector3 hipPos = upperLeg.position;
        Vector3 target = hipPos + new Vector3(0.01f, 0f, 0.5f);
        float groundY = 0f;
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLeg, lowerLeg, foot, groundY);
        Assert.IsTrue(result, "Solve should succeed");
        Assert.GreaterOrEqual(foot.position.y, groundY - 0.001f,
            $"Foot y={foot.position.y:F4} should be at or above groundY");
        Vector3 soleDir = foot.TransformDirection(Vector3.up);
        float soleDot = Vector3.Dot(soleDir, Vector3.up);
        Assert.Less(soleDot, 0f,
            $"Sole dot up = {soleDot:F3}. Expected negative (sole faces ground).");
        Object.DestroyImmediate(rootGO);
    }

    [Test]
    public void TestFootClampedToGroundY_WhenTargetBelowGround()
    {
        // Target below groundY, clamped to groundY=0. Distance > maxReach, graceful degradation.
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        var rootGO = CreateLegHierarchy(out var rootTfm, out var upperLeg, out var lowerLeg, out var foot);
        float groundY = 0f;
        Vector3 hipPos = upperLeg.position;
        Vector3 target = new Vector3(hipPos.x + 0.01f, groundY - 0.3f, hipPos.z + 0.5f);
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLeg, lowerLeg, foot, groundY);
        Assert.IsTrue(result, "Solve should succeed with ground clamping");
        Assert.GreaterOrEqual(foot.position.y, groundY - 0.001f,
            $"Foot y={foot.position.y:F4} should be clamped to groundY");
        Vector3 soleDir = foot.TransformDirection(Vector3.up);
        float soleDot = Vector3.Dot(soleDir, Vector3.up);
        Assert.Less(soleDot, 0f,
            $"Sole dot up = {soleDot:F3}. Expected negative (sole faces ground).");
        Object.DestroyImmediate(rootGO);
    }

    [Test]
    public void TestFootSoleFacesGround_AfterGroundClamping()
    {
        // Target below groundY, clamped. Sole should still face ground.
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        var rootGO = CreateLegHierarchy(out var rootTfm, out var upperLeg, out var lowerLeg, out var foot);
        float groundY = 0f;
        Vector3 hipPos = upperLeg.position;
        Vector3 target = new Vector3(hipPos.x + 0.01f, groundY - 0.5f, hipPos.z + 0.5f);
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLeg, lowerLeg, foot, groundY);
        Assert.IsTrue(result, "Solve should succeed with ground clamping");
        Vector3 soleWorldDir = foot.TransformDirection(Vector3.up);
        float soleDotUp = Vector3.Dot(soleWorldDir, Vector3.up);
        Assert.Less(soleDotUp, 0f,
            $"Sole dot up = {soleDotUp:F3}. Expected negative (sole faces ground).");
        Object.DestroyImmediate(rootGO);
    }

    [Test]
    public void TestFootControllerPlantedStateKeepsFootOnGround()
    {
        var footGO = new GameObject("Foot");
        footGO.transform.position = new Vector3(0f, 1f, 0f);
        var controller = footGO.AddComponent<ProceduralFootController>();
        controller.side = ProceduralFootController.FootSide.Left;
        controller.footTransform = footGO.transform;
        controller.upperLegTransform = new GameObject("UL").transform;
        controller.lowerLegTransform = new GameObject("LL").transform;
        controller.SetGroundY(0f);
        controller.Initialize();
        controller.UpdateStateWithDelta(0.02f);
        Vector3 pos = controller.GetFootWorldPosition();
        Assert.AreEqual(0f, pos.y, 0.001f, "Planted foot Y should equal groundY");
        Object.DestroyImmediate(footGO);
    }

    [Test]
    public void TestFootControllerStepCycleReturnsToGround()
    {
        var footGO = new GameObject("Foot");
        footGO.transform.position = new Vector3(0f, 1f, 0f);
        var controller = footGO.AddComponent<ProceduralFootController>();
        controller.side = ProceduralFootController.FootSide.Left;
        controller.footTransform = footGO.transform;
        controller.upperLegTransform = new GameObject("UL").transform;
        controller.lowerLegTransform = new GameObject("LL").transform;
        // Use defaults: arcLiftHeight=0.15f, stepPhaseDuration=0.4f
        controller.SetGroundY(0f);
        controller.Initialize();
        controller.RequestStep(new Vector3(0.6f, 0f, 0.6f));
        // Advance through Lifting phase (20 steps of 0.02s = 0.4s total)
        for (int i = 0; i < 20; i++) controller.UpdateStateWithDelta(0.02f);
        Assert.AreEqual(ProceduralFootController.FootState.Placing, controller.GetState());
        // Advance through Placing phase
        for (int i = 0; i < 20; i++) controller.UpdateStateWithDelta(0.02f);
        Assert.AreEqual(ProceduralFootController.FootState.Planted, controller.GetState());
        Vector3 pos = controller.GetFootWorldPosition();
        Assert.AreEqual(0f, pos.y, 0.001f,
            $"After step cycle, foot Y={pos.y:F4} should equal groundY=0f");
        Object.DestroyImmediate(footGO);
    }

    [Test]
    public void TestIKPipeline_FootTargetOnGroundYieldsFootOnGround()
    {
        var rootGO = CreateLegHierarchy(out var rootTfm, out var upperLeg, out var lowerLeg, out var foot);
        float groundY = 0f;
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLeg.position;
        Vector3 footTarget = hipPos + new Vector3(0.01f, 0f, 0.5f);
        bool result = TwoBoneIK.Solve(footTarget, rootTfm, upperLeg, lowerLeg, foot, groundY);
        Assert.IsTrue(result, "IK solve should succeed");
        Assert.GreaterOrEqual(foot.position.y, groundY - 0.001f,
            $"Foot y={foot.position.y:F4} should be at or above groundY after IK");
        Vector3 soleDir = foot.TransformDirection(Vector3.up);
        float soleDot = Vector3.Dot(soleDir, Vector3.up);
        Assert.Less(soleDot, 0f, "Sole should face ground after IK");
        Object.DestroyImmediate(rootGO);
    }

    [Test]
    public void TestIKPipeline_ElevatedGroundY()
    {
        var rootGO = CreateLegHierarchy(out var rootTfm, out var upperLeg, out var lowerLeg, out var foot);
        float groundY = 0.5f;
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLeg.position;
        Vector3 footTarget = hipPos + new Vector3(0.01f, 0f, 0.5f);
        bool result = TwoBoneIK.Solve(footTarget, rootTfm, upperLeg, lowerLeg, foot, groundY);
        Assert.IsTrue(result, "IK solve should succeed with elevated groundY");
        Assert.GreaterOrEqual(foot.position.y, groundY - 0.001f,
            $"Foot y={foot.position.y:F4} should be at or above groundY={groundY:F2}");
        Vector3 soleDir = foot.TransformDirection(Vector3.up);
        float soleDot = Vector3.Dot(soleDir, Vector3.up);
        Assert.Less(soleDot, 0f,
            $"Sole dot up = {soleDot:F3}. Expected negative (sole faces ground).");
        Object.DestroyImmediate(rootGO);
    }

    [Test]
    public void TestIKPipeline_BackwardTargetSoleFacesGround()
    {
        // When foot target is behind the character, bendNormal points UP.
        // The fix ensures soleDir always points DOWN, so sole faces ground regardless.
        var rootGO = CreateLegHierarchy(out var rootTfm, out var upperLeg, out var lowerLeg, out var foot);
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLeg.position;
        Vector3 footTarget = hipPos + new Vector3(0.01f, -0.3f, -0.5f);
        float groundY = 0f;
        bool result = TwoBoneIK.Solve(footTarget, rootTfm, upperLeg, lowerLeg, foot, groundY);
        Assert.IsTrue(result, "IK solve should succeed for backward targets");
        Assert.GreaterOrEqual(foot.position.y, groundY - 0.001f,
            $"Foot y={foot.position.y:F4} should be at or above groundY after IK");
        Vector3 soleDir = foot.TransformDirection(Vector3.up);
        float soleDot = Vector3.Dot(soleDir, Vector3.up);
        Assert.Less(soleDot, 0f,
            $"Sole dot up = {soleDot:F3} for backward target. Expected negative (sole faces ground).");
        Vector3 footFwd = foot.TransformDirection(Vector3.forward);
        float fwdDot = Vector3.Dot(footFwd.normalized, Vector3.forward);
        Assert.Greater(fwdDot, 0f,
            $"Foot forward dot Z = {fwdDot:F3} for backward target. Should be positive.");
        Object.DestroyImmediate(rootGO);
    }
}
