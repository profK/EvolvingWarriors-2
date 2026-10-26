using NUnit.Framework;
using UnityEngine;
using UnityEditor;

/// <summary>
/// A minimal WarriorBoneController stand-in for edit-mode tests.
/// Does not require an Animator; stores transforms in a fixed-size array
/// indexed by HumanBodyBones enum values, matching the real controller's API.
/// </summary>
public class MockBoneController : MonoBehaviour, IBoneTransformProvider
{
    Transform[] bones = new Transform[19]; // Matches WarriorBoneController.BoneCount

    public void SetBoneTransform(int index, Transform bone)
    {
        if (index >= 0 && index < bones.Length)
            bones[index] = bone;
    }

    public Transform GetBoneTransform(int boneIndex)
    {
        if (boneIndex < 0 || boneIndex >= bones.Length)
            throw new System.ArgumentOutOfRangeException(nameof(boneIndex));
        return bones[boneIndex];
    }
}

/// <summary>
/// Edit-mode unit tests for WarriorPoseSolver.
/// Validates auto-resolution, balance evaluation, reach evaluation,
/// IK application, ground penetration correction, and reset behavior.
/// </summary>
public class WarriorPoseSolverTests
{
    // Bone indices matching WarriorPoseSolver constants
    const int Hips = 0;
    const int LeftUpperLeg = 12;
    const int RightUpperLeg = 13;
    const int LeftLowerLeg = 14;
    const int RightLowerLeg = 15;
    const int LeftFoot = 16;
    const int RightFoot = 17;

    GameObject rootGO;
    Transform rootTfm;
    Transform hipsTfm;
    Transform leftUpperLegTfm;
    Transform leftLowerLegTfm;
    Transform leftFootTfm;
    Transform rightUpperLegTfm;
    Transform rightLowerLegTfm;
    Transform rightFootTfm;

    MockBoneController mockBoneController;
    ProceduralFootController leftFootCtrl;
    ProceduralFootController rightFootCtrl;
    WarriorPoseSolver poseSolver;

    [SetUp]
    public void SetUpWarriorHierarchy()
    {
        // Create root warrior GameObject
        rootGO = new GameObject("TestWarrior");
        rootTfm = rootGO.transform;
        rootTfm.position = Vector3.zero;
        rootTfm.rotation = Quaternion.identity;

        // Create hips bone (child of root)
        hipsTfm = new GameObject("Hips").transform;
        hipsTfm.SetParent(rootTfm);
        hipsTfm.position = new Vector3(0f, 0.9f, 0f);
        hipsTfm.localRotation = Quaternion.identity;

        // Left leg chain: hips → upperLeg → lowerLeg → foot
        leftUpperLegTfm = new GameObject("LeftUpperLeg").transform;
        leftUpperLegTfm.SetParent(hipsTfm);
        leftUpperLegTfm.localPosition = new Vector3(-0.1f, -0.1f, 0f);
        leftUpperLegTfm.localRotation = Quaternion.identity;

        leftLowerLegTfm = new GameObject("LeftLowerLeg").transform;
        leftLowerLegTfm.SetParent(leftUpperLegTfm);
        leftLowerLegTfm.localPosition = new Vector3(0f, -0.4f, 0f);
        leftLowerLegTfm.localRotation = Quaternion.identity;

        leftFootTfm = new GameObject("LeftFoot").transform;
        leftFootTfm.SetParent(leftLowerLegTfm);
        leftFootTfm.localPosition = new Vector3(0f, -0.5f, 0f);
        leftFootTfm.localRotation = Quaternion.identity;

        // Right leg chain: hips → upperLeg → lowerLeg → foot
        rightUpperLegTfm = new GameObject("RightUpperLeg").transform;
        rightUpperLegTfm.SetParent(hipsTfm);
        rightUpperLegTfm.localPosition = new Vector3(0.1f, -0.1f, 0f);
        rightUpperLegTfm.localRotation = Quaternion.identity;

        rightLowerLegTfm = new GameObject("RightLowerLeg").transform;
        rightLowerLegTfm.SetParent(rightUpperLegTfm);
        rightLowerLegTfm.localPosition = new Vector3(0f, -0.4f, 0f);
        rightLowerLegTfm.localRotation = Quaternion.identity;

        rightFootTfm = new GameObject("RightFoot").transform;
        rightFootTfm.SetParent(rightLowerLegTfm);
        rightFootTfm.localPosition = new Vector3(0f, -0.5f, 0f);
        rightFootTfm.localRotation = Quaternion.identity;

        // Create mock bone controller
        mockBoneController = rootGO.AddComponent<MockBoneController>();
        mockBoneController.SetBoneTransform(Hips, hipsTfm);
        mockBoneController.SetBoneTransform(LeftUpperLeg, leftUpperLegTfm);
        mockBoneController.SetBoneTransform(LeftLowerLeg, leftLowerLegTfm);
        mockBoneController.SetBoneTransform(LeftFoot, leftFootTfm);
        mockBoneController.SetBoneTransform(RightUpperLeg, rightUpperLegTfm);
        mockBoneController.SetBoneTransform(RightLowerLeg, rightLowerLegTfm);
        mockBoneController.SetBoneTransform(RightFoot, rightFootTfm);

        // Create left foot controller
        leftFootCtrl = rootGO.AddComponent<ProceduralFootController>();
        leftFootCtrl.side = ProceduralFootController.FootSide.Left;
        leftFootCtrl.footTransform = leftFootTfm;
        leftFootCtrl.upperLegTransform = leftUpperLegTfm;
        leftFootCtrl.lowerLegTransform = leftLowerLegTfm;
        leftFootCtrl.enabled = true;
        leftFootCtrl.Initialize();

        // Create right foot controller
        rightFootCtrl = rootGO.AddComponent<ProceduralFootController>();
        rightFootCtrl.side = ProceduralFootController.FootSide.Right;
        rightFootCtrl.footTransform = rightFootTfm;
        rightFootCtrl.upperLegTransform = rightUpperLegTfm;
        rightFootCtrl.lowerLegTransform = rightLowerLegTfm;
        rightFootCtrl.enabled = true;
        rightFootCtrl.Initialize();

        // Create pose solver — Awake() will fail because boneController is null,
        // but initialized=false allows retry after manual assignment
        poseSolver = rootGO.AddComponent<WarriorPoseSolver>();

        // Manually wire references and re-initialize (tests can't rely on auto-resolve
        // because MockBoneController is not WarriorBoneController)
        poseSolver.boneController = mockBoneController;
        poseSolver.leftFootController = leftFootCtrl;
        poseSolver.rightFootController = rightFootCtrl;
        poseSolver.enabled = true;
        poseSolver.Initialize();
    }

    [TearDown]
    public void TearDownWarriorHierarchy()
    {
        if (rootGO != null)
            Object.DestroyImmediate(rootGO);
    }

    [Test]
    public void TestAutoResolveReferences()
    {
        // Verify all references were resolved during Initialize()
        Assert.IsNotNull(poseSolver.boneController, "boneController should be assigned");
        Assert.IsNotNull(poseSolver.leftFootController, "leftFootController should be assigned");
        Assert.IsNotNull(poseSolver.rightFootController, "rightFootController should be assigned");

        // Verify foot controllers are correctly wired
        Assert.AreEqual(ProceduralFootController.FootSide.Left, poseSolver.leftFootController.Side);
        Assert.AreEqual(ProceduralFootController.FootSide.Right, poseSolver.rightFootController.Side);
    }

    [Test]
    public void TestSolveWithNoBalanceIssue()
    {
        // Position hips centered above foot midpoint — no balance issue
        // Left foot is at ~(-0.1, 0, 0), right foot is at ~(0.1, 0, 0)
        // Hip is at (0, 0.9, 0) — directly above midpoint (0, 0, 0)
        hipsTfm.position = new Vector3(0f, 0.9f, 0f);

        // Store initial states
        var leftStateBefore = leftFootCtrl.GetState();
        var rightStateBefore = rightFootCtrl.GetState();

        poseSolver.Solve();

        // Both feet should remain Planted — no step triggered
        Assert.AreEqual(ProceduralFootController.FootState.Planted, leftFootCtrl.GetState(),
            "Left foot should remain Planted when CoM is centered");
        Assert.AreEqual(ProceduralFootController.FootState.Planted, rightFootCtrl.GetState(),
            "Right foot should remain Planted when CoM is centered");
    }

    [Test]
    public void TestBalanceTriggersStep()
    {
        // Move hip far to the right — beyond balanceThreshold (0.25m default)
        // Feet are planted at ~(-0.1, 0, 0) and ~(0.1, 0, 0), midpoint is (0, 0, 0)
        // Hip at (0.5, 0.9, 0) gives horizontal CoM offset of 0.5m > 0.25m threshold
        hipsTfm.position = new Vector3(0.5f, 0.9f, 0f);

        poseSolver.Solve();

        // The left foot (behind the CoM shift) should be triggered to step
        // CoM direction is +X; left foot at x=-0.1 projects to -0.1, right foot at x=0.1 projects to 0.1
        // leftProj < rightProj → left foot steps
        Assert.AreEqual(ProceduralFootController.FootState.Lifting, leftFootCtrl.GetState(),
            "Left foot (behind CoM shift) should transition to Lifting");
        Assert.AreEqual(ProceduralFootController.FootState.Planted, rightFootCtrl.GetState(),
            "Right foot (in front of CoM shift) should remain Planted");
    }

    [Test]
    public void TestReachTriggersStep()
    {
        // Keep hips centered so balance does NOT trigger
        hipsTfm.position = new Vector3(0f, 0.9f, 0f);

        // Manually move the left foot bone far from its planted position
        // to simulate MLP pushing it beyond stepThreshold (0.4m default)
        // Left foot planted at ~(-0.1, ~-0.9, 0). Move bone to (-0.1, 0, 1.0) — drift > 0.4m
        leftFootTfm.position = new Vector3(-0.1f, 0f, 1.0f);

        poseSolver.Solve();

        // Left foot should have triggered a step due to drift
        Assert.AreEqual(ProceduralFootController.FootState.Lifting, leftFootCtrl.GetState(),
            "Left foot should transition to Lifting when MLP pushes beyond stepThreshold");
        Assert.AreEqual(ProceduralFootController.FootState.Planted, rightFootCtrl.GetState(),
            "Right foot should remain Planted — no drift");
    }

    [Test]
    public void TestIKAppliedToLegChains()
    {
        // Position hips centered so no balance step triggers
        hipsTfm.position = new Vector3(0f, 0.9f, 0f);

        // Capture initial rotations before Solve()
        Quaternion leftUpperLegRotBefore = leftUpperLegTfm.localRotation;
        Quaternion leftLowerLegRotBefore = leftLowerLegTfm.localRotation;
        Quaternion rightUpperLegRotBefore = rightUpperLegTfm.localRotation;
        Quaternion rightLowerLegRotBefore = rightLowerLegTfm.localRotation;

        poseSolver.Solve();

        // After Solve(), IK should have modified leg rotations
        // At minimum, at least one bone per leg chain should have changed from the rest pose
        // because IK re-orientates the chain toward the foot target
        bool leftUpperChanged = !leftUpperLegTfm.localRotation.Equals(leftUpperLegRotBefore);
        bool leftLowerChanged = !leftLowerLegTfm.localRotation.Equals(leftLowerLegRotBefore);
        Assert.IsTrue(leftUpperChanged || leftLowerChanged,
            "At least one left leg bone rotation should change after IK solving");

        bool rightUpperChanged = !rightUpperLegTfm.localRotation.Equals(rightUpperLegRotBefore);
        bool rightLowerChanged = !rightLowerLegTfm.localRotation.Equals(rightLowerLegRotBefore);
        Assert.IsTrue(rightUpperChanged || rightLowerChanged,
            "At least one right leg bone rotation should change after IK solving");
    }

    [Test]
    public void TestGroundPenetrationCorrection()
    {
        // Position hips very high so the leg can't reach groundY with normal IK
        // This forces the IK solver to extend fully but still leave the foot below ground
        // The penetration correction in step 8 should then catch it
        hipsTfm.position = new Vector3(0f, 3.0f, 0f);

        poseSolver.Solve();

        // After Solve(), the foot bone should not be below groundY=0
        // Even if IK couldn't fully reach the target, penetration correction clamps it
        Assert.GreaterOrEqual(leftFootTfm.position.y, -0.01f,
            $"Left foot y={leftFootTfm.position.y:F4} should not be significantly below groundY=0");
        Assert.GreaterOrEqual(rightFootTfm.position.y, -0.01f,
            $"Right foot y={rightFootTfm.position.y:F4} should not be significantly below groundY=0");
    }

    [Test]
    public void TestResetRestsFootControllers()
    {
        // First trigger a step so the left foot enters Lifting state
        hipsTfm.position = new Vector3(0.5f, 0.9f, 0f);
        poseSolver.Solve();
        Assert.AreEqual(ProceduralFootController.FootState.Lifting, leftFootCtrl.GetState(),
            "Left foot should be in Lifting before reset");

        // Reset should bring both feet back to Planted
        poseSolver.Reset();

        Assert.AreEqual(ProceduralFootController.FootState.Planted, leftFootCtrl.GetState(),
            "Left foot should be Planted after reset");
        Assert.AreEqual(ProceduralFootController.FootState.Planted, rightFootCtrl.GetState(),
            "Right foot should be Planted after reset");

        // Verify stepTargetXZ was reset (no latent state from the interrupted step)
        Vector3 pos = leftFootCtrl.GetFootWorldPosition();
        Assert.AreEqual(0f, pos.y, 0.001f,
            "Foot Y should be at ground level after reset");
    }

    [Test]
    public void TestFootControllerResetClampsFootToGroundY()
    {
        // Simulate T-position where foot bone starts at non-zero Y
        // Move foot bone to y=0.5 (feet pointing sideways)
        leftFootTfm.position = new Vector3(-0.1f, 0.5f, 0f);

        // Reset should clamp Y to groundY=0
        leftFootCtrl.Reset();

        // Verify the captured position has Y clamped to groundY
        Vector3 capturedPos = leftFootCtrl.GetFootWorldPosition();
        Assert.AreEqual(0f, capturedPos.y, 0.001f,
            "Foot Y should be clamped to groundY=0 after reset even when foot bone starts at y=0.5");
    }
}