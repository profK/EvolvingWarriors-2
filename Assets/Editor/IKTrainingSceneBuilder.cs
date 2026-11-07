using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor tool that creates the WarriorIK prefab variant and the IK Training scene
/// entirely from scratch. No dependency on an existing TrainingScene file.
/// 
/// Builds:
///   1. ScoringRules ScriptableObject asset
///   2. WarriorIK prefab variant (based on Warrior.prefab, with IK control mode)
///   3. Full scene: arena, lighting, TrainingManager, HUD, cameras
/// 
/// Run via: Tools &rarr; Evolving Warriors &rarr; Create IK Training Scene
/// </summary>
public class IKTrainingSceneBuilder : EditorWindow
{
    public const string OriginalPrefabPath = "Assets/prefabs/Warrior.prefab";
    public const string IKPrefabPath = "Assets/prefabs/WarriorIK.prefab";
    public const string IKScenePath = "Assets/Scenes/IK Training.unity";
    public const string ScoringRulesAssetPath = "Assets/Components/Combat/ScoringRules.asset";

    [MenuItem("Tools/Evolving Warriors/Create IK Training Scene")]
    public static void CreateIKTrainingScene()
    {
        // --- 1. Create ScoringRules asset ---
        ScoringRules scoringRules = CreateScoringRulesAsset();
        if (scoringRules == null)
        {
            Debug.LogError("IKTrainingSceneBuilder: Failed to create ScoringRules asset.");
            return;
        }

        // --- 2. Create or update IK prefab ---
        GameObject ikPrefab = CreateOrUpdateIKPrefab();
        if (ikPrefab == null)
        {
            Debug.LogError("IKTrainingSceneBuilder: Failed to create or load WarriorIK prefab.");
            return;
        }

        // --- 3. Build the scene from scratch ---
        EditorApplication.NewScene();
        BuildCompleteIKScene(ikPrefab, scoringRules);

        AssetDatabase.SaveAssets();
        Debug.Log($"[IK Training] Scene created successfully at {IKScenePath}");

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "IK Training Scene Ready",
                $"Scene saved to {IKScenePath}\n\n" +
                "Includes:\n" +
                "- WarriorIK prefab (Hand IK control mode)\n" +
                "- Arena with ground and walls\n" +
                "- EvolutionSimulationManager (4 pairs, 30s rounds)\n" +
                "- Live HUD with generation, scores, pause\n" +
                "- Overhead main camera + PiP camera\n\n" +
                "Press Play to start IK training!",
                "OK");
        }
    }

    // ======================== ScoringRules Asset ========================

    static ScoringRules CreateScoringRulesAsset()
    {
        // Load existing or create new
        ScoringRules rules = AssetDatabase.LoadAssetAtPath<ScoringRules>(ScoringRulesAssetPath);
        if (rules != null)
            return rules;

        rules = ScriptableObject.CreateInstance<ScoringRules>();
        AssetDatabase.CreateAsset(rules, ScoringRulesAssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[IK Training] Created ScoringRules asset at {ScoringRulesAssetPath}");
        return rules;
    }

    // ======================== IK Prefab ========================

    public static GameObject CreateOrUpdateIKPrefab()
    {
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalPrefabPath);
        if (basePrefab == null)
        {
            Debug.LogError($"IKTrainingSceneBuilder: Base warrior prefab not found at {OriginalPrefabPath}");
            return null;
        }

        // Check if the IK prefab already exists — load it to inspect
        GameObject existingIK = AssetDatabase.LoadAssetAtPath<GameObject>(IKPrefabPath);

        // Instantiate base prefab to create variant
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

            // Save as Prefab Variant (preserves link to base prefab for shared mesh/materials)
            GameObject ikPrefab = PrefabUtility.SaveAsPrefabAsset(instance, IKPrefabPath);
            Debug.Log($"[IK Training] WarriorIK prefab saved to {IKPrefabPath}");
            return ikPrefab;
        }
        finally
        {
            DestroyImmediate(instance, false);
        }
    }

    // ======================== Scene Building ========================

    static void BuildCompleteIKScene(GameObject ikPrefab, ScoringRules scoringRules)
    {
        BuildArena();
        BuildLighting();
        BuildTrainingManager(ikPrefab, scoringRules);
        BuildHUD();
        BuildMainCamera();
        BuildPiPCamera();

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), IKScenePath);
    }

    // --- Arena ---

    static void BuildArena()
    {
        // Ground plane — Y=0 to match WarriorPoseSolver.groundY default
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "ArenaGround";
        ground.transform.position = new Vector3(0, 0f, 0);
        ground.transform.localScale = new Vector3(30, 1, 30);

        Material groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundMat.color = new Color(0.15f, 0.15f, 0.15f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

        // Arena walls (visual boundaries)
        CreateArenaWall("NorthWall", 0f, 1.5f, -15f, 30f, 3f, 0.1f);
        CreateArenaWall("SouthWall", 0f, 1.5f, 15f, 30f, 3f, 0.1f);
        CreateArenaWall("EastWall", 15f, 1.5f, 0f, 0.1f, 3f, 30f);
        CreateArenaWall("WestWall", -15f, 1.5f, 0f, 0.1f, 3f, 30f);
    }

    static void CreateArenaWall(string name, float x, float y, float z, float sx, float sy, float sz)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = new Vector3(x, y, z);
        wall.transform.localScale = new Vector3(sx, sy, sz);

        Material wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wallMat.color = new Color(0.3f, 0.3f, 0.3f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
    }

    // --- Lighting ---

    static void BuildLighting()
    {
        // Directional light
        GameObject sun = new GameObject("DirectionalLight");
        Light dirLight = sun.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        dirLight.color = Color.white;
        dirLight.intensity = 1.5f;
        dirLight.shadows = LightShadows.Soft;

        // Ambient
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientIntensity = 0.4f;
        RenderSettings.skybox = (Material)Resources.GetBuiltinResource(typeof(Material), "Skybox");
    }

    // --- Training Manager ---

    static void BuildTrainingManager(GameObject ikPrefab, ScoringRules scoringRules)
    {
        GameObject manager = new GameObject("TrainingManager");

        // EvolutionSimulationManager — use reflection for all private fields
        EvolutionSimulationManager simManager = manager.AddComponent<EvolutionSimulationManager>();
        SetField(simManager, "warriorPrefab", ikPrefab);

        // Configure all remaining private fields via reflection
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
        SetField(simManager, "scoringRules", scoringRules);

        // WarriorSpawner
        WarriorSpawner spawner = manager.AddComponent<WarriorSpawner>();
        spawner.WarriorPrefab = ikPrefab;
        spawner.PairCount = 4;
        spawner.PairSeparation = 1.5f;
        spawner.InterPairDistance = 6f;

        // EvolutionCheckpointManager
        var checkpointManager = manager.AddComponent<EvolutionCheckpointManager>();
        // The checkpoint manager needs a reference to the simulation manager; set via field
        SetField(checkpointManager, "simulationManager", simManager);
    }

    // --- HUD ---

    static void BuildHUD()
    {
        // Find the TrainingManager so we can wire the HUD's simulationManager reference
        GameObject trainingManager = GameObject.Find("TrainingManager");
        if (trainingManager == null)
        {
            Debug.LogError("[IK Training] Cannot build HUD: TrainingManager not found.");
            return;
        }
        var simManager = trainingManager.GetComponent<EvolutionSimulationManager>();

        // Canvas
        GameObject hudCanvas = new GameObject("EvolutionHUDCanvas");
        Canvas canvas = hudCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.AddComponent<CanvasScaler>();
        hudCanvas.AddComponent<GraphicRaycaster>();
        hudCanvas.AddComponent<CanvasGroup>();

        EvolutionHUD hud = hudCanvas.AddComponent<EvolutionHUD>();
        hud.simulationManager = simManager;

        // Single top bar panel
        GameObject singleBar = new GameObject("SingleBar");
        singleBar.transform.SetParent(hudCanvas.transform, false);

        var barImg = singleBar.AddComponent<Image>();
        barImg.color = new Color(0f, 0f, 0f, 0.7f);

        var barRT = singleBar.GetComponent<RectTransform>();
        if (barRT == null)
            barRT = singleBar.AddComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0f, 1f);
        barRT.anchorMax = new Vector2(1f, 1f);
        barRT.sizeDelta = new Vector2(0f, 40f);
        barRT.anchoredPosition = new Vector2(0f, 50f);

        // Text elements in the top bar
        hud.generationText = CreateAnchoredTextWithWidth(singleBar, "GenerationText", "Generation: 0", 18, TextAlignmentOptions.MidlineLeft, 10f, -300f);
        hud.timeRemainingText = CreateAnchoredTextWithWidth(singleBar, "TimeText", "Time: --", 18, TextAlignmentOptions.MidlineLeft, -290f, -170f);
        hud.stateText = CreateAnchoredTextWithWidth(singleBar, "StateText", "State: --", 18, TextAlignmentOptions.MidlineLeft, -160f, -100f);

        // Best score (mid-screen)
        hud.bestScoreText = CreateAnchoredTextWithWidth(hudCanvas, "BestScoreText", "Best Score: --", 16, TextAlignmentOptions.MidlineLeft, 10f, -200f);
        RectTransform bestRT = hud.bestScoreText.GetComponent<RectTransform>();
        bestRT.anchorMin = new Vector2(0f, 0.5f);
        bestRT.anchorMax = new Vector2(0f, 0.5f);
        bestRT.anchoredPosition = new Vector2(10f, 80f);

        // Average score (mid-screen)
        hud.averageScoreText = CreateAnchoredTextWithWidth(hudCanvas, "AvgScoreText", "Average Score: --", 16, TextAlignmentOptions.MidlineLeft, 10f, -200f);
        RectTransform avgRT = hud.averageScoreText.GetComponent<RectTransform>();
        avgRT.anchorMin = new Vector2(0f, 0.5f);
        avgRT.anchorMax = new Vector2(0f, 0.5f);
        avgRT.anchoredPosition = new Vector2(10f, 55f);

        // Worst score (mid-screen)
        hud.worstScoreText = CreateAnchoredTextWithWidth(hudCanvas, "WorstScoreText", "Worst: --", 16, TextAlignmentOptions.MidlineLeft, 10f, -200f);
        RectTransform worstRT = hud.worstScoreText.GetComponent<RectTransform>();
        worstRT.anchorMin = new Vector2(0f, 0.5f);
        worstRT.anchorMax = new Vector2(0f, 0.5f);
        worstRT.anchoredPosition = new Vector2(10f, 30f);

        // Pairs info
        hud.pairsText = CreateAnchoredTextWithWidth(hudCanvas, "PairsText", "Pairs: 4", 14, TextAlignmentOptions.MidlineLeft, 10f, -160f);
        RectTransform pairsRT = hud.pairsText.GetComponent<RectTransform>();
        pairsRT.anchorMin = new Vector2(0f, 0.5f);
        pairsRT.anchorMax = new Vector2(0f, 0.5f);
        pairsRT.anchoredPosition = new Vector2(10f, 5f);
        pairsRT.GetComponent<TextMeshProUGUI>().fontSize = 14;

        // Population info
        hud.populationText = CreateAnchoredTextWithWidth(hudCanvas, "PopulationText", "Population: 8", 14, TextAlignmentOptions.MidlineLeft, 10f, -180f);
        RectTransform popRT = hud.populationText.GetComponent<RectTransform>();
        popRT.anchorMin = new Vector2(0f, 0.5f);
        popRT.anchorMax = new Vector2(0f, 0.5f);
        popRT.anchoredPosition = new Vector2(10f, -20f);
        popRT.GetComponent<TextMeshProUGUI>().fontSize = 14;

        // Previous generation text (lower area)
        hud.previousGenText = CreateAnchoredTextWithWidth(hudCanvas, "PrevGenText", "", 14, TextAlignmentOptions.MidlineLeft, 10f, -300f);
        RectTransform prevRT = hud.previousGenText.GetComponent<RectTransform>();
        prevRT.anchorMin = new Vector2(0f, 0.5f);
        prevRT.anchorMax = new Vector2(0f, 0.5f);
        prevRT.anchoredPosition = new Vector2(10f, -50f);
        prevRT.GetComponent<TextMeshProUGUI>().fontSize = 14;

        // Pause button (right side of top bar)
        GameObject pauseBtn = CreateAnchoredButton(singleBar, "PauseButton");
        hud.pauseButton = pauseBtn.GetComponent<Button>();
        TextMeshProUGUI btnText = pauseBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
            btnText.text = "Pause";
        hud.pauseButtonText = btnText;

        // Reset button (below pause button on right side)
        GameObject resetBtn = CreateFreeButton(hudCanvas, "ResetButton");
        hud.resetButton = resetBtn.GetComponent<Button>();
        TextMeshProUGUI resetText = resetBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (resetText != null)
            resetText.text = "Reset Pose";

        EditorUtility.SetDirty(hudCanvas);
        Debug.Log("[IK Training] HUD Canvas created and wired.");
    }

    // --- Main Camera ---

    static void BuildMainCamera()
    {
        GameObject camera = new GameObject("OverheadCamera");
        Camera cam = camera.AddComponent<Camera>();
        cam.tag = "MainCamera";

        // Add URP camera data if available
        Type urpCameraType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        if (urpCameraType != null)
            camera.AddComponent(urpCameraType);

        // Add first-person camera controller for WASD movement and mouse look
        camera.AddComponent<FirstPersonCameraController>();

        // Overhead position looking down at arena
        camera.transform.position = new Vector3(0f, 25f, 0f);
        camera.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
    }

    // --- PiP Camera ---

    static void BuildPiPCamera()
    {
        GameObject trainingManager = GameObject.Find("TrainingManager");
        if (trainingManager == null)
        {
            Debug.LogWarning("[IK Training] Skipping PiP camera: TrainingManager not found.");
            return;
        }

        Canvas hudCanvas = FindObjectOfType<Canvas>();
        if (hudCanvas == null)
        {
            Debug.LogWarning("[IK Training] Skipping PiP camera: HUD Canvas not found.");
            return;
        }

        // PiP Camera GameObject
        GameObject pipCameraGO = new GameObject("PiPCamera");
        Camera pipCam = pipCameraGO.AddComponent<Camera>();
        pipCam.clearFlags = CameraClearFlags.SolidColor;
        pipCam.backgroundColor = new Color(0f, 0.1f, 0.15f, 1f);
        pipCam.cullingMask = LayerMask.GetMask("Default");

        // Add URP camera data if available
        Type urpCameraType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        if (urpCameraType != null)
            pipCameraGO.AddComponent(urpCameraType);

        // Position: three-quarter angle
        pipCameraGO.transform.position = new Vector3(0f, 8f, -8f);
        pipCameraGO.transform.LookAt(Vector3.zero);

        // RenderTexture
        RenderTexture pipRT = new RenderTexture(384, 216, 16);
        AssetDatabase.CreateAsset(pipRT, "Assets/PiPRenderTexture.asset");
        AssetDatabase.SaveAssets();
        pipCam.targetTexture = pipRT;

        // PiP overlay UI on HUD canvas (top-right corner, 25% of reference width)
        float referenceWidth = 1920f;
        float pipWidth = referenceWidth * 0.25f;
        float pipHeight = pipWidth * 9f / 16f;
        float margin = 16f;

        GameObject pipUI = new GameObject("PiPOverlay");
        pipUI.transform.SetParent(hudCanvas.transform, false);

        RectTransform overlayRect = pipUI.GetComponent<RectTransform>();
        if (overlayRect == null)
            overlayRect = pipUI.AddComponent<RectTransform>();
        overlayRect.anchorMin = new Vector2(1f, 1f);
        overlayRect.anchorMax = new Vector2(1f, 1f);
        overlayRect.pivot = new Vector2(1f, 1f);
        overlayRect.anchoredPosition = new Vector2(margin, margin);
        overlayRect.sizeDelta = new Vector2(pipWidth, pipHeight);

        // Background panel (dark border)
        GameObject pipPanel = new GameObject("PiPBackground");
        pipPanel.transform.SetParent(pipUI.transform, false);
        var panelImg = pipPanel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.9f);

        RectTransform panelRect = pipPanel.GetComponent<RectTransform>();
        if (panelRect == null)
            panelRect = pipPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;

        // RawImage for the PiP feed (inset 2px for border)
        GameObject rawImageGO = new GameObject("PiPRawImage");
        rawImageGO.transform.SetParent(pipPanel.transform, false);
        var rawImage = rawImageGO.AddComponent<RawImage>();
        rawImage.texture = pipRT;

        RectTransform rawRect = rawImageGO.GetComponent<RectTransform>();
        if (rawRect == null)
            rawRect = rawImageGO.AddComponent<RectTransform>();
        rawRect.anchorMin = Vector2.zero;
        rawRect.anchorMax = Vector2.one;
        rawRect.offsetMin = new Vector2(2f, 2f);
        rawRect.offsetMax = new Vector2(2f, 2f);

        // Add and wire PiPCameraController
        var pipController = trainingManager.AddComponent<PiPCameraController>();
        pipController.PiPCamera = pipCam;
        pipController.RenderTexture = pipRT;
        pipController.PiPRawImage = rawImage;
        pipController.SimulationManager = trainingManager.GetComponent<EvolutionSimulationManager>();

        Selection.activeGameObject = pipCameraGO;
        Debug.Log("[IK Training] PiP camera created (top-right corner overlay).");
    }

    // ======================== UI Helpers ========================

    static TextMeshProUGUI CreateAnchoredTextWithWidth(GameObject parent, string name, string text, int fontSize, TextAlignmentOptions alignment, float leftOffset, float rightOffset)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null)
            rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = new Vector2(leftOffset, 0f);
        rt.offsetMax = new Vector2(rightOffset, 0f);

        return tmp;
    }

    static GameObject CreateAnchoredButton(GameObject parent, string name)
    {
        GameObject btn = new GameObject(name);
        btn.transform.SetParent(parent.transform, false);

        btn.AddComponent<Button>();
        var img = btn.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 0.2f);

        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt == null)
            rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-75f, 0f);
        rt.sizeDelta = new Vector2(130f, 30f);

        // Button text
        var textGo = new GameObject("ButtonText");
        textGo.transform.SetParent(btn.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Pause";
        tmp.fontSize = 16;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        var textRT = textGo.GetComponent<RectTransform>();
        if (textRT == null)
            textRT = textGo.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.anchoredPosition = Vector2.zero;
        textRT.sizeDelta = Vector2.zero;

        return btn;
    }

    /// <summary>Creates a free-positioned button (not anchored to a parent panel).</summary>
    static GameObject CreateFreeButton(GameObject parent, string name)
    {
        GameObject btn = new GameObject(name);
        btn.transform.SetParent(parent.transform, false);

        btn.AddComponent<Button>();
        var img = btn.AddComponent<Image>();
        img.color = new Color(0.6f, 0.3f, 0.2f, 0.8f);

        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt == null)
            rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-75f, -40f);
        rt.sizeDelta = new Vector2(130f, 30f);

        // Button text
        var textGo = new GameObject("ButtonText");
        textGo.transform.SetParent(btn.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Reset";
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        var textRT = textGo.GetComponent<RectTransform>();
        if (textRT == null)
            textRT = textGo.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.anchoredPosition = Vector2.zero;
        textRT.sizeDelta = Vector2.zero;

        return btn;
    }

    // ======================== Reflection Helpers ========================

    static void SetField<T>(MonoBehaviour behaviour, string fieldName, T value)
    {
        Type type = behaviour.GetType();
        FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(behaviour, value);
        }
        else
        {
            Debug.LogWarning($"IKTrainingSceneBuilder: Field '{fieldName}' not found on {type.Name}");
        }
    }
}
