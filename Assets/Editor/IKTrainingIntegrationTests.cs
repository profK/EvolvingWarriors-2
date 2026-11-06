using NUnit.Framework;
using System.IO;
using UnityEngine;

public class IKTrainingIntegrationTests
{
    [Test]
    public void TestBuildIKPrefabAndScene()
    {
        // Build or update IK prefab and scene
        GameObject ikPrefab = IKTrainingSceneBuilder.CreateOrUpdateIKPrefab();
        Assert.IsNotNull(ikPrefab, "WarriorIK prefab should be created.");

        var agent = ikPrefab.GetComponent<WarriorAgent>();
        Assert.IsNotNull(agent, "WarriorIK must have WarriorAgent.");
        Assert.AreEqual(WarriorControlMode.HandIKControl, agent.ControlMode, "WarriorIK ControlMode must be HandIKControl.");

        var ikController = ikPrefab.GetComponent<WarriorHandIKController>();
        Assert.IsNotNull(ikController, "WarriorIK must have WarriorHandIKController.");

        var ikAdapter = ikPrefab.GetComponent<IKWarriorMLPAdapter>();
        Assert.IsNotNull(ikAdapter, "WarriorIK must have IKWarriorMLPAdapter.");

        // Build scene
        IKTrainingSceneBuilder.CreateIKTrainingScene();
        Assert.IsTrue(File.Exists(IKTrainingSceneBuilder.IKScenePath), "IK Training scene file must exist.");
        Assert.IsTrue(File.Exists(IKTrainingSceneBuilder.ScoringRulesAssetPath), "ScoringRules asset must exist.");
    }
}
