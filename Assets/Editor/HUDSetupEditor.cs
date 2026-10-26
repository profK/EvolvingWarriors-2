using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor tool that auto-configures the EvolutionHUD Canvas and EvolutionCheckpointManager
/// in the TrainingScene. Run via: Tools → Evolving Warriors → Setup HUD & Checkpoints
/// </summary>
public class HUDSetupEditor : EditorWindow
{
    [MenuItem("Tools/Evolving Warriors/Setup HUD & Checkpoints")]
    public static void SetupHUD()
    {
        // Find the TrainingManager (holds EvolutionSimulationManager + WarriorSpawner)
        GameObject trainingManager = GameObject.Find("TrainingManager");
        if (trainingManager == null)
        {
            // Try to find any object with EvolutionSimulationManager
            var manager = FindObjectOfType<EvolutionSimulationManager>();
            if (manager != null)
                trainingManager = manager.gameObject;
            else
            {
                EditorUtility.DisplayDialog("HUD Setup",
                    "No TrainingManager or EvolutionSimulationManager found in the current scene.\n\n" +
                    "Make sure you're in the TrainingScene.",
                    "OK");
                return;
            }
        }

        var simManager = trainingManager.GetComponent<EvolutionSimulationManager>();
        if (simManager == null)
        {
            EditorUtility.DisplayDialog("HUD Setup",
                "EvolutionSimulationManager not found on TrainingManager.",
                "OK");
            return;
        }

        // 1. Add CheckpointManager if not present
        var checkpointManager = trainingManager.GetComponent<EvolutionCheckpointManager>();
        if (checkpointManager == null)
        {
            checkpointManager = trainingManager.AddComponent<EvolutionCheckpointManager>();
            checkpointManager.simulationManager = simManager;
            Debug.Log("Added EvolutionCheckpointManager to TrainingManager.");
        }

        // 2. Create or find HUD Canvas
        GameObject hudCanvas = GameObject.Find("EvolutionHUDCanvas");
        if (hudCanvas == null)
        {
            hudCanvas = new GameObject("EvolutionHUDCanvas");
            Canvas canvas = hudCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.AddComponent<CanvasScaler>();
            hudCanvas.AddComponent<GraphicRaycaster>();
            hudCanvas.AddComponent<EvolutionHUD>();
        }

        CanvasGroup canvasGroup = hudCanvas.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = hudCanvas.AddComponent<CanvasGroup>();

        EvolutionHUD hud = hudCanvas.GetComponent<EvolutionHUD>();
        if (hud == null)
            hud = hudCanvas.AddComponent<EvolutionHUD>();
        hud.simulationManager = simManager;

        // 2b. Clean up old UI elements to make this tool idempotent on re-runs
        string[] oldPanelNames = { "TopBar", "StatsPanel", "PrevGenPanel", "SingleBar", "PauseButton" };
        // Collect into a list first to avoid modifying the collection during iteration
        var childrenToDestroy = new List<GameObject>();
        foreach (Transform child in hudCanvas.transform)
        {
            if (System.Array.IndexOf(oldPanelNames, child.name) >= 0)
            {
                childrenToDestroy.Add(child.gameObject);
            }
        }
        foreach (GameObject go in childrenToDestroy)
        {
            UnityEngine.Object.DestroyImmediate(go);
        }

        // 3. Create single top bar: combined stats text + pause button
        GameObject singleBar = new GameObject("SingleBar");
        singleBar.transform.SetParent(hudCanvas.transform, false);
        
        var barImg = singleBar.AddComponent<Image>();
        barImg.color = new Color(0f, 0f, 0f, 0.7f);
        
        var barRT = singleBar.GetComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0f, 1f);
        barRT.anchorMax = new Vector2(1f, 1f);
        barRT.sizeDelta = new Vector2(0, 40);
        barRT.anchoredPosition = new Vector2(0f, 50f);

        // Three separate text fields: generation (left), time (center), state (right before pause button)
        hud.generationText = CreateAnchoredTextWithWidth(singleBar, "GenerationText", "Generation: 0", 18, TextAlignmentOptions.MidlineLeft, 10f, -300f);
        hud.timeRemainingText = CreateAnchoredTextWithWidth(singleBar, "TimeText", "Time: --", 18, TextAlignmentOptions.MidlineLeft, -290f, -170f);
        hud.stateText = CreateAnchoredTextWithWidth(singleBar, "StateText", "State: --", 18, TextAlignmentOptions.MidlineLeft, -160f, -100f);

        // Pause button (right side of bar)
        GameObject btn = CreateAnchoredButton(singleBar, "PauseButton");
        var btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
            btnText.text = "Pause";
        
        hud.pauseButton = btn.GetComponent<Button>();
        hud.pauseButtonText = btnText;

        // Mark scene as dirty
        EditorUtility.SetDirty(hudCanvas);
        EditorUtility.SetDirty(trainingManager);

        EditorUtility.DisplayDialog("HUD Setup Complete",
            "EvolutionHUD Canvas and EvolutionCheckpointManager have been configured.\n\n" +
            "Press Play to start training with live stats and checkpointing!",
            "OK");
    }

    static GameObject CreatePanel(GameObject parent, string name, Rect rect, Color bgColor)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent.transform, false);
        
        var img = panel.AddComponent<Image>();
        img.color = bgColor;
        
        var rt = panel.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(rect.x, rect.y);
        rt.offsetMax = new Vector2(rect.x + rect.width, rect.y + rect.height);
        
        return panel;
    }

    static TextMeshProUGUI CreateAnchoredText(GameObject parent, string name, string text, int fontSize, TextAlignmentOptions alignment)
    {
        return CreateAnchoredTextWithWidth(parent, name, text, fontSize, alignment, 10f, -150f);
    }

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
        
        var rt = go.GetComponent<RectTransform>();
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
        
        var button = btn.AddComponent<Button>();
        
        var img = btn.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 0.2f);
        
        var rt = btn.GetComponent<RectTransform>();
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
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.anchoredPosition = Vector2.zero;
        textRT.sizeDelta = Vector2.zero;
        
        return btn;
    }
}