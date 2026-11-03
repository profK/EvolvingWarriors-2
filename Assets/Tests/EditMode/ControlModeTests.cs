using NUnit.Framework;
using UnityEngine;

public class ControlModeTests
{
    [Test]
    public void TestDirectBoneControlModeMLPDimensions()
    {
        var go = new GameObject("DirectWarrior");
        var agent = go.AddComponent<WarriorAgent>();
        var encoder = go.AddComponent<WarriorObservationEncoder>();
        var boneController = go.AddComponent<WarriorBoneController>();

        // Trigger Awake / initialization
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
        var agent = go.AddComponent<WarriorAgent>();
        var encoder = go.AddComponent<WarriorObservationEncoder>();
        var ikAdapter = go.AddComponent<IKWarriorMLPAdapter>();
        go.AddComponent<WarriorHandIKController>();

        agent.ControlMode = WarriorControlMode.HandIKControl;

        // Verify dimensions
        Assert.AreEqual(WarriorControlMode.HandIKControl, agent.ControlMode);
        Assert.AreEqual(encoder.InputDimension, agent.NeuralNetwork.InputSize);
        Assert.AreEqual(IKWarriorMLPAdapter.OutputDimension, agent.NeuralNetwork.OutputSize);
        Assert.AreEqual(6, agent.NeuralNetwork.OutputSize);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void TestIncompatibleGenomeRejected()
    {
        var directGO = new GameObject("DirectWarrior");
        var directAgent = directGO.AddComponent<WarriorAgent>();
        directGO.AddComponent<WarriorObservationEncoder>();
        directGO.AddComponent<WarriorBoneController>();

        var ikGO = new GameObject("IKWarrior");
        var ikAgent = ikGO.AddComponent<WarriorAgent>();
        ikGO.AddComponent<WarriorObservationEncoder>();
        ikGO.AddComponent<IKWarriorMLPAdapter>();
        ikGO.AddComponent<WarriorHandIKController>();
        ikAgent.ControlMode = WarriorControlMode.HandIKControl;

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
