using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor tool that creates the TrainingScene with all required components configured.
/// 
/// Run via: Tools → Evolving Warriors → Create Training Scene
/// 
/// The scene contains:
/// - An EvolutionSimulationManager with WarriorSpawner on a dedicated "TrainingManager" GameObject
/// - A ground plane for the arena floor
/// - A directional light and skybox
/// - The Warrior prefab referenced in the manager
/// </summary>
public class TrainingSceneBuilder : EditorWindow
{
    [MenuItem("Tools/Evolving Warriors/Create Training Scene")]
    public static void CreateTrainingScene()
    {
        // Open or create the scene
        EditorApplication.NewScene();
        
        // --- Ground Plane ---
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "ArenaGround";
        ground.transform.position = new Vector3(0, -0.5f, 0);
        ground.transform.localScale = new Vector3(30, 1, 30);
        
        // Make the ground dark gray
        Material groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundMat.color = new Color(0.15f, 0.15f, 0.15f);
        ground.GetComponent<MeshRenderer>().material = groundMat;
        
        // --- Arena Walls (visual boundaries) ---
        CreateArenaWall("NorthWall", 0, 1.5f, -15, 30, 3, 0.1f);
        CreateArenaWall("SouthWall", 0, 1.5f, 15, 30, 3, 0.1f);
        CreateArenaWall("EastWall", 15, 1.5f, 0, 0.1f, 3, 30);
        CreateArenaWall("WestWall", -15, 1.5f, 0, 0.1f, 3, 30);
        
        // --- Directional Light ---
        GameObject sun = new GameObject("DirectionalLight");
        sun.AddComponent<Light>().type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        Light dirLight = sun.GetComponent<Light>();
        dirLight.color = Color.white;
        dirLight.intensity = 1.5f;
        dirLight.shadows = LightShadows.Soft;
        
        // --- Ambient Light ---
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientIntensity = 0.4f;
        
        // --- Skybox ---
        RenderSettings.skybox = (Material)Resources.GetBuiltinResource(typeof(Material), "Skybox");
        
        // --- Training Manager ---
        GameObject manager = new GameObject("TrainingManager");
        
        // Find the Warrior prefab
        GameObject warriorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Warrior.prefab");
        if (warriorPrefab == null)
        {
            Debug.LogError("TrainingSceneBuilder: Could not find Warrior prefab at Assets/prefabs/Warrior.prefab");
            EditorUtility.DisplayDialog("Error", 
                "Warrior prefab not found at Assets/prefabs/Warrior.prefab.\n\n" +
                "Please ensure the prefab exists.", 
                "OK");
            return;
        }
        
        // Ensure the prefab has all required components
        // Open the prefab for editing, add missing components, then save
        string prefabPath = "Assets/prefabs/Warrior.prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        EnsureComponent<WarriorAgent>(prefabRoot);
        EnsureComponent<WarriorBoneController>(prefabRoot);
        EnsureComponent<WarriorObservationEncoder>(prefabRoot);
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);
        
        // Reload the prefab for the spawner reference
        warriorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        // Add EvolutionSimulationManager
        EvolutionSimulationManager simManager = manager.AddComponent<EvolutionSimulationManager>();
        
        // Add WarriorSpawner
        WarriorSpawner spawner = manager.AddComponent<WarriorSpawner>();
        
        // Configure via properties (bypassing serialized field access in editor)
        spawner.WarriorPrefab = warriorPrefab;
        spawner.PairCount = 4;
        spawner.PairSeparation = 1.5f;
        spawner.InterPairDistance = 6f;
        
        // Configure the simulation manager
        // Use reflection to set serialized fields (they're private)
        System.Type simType = typeof(EvolutionSimulationManager);
        
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
        SetField(simManager, "warriorPrefab", warriorPrefab);
        
        // --- Camera ---
        GameObject camera = new GameObject("OverheadCamera");
        camera.AddComponent<Camera>();
        // Add URP camera data if available
        Type urpCameraType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        if (urpCameraType != null)
        {
            camera.AddComponent(urpCameraType);
        }
        camera.transform.position = new Vector3(0, 25f, 0);
        camera.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
        
        // Mark as main camera
        camera.GetComponent<Camera>().tag = "MainCamera";
        
        // --- Save the scene ---
        string scenePath = "Assets/Scenes/TrainingScene.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"TrainingScene created at {scenePath}");
        Debug.Log("Configuration:");
        Debug.Log("- 4 warrior pairs (8 total)");
        Debug.Log("- 1.5m pair separation (sword-length)");
        Debug.Log("- 30s round duration");
        Debug.Log("- Genetic algorithm with tournament selection");
        Debug.Log("Press Play to start training!");
        
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("Training Scene Created", 
                $"TrainingScene saved to {scenePath}\n\n" +
                "Configuration:\n" +
                "- 4 warrior pairs (8 total)\n" +
                "- 1.5m pair separation (sword-length)\n" +
                "- 30s round duration\n" +
                "- Genetic algorithm with tournament selection\n\n" +
                "Press Play to start training!", 
                "OK");
        }
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
        FieldInfo field = type.GetField(fieldName, 
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(behaviour, value);
        }
        else
        {
            Debug.LogWarning($"TrainingSceneBuilder: Field '{fieldName}' not found on {type.Name}");
        }
    }
    
    static T EnsureComponent<T>(GameObject go) where T : UnityEngine.Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
        {
            component = go.AddComponent<T>();
            Debug.Log($"TrainingSceneBuilder: Added {typeof(T).Name} to {go.name}");
        }
        return component;
    }
}
