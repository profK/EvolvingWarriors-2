using NUnit.Framework;
using UnityEngine;

public class ControlModeTests
{
    [Test]
    public void TestDirectBoneControlModeMLPDimensions()
    {
        var go = new GameObject("DirectWarrior");

        // Add support components first so WarriorAgent.Awake() auto-resolves them.
        var encoder = go.AddComponent<WarriorObservationEncoder>();
        go.AddComponent<WarriorBoneController>();

        // Add WarriorAgent last; its Awake() runs immediately and resolves references.
        var agent = go.AddComponent<WarriorAgent>();

        // DirectBoneControl mode (default)
        Assert.AreEqual(WarriorControlMode.DirectBoneControl, agent.ControlMode);
        Assert.AreEqual(encoder.InputDimension, agent.NeuralNetwork.InputSize);
        Assert.AreEqual(WarriorBoneController.OutputDimension, agent.NeuralNetwork.OutputSize);
        Assert.AreEqual(57, agent.NeuralNetwork.OutputSize);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void TestHandIKControlModeMLPDimensions()
    {
        var go = new GameObject("IKWarrior");

        // Add all required IK components first so WarriorAgent.Awake() can auto-detect them.
        go.AddComponent<WarriorObservationEncoder>();
        go.AddComponent<IKWarriorMLPAdapter>();
        go.AddComponent<WarriorHandIKController>();

        // Add WarriorAgent last; its Awake() runs immediately and will see the
        // IK adapter already present on the same GameObject, triggering auto-detection
        // to HandIKControl mode (handIKAdapter != null && boneController == null).
        var agent = go.AddComponent<WarriorAgent>();

        // Verify dimensions
        Assert.AreEqual(WarriorControlMode.HandIKControl, agent.ControlMode);
        Assert.AreEqual(go.GetComponent<WarriorObservationEncoder>().InputDimension, agent.NeuralNetwork.InputSize);
        Assert.AreEqual(IKWarriorMLPAdapter.OutputDimension, agent.NeuralNetwork.OutputSize);
        Assert.AreEqual(6, agent.NeuralNetwork.OutputSize);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void TestIncompatibleGenomeRejected()
    {
        // Direct warrior: WarriorAgent + bone controller (no IK components)
        var directGO = new GameObject("DirectWarrior");
        directGO.AddComponent<WarriorObservationEncoder>();
        directGO.AddComponent<WarriorBoneController>();
        var directAgent = directGO.AddComponent<WarriorAgent>();

        // IK warrior: IK components first, then WarriorAgent so auto-detection works
        var ikGO = new GameObject("IKWarrior");
        ikGO.AddComponent<WarriorObservationEncoder>();
        ikGO.AddComponent<IKWarriorMLPAdapter>();
        ikGO.AddComponent<WarriorHandIKController>();
        var ikAgent = ikGO.AddComponent<WarriorAgent>();

        Genome directGenome = directAgent.Genome;
        Genome ikGenome = ikAgent.Genome;

        Assert.AreNotEqual(directGenome.Size, ikGenome.Size);

        // Attempting to install IK genome into Direct agent must throw ArgumentException
        Assert.Throws<System.ArgumentException>(() => directAgent.InstallGenome(ikGenome));

        // Attempting to install Direct genome into IK agent must throw ArgumentException
        Assert.Throws<System.ArgumentException>(() => ikAgent.InstallGenome(directGenome));

        Object.DestroyImmediate(directGO);
        Object.DestroyImmediate(ikGO);
    }
}
