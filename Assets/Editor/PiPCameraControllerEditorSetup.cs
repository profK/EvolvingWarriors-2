using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor tool that auto-configures the PiPCameraController and its UI overlay in the scene.
/// Run via: Tools → Evolving Warriors → Setup PiP Camera
/// 
/// Creates:
/// - A secondary Camera for the PiP view (three-quarter angle)
/// - A RenderTexture for the PiP camera target
/// - A UI RawImage on the existing HUD Canvas to display the PiP
/// - Wires the PiPCameraController references
/// </summary>
public class PiPCameraControllerEditorSetup : EditorWindow
{
    [MenuItem("Tools/Evolving Warriors/Setup PiP Camera")]
    public static void SetupPiPCamera()
    {
        // Find the EvolutionSimulationManager or TrainingManager
        GameObject trainingManager = GameObject.Find("TrainingManager");
        if (trainingManager == null)
        {
            var manager = FindObjectOfType<EvolutionSimulationManager>();
            if (manager != null)
                trainingManager = manager.gameObject;
            else
            {
                EditorUtility.DisplayDialog("PiP Camera Setup",
                    "No TrainingManager or EvolutionSimulationManager found in scene.\n\nPlease set up the training scene first.",
                    "OK");
                return;
            }
        }

        // Check if PiPCameraController already exists
        var existingPiP = FindObjectOfType<PiPCameraController>();
        if (existingPiP != null)
        {
            if (!EditorUtility.DisplayDialog("PiP Camera Setup",
                "A PiPCameraController already exists in the scene.\n\nDelete it and try again?",
                "OK", "Cancel"))
            {
                return;
            }
            UnityEngine.Object.DestroyImmediate(existingPiP);
        }

        // 1. Create the PiP Camera
        GameObject pipCameraGO = new GameObject("PiPCamera");
        pipCameraGO.AddComponent<Camera>();

        // Position it above the arena
        pipCameraGO.transform.position = new Vector3(0f, 8f, -8f);
        pipCameraGO.transform.LookAt(Vector3.zero);

        // 2. Create the RenderTexture
        RenderTexture pipRenderTexture = new RenderTexture(384, 216, 16);
        AssetDatabase.CreateAsset(pipRenderTexture, "Assets/PiPRenderTexture.asset");
        AssetDatabase.SaveAssets();

        // Assign it to the camera
        pipCameraGO.GetComponent<Camera>().targetTexture = pipRenderTexture;
        pipCameraGO.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        pipCameraGO.GetComponent<Camera>().backgroundColor = new Color(0f, 0.1f, 0.15f, 1f);

        // 3. Find or create the HUD Canvas
        Canvas hudCanvas = FindObjectOfType<Canvas>();
        if (hudCanvas == null)
        {
            EditorUtility.DisplayDialog("PiP Camera Setup",
                "No Canvas found in scene.\n\nPlease run 'Setup HUD & Checkpoints' first.",
                "OK");
            return;
        }

        // 4. Create the PiP UI overlay hierarchy on the HUD Canvas.
        //
        // Design (consistent with HUDSetupEditor.cs anchored positioning):
        //   PiPOverlay — anchored to top-right corner of canvas. Size is a fraction
        //               of the CanvasScaler's reference resolution (default 1920×1080)
        //               so the CanvasScaler correctly scales it at runtime.
        //     └ PiPBackground — Image that fills the overlay, provides the dark border.
        //         └ PiPRawImage — RawImage inset by 2px via offsets to create the border.
        //
        // Hierarchy:
        //   PiPOverlay (anchor top-right, sizeDelta = 25% of reference width, 16:9)
        //     └ PiPBackground (anchor stretch, Image = dark bg)
        //         └ PiPRawImage (anchor stretch, inset 2px for border frame)
        GameObject pipUI = new GameObject("PiPOverlay");
        pipUI.transform.SetParent(hudCanvas.transform, false);

        // --- PiPOverlay: position and size relative to CanvasScaler reference resolution ---
        float referenceWidth = 1920f;                         // default CanvasScaler reference
        float pipWidth = referenceWidth * 0.25f;              // 480px at reference resolution
        float pipHeight = pipWidth * 9f / 16f;                // 270px (16:9 aspect)
        float margin = 16f;

        RectTransform overlayRect = pipUI.GetComponent<RectTransform>();
        if (overlayRect == null)
            overlayRect = pipUI.AddComponent<RectTransform>();
        overlayRect.anchorMin = new Vector2(1f, 1f);         // top-right
        overlayRect.anchorMax = new Vector2(1f, 1f);
        overlayRect.pivot = new Vector2(1f, 1f);             // pivot at top-right corner
        overlayRect.anchoredPosition = new Vector2(margin, margin);
        overlayRect.sizeDelta = new Vector2(pipWidth, pipHeight);

        // --- PiPBackground: fills overlay, dark background for the border frame ---
        GameObject pipPanel = new GameObject("PiPBackground");
        pipPanel.transform.SetParent(pipUI.transform, false);
        var panel = pipPanel.AddComponent<Image>();
        panel.color = new Color(0f, 0f, 0f, 0.9f);           // dark background

        RectTransform panelRect = pipPanel.GetComponent<RectTransform>();
        if (panelRect == null)
            panelRect = pipPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;                   // stretch to fill parent
        panelRect.anchorMax = Vector2.one;
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = Vector2.zero;                   // fills parent completely

        // --- PiPRawImage: child of background, inset 2px on all sides for border effect ---
        GameObject pipRawImageGO = new GameObject("PiPRawImage");
        pipRawImageGO.transform.SetParent(pipPanel.transform, false);
        var rawImage = pipRawImageGO.AddComponent<RawImage>();
        rawImage.texture = pipRenderTexture;

        RectTransform rawRect = pipRawImageGO.GetComponent<RectTransform>();
        if (rawRect == null)
            rawRect = pipRawImageGO.AddComponent<RectTransform>();
        rawRect.anchorMin = Vector2.zero;                     // stretch to fill parent
        rawRect.anchorMax = Vector2.one;
        rawRect.anchoredPosition = Vector2.zero;
        rawRect.offsetMin = new Vector2(2f, 2f);             // 2px inset (border)
        rawRect.offsetMax = new Vector2(2f, 2f);

        // 5. Add and wire PiPCameraController
        var pipController = trainingManager.AddComponent<PiPCameraController>();
        pipController.PiPCamera = pipCameraGO.GetComponent<Camera>();
        pipController.RenderTexture = pipRenderTexture;
        pipController.PiPRawImage = rawImage;
        pipController.SimulationManager = FindObjectOfType<EvolutionSimulationManager>();

        // Select the new camera for inspection
        Selection.activeGameObject = pipCameraGO;

        Debug.Log($"[PiP Camera Setup] Created PiPCameraController wired to {hudCanvas.name} canvas.");
        Debug.Log("[PiP Camera Setup] PiP overlay is in the upper-right corner at 25% screen width.");
    }
}
