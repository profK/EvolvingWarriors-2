using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor tool that creates the WarriorIK prefab variant and the IK Training scene.
/// 
/// Run via: Tools → Evolving Warriors → Create IK Training Scene
/// </summary>
public class IKTrainingSceneBuilder : EditorWindow
{
    public const string OriginalPrefabPath = "Assets/prefabs/Warrior.prefab";
    public const string IKPrefabPath = "Assets/prefabs/WarriorIK.prefab";
    public const string IKScenePath = "Assets/Scenes/IK Training.unity";

    [MenuItem("Tools/Evolving Warriors/Create IK Training Scene")]
    public static void CreateIKTrainingScene()
    {
        GameObject ikPrefab = CreateOrUpdateIKPrefab();
        if (ikPrefab == null)
        {
            Debug.LogError("IKTrainingSceneBuilder: Failed to create or load WarriorIK prefab.");
            return;
        }

        // Open or duplicate TrainingScene or create new scene
        string trainingScenePath = "Assets/Scenes/TrainingScene.unity";
        if (System.IO.File.Exists(trainingScenePath))
        {
            // Copy training scene to IK Training scene
            System.IO.File.Copy(trainingScenePath, IKScenePath, true);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(IKScenePath, OpenSceneMode.Single);
            
            // Find TrainingManager or EvolutionSimulationManager
            var simManager = UnityEngine.Object.FindObjectOfType<EvolutionSimulationManager>();
            var spawner = UnityEngine.Object.FindObjectOfType<WarriorSpawner>();

            if (simManager != null)
            {
                SetField(simManager, "warriorPrefab", ikPrefab);
                EditorUtility.SetDirty(simManager);
            }

            if (spawner != null)
            {
                spawner.WarriorPrefab = ikPrefab;
                EditorUtility.SetDirty(spawner);
            }

            EditorSceneManager.SaveScene(scene, IKScenePath);
        }
        else
        {
            // Build from scratch using TrainingSceneBuilder style
            EditorApplication.NewScene();
            BuildIKSceneDirectly(ikPrefab);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"IK Training Scene created successfully at {IKScenePath}");
    }

    public static GameObject CreateOrUpdateIKPrefab()
    {
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalPrefabPath);
        if (basePrefab == null)
        {
            Debug.LogError($"IKTrainingSceneBuilder: Base warrior prefab not found at {OriginalPrefabPath}");
            return null;
        }

        // Instantiate base prefab to create variant or configure
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        try
        {
            var agent = instance.GetComponent<WarriorAgent>();
            if (agent == null) agent = instance.AddComponent<WarriorAgent>();
            agent.ControlMode = WarriorControlMode.HandIKControl;

            var ikController = instance.GetComponent<WarriorHandIKController>();
            if (ikController == null) ikController = instance.AddComponent<WarriorHandIKController>();

            var ikAdapter = instance.GetComponent<IKWarriorMLPAdapter>();
            if (ikAdapter == null) ikAdapter = instance.AddComponent<IKWarriorMLPAdapter>();

            // Save as Prefab Variant
            GameObject ikPrefab = PrefabUtility.SaveAsPrefabAsset(instance, IKPrefabPath);
            Debug.Log($"WarriorIK prefab saved to {IKPrefabPath}");
            return ikPrefab;
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private static void BuildIKSceneDirectly(GameObject ikPrefab)
    {
        // Ground Plane
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "ArenaGround";
        ground.transform.position = new Vector3(0, -0.5f, 0);
        ground.transform.localScale = new Vector3(30, 1, 30);

        Material groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundMat.color = new Color(0.15f, 0.15f, 0.15f);
        ground.GetComponent<MeshRenderer>().material = groundMat;

        // Arena Walls
        CreateArenaWall("NorthWall", 0, 1.5f, -15, 30, 3, 0.1f);
        CreateArenaWall("SouthWall", 0, 1.5f, 15, 30, 3, 0.1f);
        CreateArenaWall("EastWall", 15, 1.5f, 0, 0.1f, 3, 30);
        CreateArenaWall("WestWall", -15, 1.5f, 0, 0.1f, 3, 30);

        // Directional Light
        GameObject sun = new GameObject("DirectionalLight");
        sun.AddComponent<Light>().type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        Light dirLight = sun.GetComponent<Light>();
        dirLight.color = Color.white;
        dirLight.intensity = 1.5f;
        dirLight.shadows = LightShadows.Soft;

        // Training Manager
        GameObject manager = new GameObject("TrainingManager");
        EvolutionSimulationManager simManager = manager.AddComponent<EvolutionSimulationManager>();
        WarriorSpawner spawner = manager.AddComponent<WarriorSpawner>();

        spawner.WarriorPrefab = ikPrefab;
        spawner.PairCount = 4;
        spawner.PairSeparation = 1.5f;
        spawner.InterPairDistance = 6f;

        SetField(simManager, "pairCount", 4);
        SetField(simManager, "pairSeparation", 1.5f);
        SetField(simManager, "interPairDistance", 6f);
        SetField(simManager, "roundDuration", 30f);
        SetField(simManager, "maxAngularSpeed", 180f);
        SetField(simManager, "randomSeed", -1L);
        SetField(simManager, "eliteFraction", 0.10f);
        SetField(simManager, "tournamentSize", 3);
        SetField(simManager, "crossoverProbability", 0.70f);
        SetField(simManager, "mutationProbability", 0.02f);
        SetField(simManager, "mutationStrength", 0.10f);
        SetField(simManager, "warriorPrefab", ikPrefab);

        // Camera
        GameObject camera = new GameObject("OverheadCamera");
        camera.AddComponent<Camera>();
        Type urpCameraType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        if (urpCameraType != null) camera.AddComponent(urpCameraType);
        camera.AddComponent<FirstPersonCameraController>();
        camera.transform.position = new Vector3(0, 25f, 0);
        camera.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
        camera.GetComponent<Camera>().tag = "MainCamera";

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), IKScenePath);
    }

    static void CreateArenaWall(string name, float x, float y, float z, float sx, float sy, float sz)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = new Vector3(x, y, z);
        wall.transform.localScale = new Vector3(sx, sy, sz);

        Material wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wallMat.color = new Color(0.3f, 0.3f, 0.3f);
        wall.GetComponent<MeshRenderer>().material = wallMat;
    }

    static void SetField<T>(MonoBehaviour behaviour, string fieldName, T value)
    {
        Type type = behaviour.GetType();
        FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(behaviour, value);
        }
    }
}
