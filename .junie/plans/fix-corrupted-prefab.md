---
sessionId: session-261017-152521-e37v
---

# Problem Analysis

### Root Cause

The `HumanCharacterDummy_M Armed.prefab` is a **variant** of `HumanCharacterDummy_M.prefab` (guid `dbde7fb8c84e244d4842d12130437f67`). It adds a nested Sword001 prefab instance with two added components: a `CapsuleCollider` and an `AttackSurface` MonoBehaviour.

The corruption is in how the added components reference their `m_GameObject`. Both the `CapsuleCollider` (line 228) and the `MonoBehaviour` (line 251) point to the source prefab's GameObject using a **static GUID reference**:

```
m_GameObject: {fileID: 145208, guid: b8c5caa99f8fb4e259033573bdc71acf, type: 3}
```

This is incorrect. Unity's prefab variant serialization expects added components to reference the **instantiated** GameObject via a **stripped reference** — a local instance ID resolved through the PrefabInstance. The `Warrior.prefab` in the same project demonstrates the correct pattern: it first declares a stripped `GameObject` block (line 80-84), then the added components reference that stripped ID:

```
m_GameObject: {fileID: 6508550611535698564}
```

### Why Unity Reports Errors

Unity cannot resolve the GUID-based GameObject reference at import time because the component belongs to the *instance* of the sword prefab inside the variant, not the source asset. The error messages "GameObject 'Sword001' does not reference component CapsuleCollider/MonoBehaviour" confirm Unity is trying — and failing — to connect these components to the instantiated GameObject.

# Technical Design

### Fix: Correct the m_GameObject References

The fix requires two changes in `HumanCharacterDummy_M Armed.prefab`:

1. **Add a stripped GameObject block** — similar to what `Warrior.prefab` does (lines 80-84). This block resolves the instantiated Sword001 GameObject from the nested PrefabInstance:
   ```
   --- !u!1 &<new_id> stripped
   GameObject:
     m_CorrespondingSourceObject: {fileID: 145208, guid: b8c5caa99f8fb4e259033573bdc71acf, type: 3}
     m_PrefabInstance: {fileID: 7400125921521206789}
     m_PrefabAsset: {fileID: 0}
   ```

2. **Update both component `m_GameObject` fields** — change from the GUID reference `{fileID: 145208, guid: b8c5caa99f8fb4e259033573bdc71acf, type: 3}` to the stripped reference `{fileID: <new_id>}`.

This mirrors the exact pattern used in `Warrior.prefab` (lines 80-91, 159-160) where added components reference a stripped GameObject resolved through the PrefabInstance chain.

### Reference: Warrior.prefab Pattern

The `Warrior.prefab` already demonstrates the correct serialization pattern for added components in a variant:
- Line 80-84: stripped `GameObject` declaration resolving from source via `m_CorrespondingSourceObject` + `m_PrefabInstance`
- Line 91, 107, 120, 132, 146, 159: added components reference the stripped GameObject by its local ID only

The same pattern must be applied to the Sword001 nested instance in `HumanCharacterDummy_M Armed.prefab`.

# Delivery Steps

### ✓ Step 1: Fix the corrupted prefab file
Edit `Assets/prefabs/HumanCharacterDummy_M Armed.prefab` to correct the broken GameObject references for the two added components (CapsuleCollider and AttackSurface MonoBehaviour).

- Add a stripped GameObject block (e.g. `!u!1 &9000000000000000000 stripped`) that resolves the instantiated Sword001 GameObject via `m_CorrespondingSourceObject` pointing to `fileID: 145208, guid: b8c5caa99f8fb4e259033573bdc71acf` and `m_PrefabInstance` pointing to `7400125921521206789`.
- Update the `CapsuleCollider` (line 228) `m_GameObject` field from the GUID reference to the new stripped GameObject ID.
- Update the `MonoBehaviour` (line 251) `m_GameObject` field from the GUID reference to the same stripped GameObject ID.
- Place the stripped GameObject block before the two component definitions, following the ordering pattern in `Warrior.prefab`.

### ✓ Step 2: Verify the fix in Unity
Confirm that Unity no longer reports import errors for the corrected prefab.

- Open Unity and verify `Assets/prefabs/HumanCharacterDummy_M Armed.prefab` imports without the "GameObject 'Sword001' does not reference component" errors.
- Confirm the prefab displays correctly in the Prefab Editor with both the CapsuleCollider and AttackSurface components attached to the Sword001 child object.
- Verify the prefab variant still correctly inherits from `HumanCharacterDummy_M.prefab` and that the Shield002 addition is unaffected.