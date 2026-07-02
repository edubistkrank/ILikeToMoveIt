using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private static void FinalizeMovedWaterPark()
    {
        WaterPark source = moveSessionWaterParkSource;
        Base oldBase = moveSessionOriginalBase;
        Vector3 targetPosition = moveHasLastGhostTransform ? moveLastGhostPosition : moveOriginalPosition;
        List<WaterParkItem> itemsSnapshot = new List<WaterParkItem>(moveSessionWaterParkItems);
        List<Pickupable> pickupablesSnapshot = new List<Pickupable>(moveSessionWaterParkPickupables);
        List<Pickupable> planterPickupablesSnapshot = new List<Pickupable>(moveSessionWaterParkPlanterPickupables);

        if (TryFindWaterParkDestination(targetPosition, out WaterPark destination, out Base destinationBase))
        {
            CompleteWaterParkTransfer(source, destination, oldBase, destinationBase, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot);
            return;
        }

        if (Instance != null)
        {
            Instance.StartCoroutine(FinalizeMovedWaterParkDelayed(source, oldBase, targetPosition, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot));
            return;
        }

        if (source != null)
        {
            Object.Destroy(source.gameObject);
        }

        oldBase?.RebuildGeometry();
    }

    private static void RestoreCanceledWaterPark(Base originalBase, Base.Face originalFace, WaterPark source, List<WaterParkItem> itemsSnapshot, List<Pickupable> pickupablesSnapshot, List<Pickupable> planterPickupablesSnapshot)
    {
        if (originalBase == null)
        {
            return;
        }

        WaterPark destination = TryGetOrSpawnWaterParkAtFace(originalBase, originalFace, source);

        if (destination == null)
        {
            if (Instance != null)
            {
                Instance.StartCoroutine(RestoreCanceledWaterParkDeferred(originalBase, originalFace, source, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot));
            }
            else
            {
                Log.LogWarning("RestoreCanceledWaterPark: failed to respawn source WaterPark on cancel");
            }
            return;
        }

        Base destinationBase = destination.GetComponentInParent<Base>() ?? originalBase;
        ForceWaterParkVisible(destination);
        Log.LogInfo($"RestoreCanceledWaterPark: destination={destination} base={destinationBase}");
        CompleteWaterParkTransfer(source, destination, originalBase, destinationBase, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot, false, false);
    }

    private static IEnumerator RestoreCanceledWaterParkDeferred(Base originalBase, Base.Face originalFace, WaterPark source, List<WaterParkItem> itemsSnapshot, List<Pickupable> pickupablesSnapshot, List<Pickupable> planterPickupablesSnapshot)
    {
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;
            WaterPark destination = TryGetOrSpawnWaterParkAtFace(originalBase, originalFace, source);
            if (destination != null)
            {
                Base destinationBase = destination.GetComponentInParent<Base>() ?? originalBase;
                ForceWaterParkVisible(destination);
                CompleteWaterParkTransfer(source, destination, originalBase, destinationBase, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot, false, false);
                yield break;
            }
        }

        Log.LogWarning("RestoreCanceledWaterParkDeferred: timeout waiting for canceled WaterPark respawn");
    }

    private static WaterPark TryGetOrSpawnWaterParkAtFace(Base originalBase, Base.Face originalFace, WaterPark source)
    {
        if (originalBase == null)
        {
            return null;
        }

        WaterPark destination = originalBase.GetModule(originalFace) as WaterPark;
        if (IsWaterParkBoundToBaseFace(destination, originalBase, originalFace))
        {
            return destination;
        }

        foreach (GameObject prefab in GetCandidateWaterParkPrefabs(source))
        {
            if (prefab == null)
            {
                continue;
            }

            GameObject spawned = originalBase.SpawnModule(prefab, originalFace);
            WaterPark spawnedWaterPark = spawned != null
                ? (spawned.GetComponent<WaterPark>() ?? spawned.GetComponentInChildren<WaterPark>(true))
                : null;

            if (IsWaterParkBoundToBaseFace(spawnedWaterPark, originalBase, originalFace))
            {
                return spawnedWaterPark;
            }
        }

        destination = originalBase.GetModule(originalFace) as WaterPark;
        if (IsWaterParkBoundToBaseFace(destination, originalBase, originalFace))
        {
            return destination;
        }

        WaterPark[] all = Object.FindObjectsOfType<WaterPark>();
        for (int i = 0; i < all.Length; i++)
        {
            WaterPark candidate = all[i];
            if (ReferenceEquals(candidate, source))
            {
                continue;
            }

            if (IsWaterParkBoundToBaseFace(candidate, originalBase, originalFace))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<GameObject> GetCandidateWaterParkPrefabs(WaterPark source)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        System.Reflection.FieldInfo largeField = typeof(WaterPark).GetField("largeRoomWaterParkPrefab", flags);
        System.Reflection.FieldInfo roomField = typeof(WaterPark).GetField("roomWaterParkPrefab", flags);

        GameObject largePrefab = largeField?.GetValue(null) as GameObject;
        GameObject roomPrefab = roomField?.GetValue(null) as GameObject;

        if (source is LargeRoomWaterPark)
        {
            if (largePrefab != null) yield return largePrefab;
            if (roomPrefab != null) yield return roomPrefab;
        }
        else
        {
            if (roomPrefab != null) yield return roomPrefab;
            if (largePrefab != null) yield return largePrefab;
        }
    }

    private static void ForceWaterParkVisible(WaterPark waterPark)
    {
        if (waterPark == null)
        {
            return;
        }

        if (!waterPark.gameObject.activeSelf)
        {
            waterPark.gameObject.SetActive(true);
        }

        Renderer[] renderers = waterPark.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = true;
            }
        }
    }

    private static bool IsWaterParkBoundToBaseFace(WaterPark waterPark, Base expectedBase, Base.Face expectedFace)
    {
        if (waterPark == null || expectedBase == null)
        {
            return false;
        }

        Base actualBase = waterPark.GetComponentInParent<Base>();
        if (!ReferenceEquals(actualBase, expectedBase))
        {
            return false;
        }

        return ReferenceEquals(expectedBase.GetModule(expectedFace), waterPark);
    }

    private static bool TryFindWaterParkDestination(Vector3 targetPosition, out WaterPark destination, out Base destinationBase)
    {
        destination = moveSessionWaterParkDestination;
        destinationBase = destination != null ? destination.GetComponentInParent<Base>() : null;
        return destination != null;
    }

    private static IEnumerator FinalizeMovedWaterParkDelayed(WaterPark source, Base oldBase, Vector3 targetPosition, List<WaterParkItem> itemsSnapshot, List<Pickupable> pickupablesSnapshot, List<Pickupable> planterPickupablesSnapshot)
    {
        const int maxFrames = 90;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;

            if (TryFindWaterParkDestination(targetPosition, out WaterPark destination, out Base destinationBase))
            {
                CompleteWaterParkTransfer(source, destination, oldBase, destinationBase, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot);
                yield break;
            }
        }

        if (source != null)
        {
            Object.Destroy(source.gameObject);
        }

        oldBase?.RebuildGeometry();
    }

    private static void CompleteWaterParkTransfer(WaterPark source, WaterPark destination, Base oldBase, Base destinationBase, List<WaterParkItem> itemsSnapshot, List<Pickupable> pickupablesSnapshot, List<Pickupable> planterPickupablesSnapshot, bool rebuildBases = true, bool destroySource = true)
    {
        bool restored = false;
        if (pickupablesSnapshot != null && pickupablesSnapshot.Count > 0)
        {
            for (int i = 0; i < pickupablesSnapshot.Count; i++)
            {
                Pickupable pickupable = pickupablesSnapshot[i];
                if (pickupable == null)
                {
                    continue;
                }

                if (!pickupable.gameObject.activeSelf)
                {
                    pickupable.gameObject.SetActive(true);
                }

                destination.AddItem(pickupable);
                Vector3 clampedPosition = pickupable.transform.position;
                destination.EnsurePointIsInside(ref clampedPosition);
                pickupable.transform.position = clampedPosition;
                restored = true;
            }
        }

        if (planterPickupablesSnapshot != null && planterPickupablesSnapshot.Count > 0 && Instance != null)
        {
            Instance.StartCoroutine(RestoreWaterParkPlanterItemsDeferred(destination, planterPickupablesSnapshot));
        }

        if (!restored && itemsSnapshot != null && itemsSnapshot.Count > 0)
        {
            for (int i = 0; i < itemsSnapshot.Count; i++)
            {
                WaterParkItem item = itemsSnapshot[i];
                if (item != null && item.GetWaterPark() != destination)
                {
                    item.SetWaterPark(destination);
                    restored = true;
                }
            }
        }

        if (!moveSessionWaterParkUseVanillaFauna && !restored && source != null && source.HasItemsInside())
        {
            WaterPark.TransferValue(source, destination);
        }

        Base newBase = destinationBase ?? destination.GetComponentInParent<Base>();
        if (destroySource && source != null && !ReferenceEquals(source, destination))
        {
            Object.Destroy(source.gameObject);
        }

        if (rebuildBases)
        {
            oldBase?.RebuildGeometry();
            if (newBase != null && !ReferenceEquals(newBase, oldBase))
            {
                newBase.RebuildGeometry();
            }
        }
    }

    private static void CaptureWaterParkItems(WaterPark waterPark)
    {
        moveSessionWaterParkItems.Clear();
        moveSessionWaterParkPickupables.Clear();
        if (waterPark == null)
        {
            return;
        }

        System.Reflection.FieldInfo itemsField = typeof(WaterPark).GetField("items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (itemsField == null)
        {
            return;
        }

        if (itemsField.GetValue(waterPark.rootWaterPark) is List<WaterParkItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                WaterParkItem item = items[i];
                if (item != null)
                {
                    moveSessionWaterParkItems.Add(item);
                    Pickupable pickupable = item.GetComponent<Pickupable>();
                    if (pickupable != null)
                    {
                        moveSessionWaterParkPickupables.Add(pickupable);
                    }
                }
            }
        }
    }

    private static void CaptureWaterParkPlanterItems(WaterPark waterPark)
    {
        moveSessionWaterParkPlanterPickupables.Clear();
        if (waterPark == null)
        {
            return;
        }

        if (waterPark.planter != null)
        {
            EnsurePlanterReady(waterPark.planter);
            CaptureFromPlanter(waterPark.planter);
        }

        System.Reflection.FieldInfo plantersField = waterPark.GetType().GetField("planters",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object plantersObj = plantersField?.GetValue(waterPark);
        if (plantersObj != null)
        {
            System.Type plantersType = plantersObj.GetType();
            foreach (string name in new[] { "leftPlanter", "rightPlanter" })
            {
                System.Reflection.FieldInfo f = plantersType.GetField(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(plantersObj) is Planter p)
                {
                    EnsurePlanterReady(p);
                    CaptureFromPlanter(p);
                }
            }
        }
    }

    private static void CaptureFromPlanter(Planter planter)
    {
        StorageContainer storageContainer = planter.storageContainer;
        ItemsContainer container = storageContainer?.container;
        if (container == null)
        {
            CaptureWaterParkPlanterItemsFromSlots(planter);
            return;
        }

        List<InventoryItem> snapshot = new List<InventoryItem>();
        foreach (InventoryItem inventoryItem in container)
        {
            if (inventoryItem != null)
            {
                snapshot.Add(inventoryItem);
            }
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            Pickupable pickupable = snapshot[i].item;
            if (pickupable == null || moveSessionWaterParkPlanterPickupables.Contains(pickupable))
            {
                continue;
            }

            Plantable plantable = pickupable.GetComponent<Plantable>();
            if (plantable != null)
            {
                planter.RemoveItem(plantable);
            }
            else
            {
                container.RemoveItem(pickupable, true);
            }

            moveSessionWaterParkPlanterPickupables.Add(pickupable);
        }
    }

    private static void CaptureWaterParkPlanterItemsFromSlots(Planter planter)
    {
        if (planter == null)
        {
            return;
        }

        CaptureWaterParkPlanterItemsFromSlotArray(planter, "bigPlantSlots");
        CaptureWaterParkPlanterItemsFromSlotArray(planter, "smallPlantSlots");
    }

    private static void CaptureWaterParkPlanterItemsFromSlotArray(Planter planter, string fieldName)
    {
        try
        {
            System.Reflection.FieldInfo slotsField = typeof(Planter).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (!(slotsField?.GetValue(planter) is System.Array slots))
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                object slot = slots.GetValue(i);
                if (slot == null)
                {
                    continue;
                }

                System.Reflection.FieldInfo plantableField = slot.GetType().GetField("plantable", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Plantable plantable = plantableField?.GetValue(slot) as Plantable;
                Pickupable pickupable = plantable != null ? plantable.GetComponent<Pickupable>() : null;
                if (pickupable == null || moveSessionWaterParkPlanterPickupables.Contains(pickupable))
                {
                    continue;
                }

                planter.RemoveItem(plantable);
                moveSessionWaterParkPlanterPickupables.Add(pickupable);
            }
        }
        catch
        {
        }
    }

    private static void ParkCapturedWaterParkPickupables()
    {
        if (moveSessionWaterParkPickupables.Count == 0)
        {
            return;
        }

        Transform parkingRoot = GetOrCreateMoveSessionParkingRoot();
        for (int i = 0; i < moveSessionWaterParkPickupables.Count; i++)
        {
            Pickupable pickupable = moveSessionWaterParkPickupables[i];
            if (pickupable == null)
            {
                continue;
            }

            WaterParkItem item = pickupable.GetComponent<WaterParkItem>();
            if (item != null)
            {
                item.SetWaterPark(null);
            }

            pickupable.transform.SetParent(parkingRoot, true);
            if (pickupable.gameObject.activeSelf)
            {
                pickupable.gameObject.SetActive(false);
            }
        }
    }

    private static void ParkCapturedWaterParkPlanterPickupables()
    {
        if (moveSessionWaterParkPlanterPickupables.Count == 0)
        {
            return;
        }

        Transform parkingRoot = GetOrCreateMoveSessionParkingRoot();
        for (int i = 0; i < moveSessionWaterParkPlanterPickupables.Count; i++)
        {
            Pickupable pickupable = moveSessionWaterParkPlanterPickupables[i];
            if (pickupable == null)
            {
                continue;
            }

            pickupable.transform.SetParent(parkingRoot, true);
            if (pickupable.gameObject.activeSelf)
            {
                pickupable.gameObject.SetActive(false);
            }
        }
    }

    private static IEnumerator RestoreWaterParkPlanterItemsDeferred(WaterPark destination, List<Pickupable> planterPickupablesSnapshot)
    {
        if (destination == null || planterPickupablesSnapshot == null || planterPickupablesSnapshot.Count == 0)
        {
            yield break;
        }

        List<Pickupable> pending = new List<Pickupable>(planterPickupablesSnapshot);
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames && pending.Count > 0; frame++)
        {
            List<Planter> planters = GetWaterParkPlanters(destination);
            foreach (Planter planter in planters)
            {
                EnsurePlanterReady(planter);
            }

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pickupable pickupable = pending[i];
                if (pickupable == null)
                {
                    pending.RemoveAt(i);
                    continue;
                }

                if (pickupable.gameObject.activeSelf)
                {
                    pickupable.gameObject.SetActive(false);
                }

                pickupable.ResetTechTypeOverride();

                bool added = false;
                foreach (Planter planter in planters)
                {
                    ItemsContainer container = planter.storageContainer?.container;
                    if (container == null)
                    {
                        continue;
                    }

                    InventoryItem result = container.AddItem(pickupable);
                    if (result != null)
                    {
                        added = true;
                        break;
                    }
                }

                if (added)
                {
                    pending.RemoveAt(i);
                }
            }

            yield return null;
        }
    }

    private static List<Planter> GetWaterParkPlanters(WaterPark waterPark)
    {
        List<Planter> result = new List<Planter>();
        if (waterPark == null)
        {
            return result;
        }

        if (waterPark.planter != null)
        {
            result.Add(waterPark.planter);
        }

        System.Reflection.FieldInfo plantersField = waterPark.GetType().GetField("planters",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object plantersObj = plantersField?.GetValue(waterPark);
        if (plantersObj != null)
        {
            System.Type t = plantersObj.GetType();
            foreach (string name in new[] { "leftPlanter", "rightPlanter" })
            {
                System.Reflection.FieldInfo f = t.GetField(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(plantersObj) is Planter p)
                {
                    result.Add(p);
                }
            }
        }

        return result;
    }

    private static void EnsurePlanterReady(Planter planter)
    {
        if (planter == null)
        {
            return;
        }

        try
        {
            if (planter.storageContainer != null && !planter.storageContainer.enabled)
            {
                planter.storageContainer.enabled = true;
            }

            System.Reflection.MethodInfo initialize = typeof(Planter).GetMethod("Initialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            initialize?.Invoke(planter, null);

            System.Reflection.MethodInfo subscribe = typeof(Planter).GetMethod("Subscribe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            subscribe?.Invoke(planter, new object[] { true });
        }
        catch
        {
        }
    }

    private static Transform GetOrCreateMoveSessionParkingRoot()
    {
        if (moveSessionParkingRoot != null)
        {
            return moveSessionParkingRoot;
        }

        GameObject root = new GameObject("ILikeToMoveIt_WaterParkParkingRoot");
        root.hideFlags = HideFlags.HideAndDontSave;
        moveSessionParkingRoot = root.transform;
        return moveSessionParkingRoot;
    }

    [HarmonyPatch(typeof(Base), "SpawnModule", new[] { typeof(GameObject), typeof(Base.Face) })]
    private static class Base_SpawnModule_Patch
    {
        private static void Postfix(GameObject __result)
        {
            if (!moveSessionActive || moveTechType != TechType.BaseWaterPark || __result == null)
            {
                return;
            }

            WaterPark waterPark = __result.GetComponent<WaterPark>() ?? __result.GetComponentInChildren<WaterPark>(true);
            if (waterPark != null)
            {
                moveSessionWaterParkDestination = waterPark;
            }
        }
    }

    [HarmonyPatch(typeof(WaterParkGeometry), "CanDeconstruct")]
    private static class WaterParkGeometry_CanDeconstruct_Patch
    {
        private static bool Prefix(ref bool __result, ref string reason)
        {
            if (!moveSessionActive || moveBackend != MoveBackend.Face || moveTechType != TechType.BaseWaterPark)
            {
                return true;
            }

            if (Settings != null && Settings.PreventMoveWaterParkIfNotEmpty)
            {
                return true;
            }

            reason = null;
            __result = true;
            return false;
        }
    }

    private static void ApplyIsCellUnderConstructionPatch(Harmony harmonyInstance)
    {
        try
        {
            System.Reflection.MethodInfo target = typeof(Base).GetMethod(
                "IsCellUnderConstruction",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (target == null)
            {
                Log.LogWarning("ApplyIsCellUnderConstructionPatch: IsCellUnderConstruction not found");
                return;
            }

            System.Reflection.MethodInfo postfix = typeof(Plugin).GetMethod(
                nameof(IsCellUnderConstruction_Postfix),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (postfix == null)
            {
                Log.LogWarning("ApplyIsCellUnderConstructionPatch: postfix method not found");
                return;
            }

            harmonyInstance.Patch(target, postfix: new HarmonyMethod(postfix));
        }
        catch (System.Exception ex)
        {
            Log.LogError($"ApplyIsCellUnderConstructionPatch failed: {ex.Message}");
        }
    }

    private static void IsCellUnderConstruction_Postfix(Base __instance, ref bool __result)
    {
        if (!moveSessionActive || moveBackend != MoveBackend.Face || moveTechType != TechType.BaseWaterPark)
        {
            return;
        }

        if (!ReferenceEquals(__instance, moveSessionOriginalBase))
        {
            return;
        }

        __result = false;
    }
}
