---
sessionId: session-261022-005928-p7ht
---

# Technical Design

### Current Implementation

`HUDSetupEditor.cs` is a Unity Editor script that auto-configures the EvolutionHUD Canvas and checkpoint manager. It has three helper methods: `CreatePanel`, `CreateTMProText`, and `CreateButton`.

### The Bug

All three helper methods follow the same broken pattern:

1. Create a new `GameObject`
2. Call `SetParent(parent.transform, false)` — when the parent is a Canvas, Unity **auto-creates a RectTransform** on the child
3. Call `AddComponent<RectTransform>()` — this tries to add a **second** RectTransform, which either fails or returns null, causing the NullReferenceException at line 157

### Proposed Fix

Change `AddComponent<RectTransform>()` to `GetComponent<RectTransform>()` in all three helper methods. Since `SetParent` already creates the RectTransform when parenting to a Canvas, we just need to retrieve it instead of trying to create a duplicate.

**File: `Assets/Editor/HUDSetupEditor.cs`**

- Line 156: `var rt = panel.AddComponent<RectTransform>();` → `var rt = panel.GetComponent<RectTransform>();`
- Line 174: `var rt = go.AddComponent<RectTransform>();` → `var rt = go.GetComponent<RectTransform>();`
- Line 191: `var rt = btn.AddComponent<RectTransform>();` → `var rt = btn.GetComponent<RectTransform>();`
- Line 204: `var textRT = textGo.AddComponent<RectTransform>();` → `var textRT = textGo.GetComponent<RectTransform>();`

# Delivery Steps

### ✓ Step 1: Fix RectTransform double-add in HUDSetupEditor.cs
Replace all `AddComponent<RectTransform>()` calls with `GetComponent<RectTransform>()` in the three helper methods.

In `CreatePanel` (line 156):
- Change `var rt = panel.AddComponent<RectTransform>();` to `var rt = panel.GetComponent<RectTransform>();`

In `CreateTMProText` (line 174):
- Change `var rt = go.AddComponent<RectTransform>();` to `var rt = go.GetComponent<RectTransform>();`

In `CreateButton` (lines 191 and 204):
- Change `var rt = btn.AddComponent<RectTransform>();` to `var rt = btn.GetComponent<RectTransform>();`
- Change `var textRT = textGo.AddComponent<RectTransform>();` to `var textRT = textGo.GetComponent<RectTransform>();`

Since `SetParent(parent.transform, false)` already auto-creates a RectTransform when the parent is a Canvas, `GetComponent` will retrieve the existing one instead of trying to create a duplicate.