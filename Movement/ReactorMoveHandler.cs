using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private static bool ReactorHasContentOnRoot(GameObject reactorRoot, TechType reactorTechType)
    {
        return GetReactorPickupablesFromRoot(reactorRoot, reactorTechType).Count > 0;
    }

    private static bool CaptureReactorItemsFromRoot(GameObject reactorRoot, TechType reactorTechType)
    {
        moveSessionReactorStateCaptured = false;
        moveSessionReactorPower = 0f;
        moveSessionReactorToConsume = 0f;

        if (moveSessionReactorPickupables == null)
        {
            moveSessionReactorPickupables = new List<Pickupable>();
        }
        else
        {
            moveSessionReactorPickupables.Clear();
        }

        moveSessionReactorTechType = TechType.None;
        if (reactorRoot == null)
        {
            return true;
        }

        if (reactorTechType != TechType.BaseBioReactor && reactorTechType != TechType.BaseNuclearReactor)
        {
            return true;
        }

        CaptureReactorRuntimeState(reactorRoot, reactorTechType);

        List<Pickupable> found = GetReactorPickupablesFromRoot(reactorRoot, reactorTechType);
        List<Pickupable> extracted = RemoveReactorItemsFromRoot(reactorRoot, reactorTechType, found);
        if (extracted.Count != found.Count)
        {
            Log.LogWarning($"CaptureReactorItemsFromRoot: extracted {extracted.Count}/{found.Count}, aborting move to avoid stale reactor references");
            return false;
        }

        for (int i = 0; i < extracted.Count; i++)
        {
            Pickupable pickupable = extracted[i];
            if (pickupable == null)
            {
                continue;
            }

            if (!pickupable.gameObject.activeSelf)
            {
                pickupable.gameObject.SetActive(true);
            }

            moveSessionReactorPickupables.Add(pickupable);
        }

        moveSessionReactorTechType = reactorTechType;

        Transform parkingRoot = GetOrCreateMoveSessionParkingRoot();
        for (int i = 0; i < moveSessionReactorPickupables.Count; i++)
        {
            Pickupable pickupable = moveSessionReactorPickupables[i];
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

        return true;
    }

    private static List<Pickupable> GetReactorPickupablesFromRoot(GameObject reactorRoot, TechType reactorTechType)
    {
        List<Pickupable> result = new List<Pickupable>();
        if (reactorRoot == null)
        {
            return result;
        }

        if (reactorTechType == TechType.BaseBioReactor)
        {
            ItemsContainer container = GetBioReactorContainerFromRoot(reactorRoot);
            if (container == null)
            {
                return result;
            }

            foreach (InventoryItem inventoryItem in container)
            {
                if (inventoryItem?.item != null)
                {
                    result.Add(inventoryItem.item);
                }
            }

            return result;
        }

        if (reactorTechType == TechType.BaseNuclearReactor)
        {
            Equipment equipment = GetNuclearReactorEquipmentFromRoot(reactorRoot);
            if (equipment == null)
            {
                return result;
            }

            for (int i = 0; i < 8; i++)
            {
                string slot = "NuclearReactor" + i;
                InventoryItem itemInSlot = equipment.GetItemInSlot(slot);
                if (itemInSlot?.item != null)
                {
                    result.Add(itemInSlot.item);
                }
            }
        }

        return result;
    }

    private static List<Pickupable> RemoveReactorItemsFromRoot(GameObject reactorRoot, TechType reactorTechType, List<Pickupable> pickupables)
    {
        List<Pickupable> extracted = new List<Pickupable>();
        if (reactorRoot == null || pickupables == null || pickupables.Count == 0)
        {
            return extracted;
        }

        if (reactorTechType == TechType.BaseBioReactor)
        {
            ItemsContainer container = GetBioReactorContainerFromRoot(reactorRoot);
            if (container == null)
            {
                return extracted;
            }

            for (int i = 0; i < pickupables.Count; i++)
            {
                Pickupable pickupable = pickupables[i];
                if (pickupable != null)
                {
                    if (container.RemoveItem(pickupable, true))
                    {
                        extracted.Add(pickupable);
                    }
                }
            }

            return extracted;
        }

        if (reactorTechType == TechType.BaseNuclearReactor)
        {
            Equipment equipment = GetNuclearReactorEquipmentFromRoot(reactorRoot);
            if (equipment == null)
            {
                return extracted;
            }

            for (int p = 0; p < pickupables.Count; p++)
            {
                Pickupable pickupable = pickupables[p];
                if (pickupable == null)
                {
                    continue;
                }

                for (int i = 0; i < 8; i++)
                {
                    string slot = "NuclearReactor" + i;
                    InventoryItem itemInSlot = equipment.GetItemInSlot(slot);
                    if (itemInSlot?.item == pickupable)
                    {
                        InventoryItem removed = equipment.RemoveItem(slot, true, false);
                        if (removed?.item == pickupable)
                        {
                            extracted.Add(pickupable);
                        }

                        break;
                    }
                }
            }
        }

        return extracted;
    }

    private static void QueueReactorRestore(GameObject reactorRoot, TechType reactorTechType, List<Pickupable> pickupables)
    {
        if (reactorRoot == null || pickupables == null || pickupables.Count == 0)
        {
            return;
        }

        List<Pickupable> pending = new List<Pickupable>(pickupables);
        if (TryRestoreReactorItemsIntoRoot(reactorRoot, reactorTechType, pending))
        {
            return;
        }

        if (Instance != null)
        {
            Instance.StartCoroutine(RestoreReactorItemsIntoRootDeferred(reactorRoot, reactorTechType, pending));
        }
    }

    private static IEnumerator RestoreReactorItemsIntoRootDeferred(GameObject reactorRoot, TechType reactorTechType, List<Pickupable> pending)
    {
        if (reactorRoot == null || pending == null || pending.Count == 0)
        {
            yield break;
        }

        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames && pending.Count > 0; frame++)
        {
            TryRestoreReactorItemsIntoRoot(reactorRoot, reactorTechType, pending);
            yield return null;
        }
    }

    private static bool TryRestoreReactorItemsIntoRoot(GameObject reactorRoot, TechType reactorTechType, List<Pickupable> pending)
    {
        if (reactorRoot == null || pending == null || pending.Count == 0)
        {
            return true;
        }

        if (reactorTechType == TechType.BaseBioReactor)
        {
            ItemsContainer container = GetBioReactorContainerFromRoot(reactorRoot);
            if (container == null)
            {
                return false;
            }

            ForceRefreshContainerVisuals(container);

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
                InventoryItem added = container.AddItem(pickupable);
                if (added != null)
                {
                    pending.RemoveAt(i);
                }
            }

            ForceRefreshContainerVisuals(container);

            RestoreReactorRuntimeState(reactorRoot, reactorTechType);

            return pending.Count == 0;
        }

        if (reactorTechType == TechType.BaseNuclearReactor)
        {
            Equipment equipment = GetNuclearReactorEquipmentFromRoot(reactorRoot);
            if (equipment == null)
            {
                return false;
            }

            for (int p = pending.Count - 1; p >= 0; p--)
            {
                Pickupable pickupable = pending[p];
                if (pickupable == null)
                {
                    pending.RemoveAt(p);
                    continue;
                }

                if (pickupable.gameObject.activeSelf)
                {
                    pickupable.gameObject.SetActive(false);
                }

                pickupable.ResetTechTypeOverride();

                bool added = false;

                for (int i = 0; i < 8; i++)
                {
                    string slot = "NuclearReactor" + i;
                    if (equipment.GetItemInSlot(slot) == null)
                    {
                        if (equipment.AddItem(slot, new InventoryItem(pickupable), true))
                        {
                            added = true;
                            break;
                        }
                    }
                }

                if (!added)
                {
                    pickupable.transform.SetParent(GetOrCreateMoveSessionParkingRoot(), true);
                }
                else
                {
                    pending.RemoveAt(p);
                }
            }

            RestoreReactorRuntimeState(reactorRoot, reactorTechType);
            return pending.Count == 0;
        }

        return true;
    }

    private static void CaptureReactorRuntimeState(GameObject reactorRoot, TechType reactorTechType)
    {
        PowerSource source = GetReactorPowerSourceFromRoot(reactorRoot, reactorTechType);
        if (source != null)
        {
            moveSessionReactorPower = source.power;
        }

        Component reactorComponent = GetModuleComponentFromRoot(
            reactorRoot,
            reactorTechType == TechType.BaseBioReactor ? "BaseBioReactor" : "BaseNuclearReactor");
        if (reactorComponent != null)
        {
            const System.Reflection.BindingFlags fieldFlags = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance;
            System.Reflection.FieldInfo toConsumeField = reactorComponent.GetType().GetField(
                "_toConsume",
                fieldFlags)
                ?? reactorComponent.GetType().GetField(
                    "toConsume",
                    fieldFlags);
            if (toConsumeField != null)
            {
                object value = toConsumeField.GetValue(reactorComponent);
                if (value is float f)
                {
                    moveSessionReactorToConsume = f;
                }
            }
        }

        moveSessionReactorStateCaptured = true;
    }

    private static void RestoreReactorRuntimeState(GameObject reactorRoot, TechType reactorTechType)
    {
        if (!moveSessionReactorStateCaptured)
        {
            return;
        }

        PowerSource source = GetReactorPowerSourceFromRoot(reactorRoot, reactorTechType);
        if (source != null)
        {
            source.power = Mathf.Clamp(moveSessionReactorPower, 0f, source.maxPower);
        }

        Component reactorComponent = GetModuleComponentFromRoot(
            reactorRoot,
            reactorTechType == TechType.BaseBioReactor ? "BaseBioReactor" : "BaseNuclearReactor");
        if (reactorComponent != null)
        {
            const System.Reflection.BindingFlags fieldFlags = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance;
            System.Reflection.FieldInfo toConsumeField = reactorComponent.GetType().GetField(
                "_toConsume",
                fieldFlags)
                ?? reactorComponent.GetType().GetField(
                    "toConsume",
                    fieldFlags);
            if (toConsumeField != null)
            {
                toConsumeField.SetValue(reactorComponent, moveSessionReactorToConsume);
            }
        }
    }

    private static PowerSource GetReactorPowerSourceFromRoot(GameObject reactorRoot, TechType reactorTechType)
    {
        if (reactorRoot == null)
        {
            return null;
        }

        Component reactorComponent = GetReactorComponentFromRoot(reactorRoot, reactorTechType);
        if (reactorComponent != null)
        {
            System.Reflection.FieldInfo powerSourceField = reactorComponent.GetType().GetField(
                "_powerSource",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? reactorComponent.GetType().GetField(
                    "powerSource",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (powerSourceField != null && powerSourceField.GetValue(reactorComponent) is PowerSource reflectedSource)
            {
                return reflectedSource;
            }
        }

        PowerSource source = reactorRoot.GetComponent<PowerSource>()
            ?? reactorRoot.GetComponentInChildren<PowerSource>(true)
            ?? reactorRoot.GetComponentInParent<PowerSource>();
        if (source != null)
        {
            return source;
        }

        return reactorComponent != null
            ? (reactorComponent.GetComponent<PowerSource>()
                ?? reactorComponent.GetComponentInChildren<PowerSource>(true)
                ?? reactorComponent.GetComponentInParent<PowerSource>())
            : null;
    }

    private static Component GetReactorComponentFromRoot(GameObject reactorRoot, TechType reactorTechType)
    {
        return GetModuleComponentFromRoot(
            reactorRoot,
            reactorTechType == TechType.BaseBioReactor ? "BaseBioReactor" : "BaseNuclearReactor");
    }

    private static GameObject GetModuleRootFromBaseFace(Base baseComponent, Base.Face face)
    {
        if (baseComponent == null)
        {
            return null;
        }

        IBaseModule module = baseComponent.GetModule(face);
        Component component = module as Component;
        return component != null ? component.gameObject : null;
    }

    [HarmonyPatch(typeof(Base), "SpawnModule", new[] { typeof(GameObject), typeof(Base.Face) })]
    private static class Base_SpawnModule_ReactorTrack_Patch
    {
        private static void Postfix(GameObject __result)
        {
            if (!moveSessionActive || moveBackend != MoveBackend.Face || __result == null)
            {
                return;
            }

            if (moveTechType != TechType.BaseBioReactor && moveTechType != TechType.BaseNuclearReactor)
            {
                return;
            }

            moveSessionReactorDestinationRoot = __result;
        }
    }

    private static ItemsContainer GetBioReactorContainerFromRoot(GameObject reactorRoot)
    {
        Component reactorComponent = GetModuleComponentFromRoot(reactorRoot, "BaseBioReactor");
        if (reactorComponent == null)
        {
            return null;
        }

        System.Type reactorType = reactorComponent.GetType();
        System.Reflection.PropertyInfo containerProperty = reactorType.GetProperty("container", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (containerProperty != null)
        {
            return containerProperty.GetValue(reactorComponent, null) as ItemsContainer;
        }

        System.Reflection.FieldInfo containerField = reactorType.GetField("_container", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? reactorType.GetField("container", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return containerField?.GetValue(reactorComponent) as ItemsContainer;
    }

    private static Equipment GetNuclearReactorEquipmentFromRoot(GameObject reactorRoot)
    {
        Component reactorComponent = GetModuleComponentFromRoot(reactorRoot, "BaseNuclearReactor");
        if (reactorComponent == null)
        {
            return null;
        }

        System.Type reactorType = reactorComponent.GetType();
        System.Reflection.PropertyInfo equipmentProperty = reactorType.GetProperty("equipment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (equipmentProperty != null)
        {
            return equipmentProperty.GetValue(reactorComponent, null) as Equipment;
        }

        System.Reflection.FieldInfo equipmentField = reactorType.GetField("_equipment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? reactorType.GetField("equipment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return equipmentField?.GetValue(reactorComponent) as Equipment;
    }

    private static Component GetModuleComponentFromRoot(GameObject root, string componentTypeName)
    {
        if (root == null || string.IsNullOrWhiteSpace(componentTypeName))
        {
            return null;
        }

        Component component = root.GetComponent(componentTypeName);
        if (component != null)
        {
            return component;
        }

        MonoBehaviour[] children = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < children.Length; i++)
        {
            MonoBehaviour mono = children[i];
            if (mono != null && mono.GetType().Name == componentTypeName)
            {
                return mono;
            }
        }

        MonoBehaviour[] parents = root.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < parents.Length; i++)
        {
            MonoBehaviour mono = parents[i];
            if (mono != null && mono.GetType().Name == componentTypeName)
            {
                return mono;
            }
        }

        return null;
    }

    private static void ForceRefreshContainerVisuals(ItemsContainer container)
    {
        if (container == null)
        {
            return;
        }

        try
        {
            container.Sort();
        }
        catch
        {
        }

        try
        {
            System.Reflection.MethodInfo notifyResize = container.GetType().GetMethod(
                "NotifyResize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            notifyResize?.Invoke(container, new object[] { container.sizeX, container.sizeY });
        }
        catch
        {
        }
    }
}
