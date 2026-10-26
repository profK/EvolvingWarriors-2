using NUnit.Framework;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Edit-mode unit tests for the TwoBoneIK static solver.
/// Validates IK accuracy, bone length preservation, knee angle clamping,
/// graceful degradation, and NaN/Infinity guards.
/// </summary>
public class TwoBoneIKTests
{
    GameObject rootGO;
    Transform rootTfm;
    Transform upperLegTfm;
    Transform lowerLegTfm;
    Transform footTfm;
    float restUpperLegLen;
    float restLowerLegLen;

    [SetUp]
    public void SetUpHierarchy()
    {
        rootGO = new GameObject("TestRoot");
        rootTfm = rootGO.transform;
        rootTfm.position = new Vector3(0f, 1f, 0f);
        rootTfm.rotation = Quaternion.identity;

        // Transform hierarchy: root (hips) → upperLeg (hip joint) → lowerLeg (knee) → foot (ankle)
        // Bone lengths: upperLegLen = 0.3m (hip to knee), lowerLegLen = 0.5m (knee to ankle)
        // maxReach = 0.8m
        //
        // upperLeg.localPosition defines the hip joint offset from root (the hips bone).
        // lowerLeg.localPosition defines the knee joint offset from upperLeg (the hip).
        // foot.localPosition defines the ankle offset from lowerLeg (the knee).

        upperLegTfm = new GameObject("UpperLeg").transform;
        upperLegTfm.SetParent(rootTfm);
        upperLegTfm.localPosition = Vector3.up * 0.3f;  // Hip joint 0.3m above root
        upperLegTfm.localRotation = Quaternion.identity;

        lowerLegTfm = new GameObject("LowerLeg").transform;
        lowerLegTfm.SetParent(upperLegTfm);
        lowerLegTfm.localPosition = Vector3.up * 0.3f;  // Knee 0.3m from hip → upperLegLen = 0.3m
        lowerLegTfm.localRotation = Quaternion.identity;

        footTfm = new GameObject("Foot").transform;
        footTfm.SetParent(lowerLegTfm);
        footTfm.localPosition = new Vector3(0f, -0.5f, 0f);  // Ankle 0.5m from knee → lowerLegLen = 0.5m
        footTfm.localRotation = Quaternion.identity;

        restUpperLegLen = (lowerLegTfm.position - upperLegTfm.position).magnitude;
        restLowerLegLen = (footTfm.position - lowerLegTfm.position).magnitude;
    }

    [TearDown]
    public void TearDownHierarchy()
    {
        if (rootGO != null)
            Object.DestroyImmediate(rootGO);
    }

    [Test]
    public void TestBasicIKAccuracy()
    {
        // hipPos is at root(0,1,0) + upperLeg localPos(0,0.3,0) = (0, 1.3, 0)
        // Target at (0, 0, 0.5) — distance from hip ≈ 1.39m in world space, but the solver
        // works in world space. Distance from hip to target is sqrt(0 + 1.3^2 + 0.5^2) ≈ 1.39m.
        // This is beyond maxReach (0.8m), so we need a closer target.
        // Target at (0.01, 1.3, 0.6) — distance from hip ≈ 0.6m, within 0.8m maxReach.
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, 0f, 0.6f);  // 0.6m forward from hip
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsTrue(result, "Solve should return true for a reachable target");
        Vector3 footWorldPos = footTfm.position;
        float error = (footWorldPos - target).magnitude;
        Assert.LessOrEqual(error, 0.05f,
            $"Foot {footWorldPos} should be within 0.05m of target {target}, error={error:F4}");
    }

    [Test]
    public void TestBoneLengthPreservation()
    {
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, 0f, 0.6f);  // 0.6m forward from hip, within reach
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsTrue(result, "Solve should succeed");
        float newUpperLegLen = (lowerLegTfm.position - upperLegTfm.position).magnitude;
        float newLowerLegLen = (footTfm.position - lowerLegTfm.position).magnitude;
        Assert.That(newUpperLegLen, Is.EqualTo(restUpperLegLen).Within(0.001f),
            $"Upper leg length changed from {restUpperLegLen:F4} to {newUpperLegLen:F4}");
        Assert.That(newLowerLegLen, Is.EqualTo(restLowerLegLen).Within(0.001f),
            $"Lower leg length changed from {restLowerLegLen:F4} to {newLowerLegLen:F4}");
    }

    [Test]
    public void TestKneeAngleClamping()
    {
        // With unequal bone lengths (upperLegLen=0.3, lowerLegLen=0.5), a target very close
        // to the hip produces cosAngle far below -1, clamped to -1.0, giving kneeAngle = acos(-1) = 180°.
        // This WILL trigger the >160° clamp path in TwoBoneIK.Solve().
        // cosAngle = (0.09 + 0.0001 - 0.25) / (2 * 0.3 * 0.01) = -26.65 → clamped to -1.0
        // kneeAngle = acos(-1) = 180° > 160° → clamped to 160°
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, 0f, 0.01f);
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);

        // The solver should succeed with the clamped angle
        Assert.IsTrue(result, "Solve should succeed even when knee angle is clamped");

        // Verify bone lengths are preserved after clamping
        float newUpperLegLen = (lowerLegTfm.position - upperLegTfm.position).magnitude;
        float newLowerLegLen = (footTfm.position - lowerLegTfm.position).magnitude;
        Assert.That(newUpperLegLen, Is.EqualTo(restUpperLegLen).Within(0.001f),
            $"Upper leg length changed from {restUpperLegLen:F4} to {newUpperLegLen:F4}");
        Assert.That(newLowerLegLen, Is.EqualTo(restLowerLegLen).Within(0.001f),
            $"Lower leg length changed from {restLowerLegLen:F4} to {newLowerLegLen:F4}");

        // Verify knee bend angle is within anatomical limits (≤ 160°)
        Vector3 hipToKnee = lowerLegTfm.position - upperLegTfm.position;
        Vector3 kneeToFoot = footTfm.position - lowerLegTfm.position;
        if (hipToKnee.sqrMagnitude > 0.0001f && kneeToFoot.sqrMagnitude > 0.0001f)
        {
            float kneeAngle = Vector3.Angle(hipToKnee, kneeToFoot);
            // The internal angle at the knee. For a straight leg, this is ~180°.
            // The bend angle is 180° - kneeAngle. Max bend is 160°, so min knee angle is 20°.
            Assert.GreaterOrEqual(kneeAngle, 180f - 160f,
                $"Knee angle {kneeAngle:F1}° is below minimum (bend exceeds 160°)");
            Assert.LessOrEqual(kneeAngle, 180f,
                $"Knee angle {kneeAngle:F1}° exceeds 180°");
        }
    }

    [Test]
    public void TestBeyondReach()
    {
        // With unequal bone lengths (0.3/0.5), maxReach is ~0.8m.
        // Target at 1.5m from hip is clearly beyond reach.
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, 0f, 1.5f);  // 1.5m forward from hip
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsTrue(result, "Solve should return true for beyond-reach targets (graceful degradation)");
        float maxReach = restUpperLegLen + restLowerLegLen;
        float actualReach = (footTfm.position - hipPos).magnitude;
        Assert.LessOrEqual(actualReach, maxReach + 0.01f,
            $"Foot reach {actualReach:F4} exceeds maxReach {maxReach:F4}");
        float newUpperLegLen = (lowerLegTfm.position - upperLegTfm.position).magnitude;
        float newLowerLegLen = (footTfm.position - lowerLegTfm.position).magnitude;
        Assert.That(newUpperLegLen, Is.EqualTo(restUpperLegLen).Within(0.001f));
        Assert.That(newLowerLegLen, Is.EqualTo(restLowerLegLen).Within(0.001f));
    }

    [Test]
    public void TestZeroDistance()
    {
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos;
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsFalse(result, "Solve should return false for zero-distance target");
    }

    [Test]
    public void TestNaNGuard()
    {
        Vector3 target = new Vector3(float.NaN, 0f, 0.6f);
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsFalse(result, "Solve should return false when target contains NaN");
    }

    [Test]
    public void TestGroundClamping()
    {
        // This test verifies BOTH ground clamping AND beyond-reach graceful degradation.
        // hipPos is at (0, 1.3, 0). Target offset (0.01, -1.31, 0.1) gives world Y = -0.01,
        // which is below groundY=0, forcing the clamp to execute.
        // After the clamp, target.y becomes 0. Distance from hip ≈ 1.32m, beyond maxReach (0.8m).
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, -1.31f, 0.1f);  // World Y = -0.01, below ground
        float groundY = 0f;
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, groundY);
        Assert.IsTrue(result, "Solve should succeed with below-ground target (ground clamp + beyond-reach)");
        Vector3 footWorldPos = footTfm.position;
        Assert.GreaterOrEqual(footWorldPos.y, groundY,
            $"Foot y={footWorldPos.y:F4} should not be below groundY={groundY:F4}");
    }

    [Test]
    public void TestInfinityGuard()
    {
        Vector3 infTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
        bool result = TwoBoneIK.Solve(infTarget, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsFalse(result, "Solve should return false for Infinity inputs");
    }

    [Test]
    public void TestFootSoleFacesGround()
    {
        // The foot bone's local Y-axis (up) maps to the sole direction.
        // After TwoBoneIK.Solve(), the foot rotation is set so the sole faces ground.
        // TwoBoneIK projects the character forward onto the plane perpendicular to
        // soleDir (always DOWN), then uses LookRotation(footForward, soleDir).
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, 0f, 0.6f);
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsTrue(result, "Solve should succeed for a reachable target");

        // The foot's local Y-axis (up) should point DOWN in world space after IK
        Vector3 soleWorldDir = footTfm.TransformDirection(Vector3.up);
        float soleDotUp = Vector3.Dot(soleWorldDir, Vector3.up);
        Assert.Less(soleDotUp, 0f,
            $"Foot sole dot up = {soleDotUp:F3}. Expected negative (sole faces ground). " +
            "Positive means sole faces sky (180 bug).");

        // Foot forward should point roughly along character forward (+Z)
        // The footForward vector is tilted to stay perpendicular to bendNormal, so its Z
        // component is positive but may be < 0.5 for steep leg angles.
        Vector3 footFwdWorld = footTfm.TransformDirection(Vector3.forward);
        float fwdDotZ = Vector3.Dot(footFwdWorld.normalized, Vector3.forward);
        Assert.Greater(fwdDotZ, 0f,
            $"Foot forward dot Z = {fwdDotZ:F3}. Should be positive (foot faces forward).");
    }

    [Test]
    public void TestFootSoleFacesGroundWithHumanoidRestPose()
    {
        // Simulates the actual character's foot bone rest pose where local Y points DOWN.
        // In Kevin Iglesias' HumanCharacterDummy_M, the foot bone has local Y pointing DOWN
        // through the sole (standard Unity humanoid convention).
        // Set the foot's rest localRotation to match: Quaternion.Euler(180, 0, 0)
        footTfm.localRotation = Quaternion.Euler(180f, 0f, 0f);

        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, 0f, 0.6f);
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsTrue(result, "Solve should succeed with humanoid foot rest pose");

        // After IK, the foot's local Y-axis still maps to the sole direction.
        // TwoBoneIK sets foot.localRotation so that local Y maps to soleDir (always DOWN).
        // The foot's rest rotation (180,0,0) is overwritten by the IK solution, so the
        // sole direction after IK depends on the IK-computed rotation, not the rest pose.
        Vector3 soleWorldDir = footTfm.TransformDirection(Vector3.up);
        float soleDotUp = Vector3.Dot(soleWorldDir, Vector3.up);
        Assert.Less(soleDotUp, 0f,
            $"Foot sole dot up = {soleDotUp:F3}. Expected negative (sole faces ground) " +
            "even with humanoid rest pose (180,0,0).");

        // Verify foot forward points in a positive direction (roughly character forward)
        Vector3 footFwdWorld = footTfm.TransformDirection(Vector3.forward);
        float fwdDotZ = Vector3.Dot(footFwdWorld.normalized, Vector3.forward);
        Assert.Greater(fwdDotZ, 0f,
            $"Foot forward dot Z = {fwdDotZ:F3}. Should be positive (foot faces forward).");
    }
    [Test]
    public void TestFootSoleFacesGroundBackwardTarget()
    {
        // When the foot target is behind the character, bendNormal points UP (Y positive).
        // The fix signs bendNormal so soleDir always points DOWN, regardless of target direction.
        // Small X offset (0.01) avoids degenerate case where dir is parallel to bendAxis.
        // This ensures the sole faces ground even for backward foot placements.
        Vector3 hipPos = upperLegTfm.position;
        Vector3 target = hipPos + new Vector3(0.01f, -0.3f, -0.5f);  // Behind and below hip
        bool result = TwoBoneIK.Solve(target, rootTfm, upperLegTfm, lowerLegTfm, footTfm, 0f);
        Assert.IsTrue(result, "Solve should succeed for backward targets");

        // Sole must still face ground for backward targets
        Vector3 soleWorldDir = footTfm.TransformDirection(Vector3.up);
        float soleDotUp = Vector3.Dot(soleWorldDir, Vector3.up);
        Assert.Less(soleDotUp, 0f,
            $"Foot sole dot up = {soleDotUp:F3} for backward target. Expected negative (sole faces ground)." + 
            " Positive means sole faces sky.");

        // Foot forward should still face character forward (+Z) even when placed behind
        Vector3 footFwdWorld = footTfm.TransformDirection(Vector3.forward);
        float fwdDotZ = Vector3.Dot(footFwdWorld.normalized, Vector3.forward);
        Assert.Greater(fwdDotZ, 0f,
            $"Foot forward dot Z = {fwdDotZ:F3} for backward target. Should be positive (foot faces forward)." + 
            " Negative means foot faces backward.");
    }

}
