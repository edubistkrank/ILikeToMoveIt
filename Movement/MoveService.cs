using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private static void RestoreCanceledFacePiece(Base originalBase, Base.Face originalFace, Base.FaceType originalFaceType)
    {
        if (originalBase == null)
        {
            return;
        }

        originalBase.SetFaceType(originalFace, originalFaceType);

        if (originalFaceType == Base.FaceType.Ladder)
        {
            Base.Face adjacentFace = Base.GetAdjacentFace(originalFace);
            originalBase.SetFaceType(adjacentFace, Base.FaceType.Ladder);
        }

        originalBase.RebuildGeometry();
    }

    private static bool TryMoveTargetedLocker()
    {
        if (Builder.isPlacing || !AvatarInputHandler.main.IsEnabled() || moveSessionActive)
        {
            SetMoveReticleIcon(false);
            return false;
        }

        if (Builder.GetGhostModel() != null)
        {
            Builder.ResetLast();
            Builder.End();
        }

        Targeting.AddToIgnoreList(Player.main.gameObject);
        Targeting.GetTarget(30f, out GameObject target, out float distance);
        if (target == null)
        {
            SetMoveReticleIcon(false);
            return false;
        }

        if (IsFloatingLockerMoveTarget(target, out Pickupable floatingPickupable, out GameObject floatingRoot))
        {
            if (distance > 11f)
            {
                SetMoveReticleIcon(false);
                return false;
            }

            if (TryBeginFloatingLockerMove(floatingPickupable, floatingRoot))
            {
                return true;
            }

            SetMoveReticleIcon(false);
            return false;
        }

        Constructable constructable = target.GetComponentInParent<Constructable>();
        BaseDeconstructable baseDecon = FindFaceDeconstructableForTarget(target, constructable);
        Log.LogInfo($"GetComponentInParent<BaseDeconstructable>: {(baseDecon != null ? "FOUND (" + baseDecon.gameObject.name + ")" : "NOT FOUND")}");

        if (baseDecon != null && distance <= 11f)
        {
            Log.LogInfo("TryMoveTargetedLocker: Found BaseDeconstructable");
            TechType recipeType = GetBaseDeconstructableTechType(baseDecon);
            Log.LogInfo($"TryMoveTargetedLocker: BaseDeconstructable recipeType={recipeType}");
            if (recipeType != TechType.None && IsMovableBySettings(recipeType))
            {
                Log.LogInfo($"TryMoveTargetedLocker: BaseDeconstructable is movable ({recipeType}), calling TryMoveBaseFacePiece");
                return TryMoveBaseFacePiece(baseDecon);
            }

            Log.LogWarning($"TryMoveTargetedLocker: BaseDeconstructable found but recipeType={recipeType}, movable={IsMovableBySettings(recipeType)}");
        }

        Log.LogInfo($"GetComponentInParent<Constructable>: {(constructable != null ? "FOUND (" + constructable.gameObject.name + ")" : "NOT FOUND")}");

        if (constructable != null && constructable.constructed && distance <= constructable.placeMaxDistance)
        {

            if (!IsMovableBySettings(constructable))
            {
                ErrorMessage.AddMessage(L("Ese objeto no está habilitado para mover", "That object is not enabled for moving"));
                return true;
            }

            StorageContainer storage = constructable.GetComponent<StorageContainer>();
            if (Settings != null && Settings.PreventMoveIfNotEmpty && storage != null && !storage.IsEmpty())
            {
                ErrorMessage.AddMessage(L("No se puede mover: tiene items", "Cannot move: contains items"));
                return true;
            }

            BeginMoveSessionCore(
                constructable.techType,
                constructable.transform.position,
                constructable.transform.rotation,
                constructable.gameObject,
                MoveBackend.Regular);

            if (!CaptureReactorItemsFromRoot(constructable.gameObject, constructable.techType))
            {
                ErrorMessage.AddMessage(L("No se puede mover: reactor en uso (combustible bloqueado)", "Cannot move: reactor is in use (fuel is locked)"));
                ClearMoveSession();
                return true;
            }
            moveSessionReactorSourceRoot = constructable.gameObject;
            moveSessionReactorDestinationRoot = null;

            Builder.ResetLast();
            constructable.gameObject.SetActive(false);
            if (Instance != null)
            {
                Instance.StartCoroutine(BeginPlacingAsync(moveTechType));
            }

            return true;
        }

        Log.LogInfo("TryMoveTargetedLocker: No valid object found");
        SetMoveReticleIcon(false);
        return false;
    }

    private static bool TryMoveBaseFacePiece(BaseDeconstructable baseDecon)
    {
        if (baseDecon == null)
        {
            Log.LogWarning("TryMoveBaseFacePiece: baseDecon is null");
            return false;
        }

        TechType recipeType = GetBaseDeconstructableTechType(baseDecon);
        Log.LogInfo($"TryMoveBaseFacePiece: recipeType={recipeType}");
        if (recipeType == TechType.None)
        {
            Log.LogWarning("TryMoveBaseFacePiece: recipeType is None");
            return false;
        }

        if (!IsMovableBySettings(recipeType))
        {
            Log.LogWarning($"TryMoveBaseFacePiece: {recipeType} not movable by settings");
            ErrorMessage.AddMessage(L("Ese objeto no está habilitado para mover", "That object is not enabled for moving"));
            return true;
        }

        Log.LogInfo($"TryMoveBaseFacePiece: Starting move session for {recipeType}");
        BeginMoveSessionCore(
            recipeType,
            baseDecon.transform.position,
            baseDecon.transform.rotation,
            baseDecon.gameObject,
            MoveBackend.Face);

        moveSessionFacePieceSource = baseDecon;
        moveSessionOriginalBase = baseDecon.GetComponentInParent<Base>();
        moveSessionOriginalFace = baseDecon.face;
        moveSessionOriginalFaceType = baseDecon.faceType;
        bypassResourceConsumption = true;

        if (recipeType == TechType.BaseWaterPark)
        {
            WaterPark waterPark = null;
            if (moveSessionOriginalBase != null && moveSessionOriginalFace != null)
            {
                waterPark = moveSessionOriginalBase.GetModule(moveSessionOriginalFace.Value) as WaterPark;
                Log.LogInfo($"WaterPark: GetModule result={waterPark}");
            }
            else
            {
                Log.LogWarning($"WaterPark: originalBase={moveSessionOriginalBase}, originalFace={moveSessionOriginalFace} — skipping GetModule");
            }

            if (waterPark == null)
            {
                waterPark = baseDecon.GetComponentInParent<WaterPark>() ?? baseDecon.GetComponentInChildren<WaterPark>(true);
                Log.LogInfo($"WaterPark: fallback GetComponent result={waterPark}");
            }

            if (waterPark == null)
            {
                WaterPark[] allParks = Object.FindObjectsOfType<WaterPark>();
                float bestDist = 25f;
                Vector3 origin = baseDecon.transform.position;
                foreach (WaterPark wp in allParks)
                {
                    if (wp == null)
                    {
                        continue;
                    }

                    float d = (wp.transform.position - origin).sqrMagnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        waterPark = wp;
                    }
                }

                Log.LogInfo($"WaterPark: scene search result={waterPark} dist={bestDist}");
            }

            moveSessionWaterParkSource = waterPark;
            Log.LogInfo($"WaterPark: source captured={waterPark != null}, planter={waterPark?.planter}");

            moveSessionWaterParkUseVanillaFauna = waterPark != null && waterPark.IsConnected();
            Log.LogInfo($"WaterPark: vanilla fauna handling={(moveSessionWaterParkUseVanillaFauna ? "enabled (stack/connected)" : "disabled")}");

            if (waterPark != null && Settings != null && Settings.PreventMoveWaterParkIfNotEmpty && waterPark.HasItemsInside())
            {
                ErrorMessage.AddMessage(L("No se puede mover: tiene items", "Cannot move: contains items"));
                ClearMoveSession();
                return true;
            }

            if (!moveSessionWaterParkUseVanillaFauna)
            {
                CaptureWaterParkItems(waterPark);
                Log.LogInfo($"WaterPark: fauna captured={moveSessionWaterParkPickupables.Count}");
            }
            else
            {
                moveSessionWaterParkItems.Clear();
                moveSessionWaterParkPickupables.Clear();
                Log.LogInfo("WaterPark: skipping fauna capture for connected stack");
            }

            CaptureWaterParkPlanterItems(waterPark);
            Log.LogInfo($"WaterPark: flora captured={moveSessionWaterParkPlanterPickupables.Count}");
            moveSessionWaterParkDestination = null;
            if (!moveSessionWaterParkUseVanillaFauna)
            {
                ParkCapturedWaterParkPickupables();
            }

            ParkCapturedWaterParkPlanterPickupables();
        }
        else if (recipeType == TechType.BaseBioReactor || recipeType == TechType.BaseNuclearReactor)
        {
            GameObject reactorRoot = null;
            if (moveSessionOriginalBase != null && moveSessionOriginalFace != null)
            {
                IBaseModule module = moveSessionOriginalBase.GetModule(moveSessionOriginalFace.Value);
                Component moduleComponent = module as Component;
                if (moduleComponent != null)
                {
                    reactorRoot = moduleComponent.gameObject;
                }
            }

            if (reactorRoot == null)
            {
                Constructable fallback = baseDecon.GetComponentInParent<Constructable>();
                if (fallback != null)
                {
                    reactorRoot = fallback.gameObject;
                }
            }

            if (!CaptureReactorItemsFromRoot(reactorRoot, recipeType))
            {
                ErrorMessage.AddMessage(L("No se puede mover: reactor en uso (combustible bloqueado)", "Cannot move: reactor is in use (fuel is locked)"));
                ClearMoveSession();
                return true;
            }
            moveSessionReactorSourceRoot = reactorRoot;
            moveSessionReactorDestinationRoot = null;
        }
        else if (recipeType == TechType.BaseFiltrationMachine)
        {
            GameObject filtrationRoot = null;
            if (moveSessionOriginalBase != null && moveSessionOriginalFace != null)
            {
                IBaseModule module = moveSessionOriginalBase.GetModule(moveSessionOriginalFace.Value);
                Component moduleComponent = module as Component;
                if (moduleComponent != null)
                {
                    filtrationRoot = moduleComponent.gameObject;
                }
            }

            if (filtrationRoot == null)
            {
                Constructable fallback = baseDecon.GetComponentInParent<Constructable>();
                if (fallback != null)
                {
                    filtrationRoot = fallback.gameObject;
                }
            }

            CaptureFiltrationItemsFromRoot(filtrationRoot);
            moveSessionFiltrationSourceRoot = filtrationRoot;
            moveSessionFiltrationDestinationRoot = null;
        }

        Log.LogInfo("TryMoveBaseFacePiece: Calling Builder.ResetLast()");
        Builder.ResetLast();

        if (!baseDecon.DeconstructionAllowed(out string reason))
        {
            if (!string.IsNullOrEmpty(reason))
            {
                ErrorMessage.AddMessage(reason);
            }

            Log.LogWarning($"TryMoveBaseFacePiece: DeconstructionAllowed=false reason='{reason}'");
            ClearMoveSession();
            return true;
        }

        Log.LogInfo("TryMoveBaseFacePiece: Calling baseDecon.Deconstruct()");
        baseDecon.Deconstruct();
        Log.LogInfo("TryMoveBaseFacePiece: Deconstruct() completed");

        ConstructableBase[] afterCB = Object.FindObjectsOfType<ConstructableBase>();
        moveSessionConstructableBase = null;
        for (int i = 0; i < afterCB.Length; i++)
        {
            ConstructableBase cb = afterCB[i];
            if (cb != null && cb.techType == recipeType && !cb.constructed)
            {
                moveSessionConstructableBase = cb;
                Log.LogInfo($"TryMoveBaseFacePiece: Found transient ConstructableBase '{cb.gameObject.name}'");
                break;
            }
        }

        if (Instance != null)
        {
            Instance.StartCoroutine(BeginPlacingAsync(moveTechType));
        }

        return true;
    }

    private static void CaptureFiltrationItemsFromRoot(GameObject filtrationRoot)
    {
        if (moveSessionFiltrationPickupables == null)
        {
            moveSessionFiltrationPickupables = new List<Pickupable>();
        }
        else
        {
            moveSessionFiltrationPickupables.Clear();
        }

        if (filtrationRoot == null)
        {
            return;
        }

        StorageContainer storage = filtrationRoot.GetComponentInChildren<StorageContainer>(true)
            ?? filtrationRoot.GetComponentInParent<StorageContainer>();
        ItemsContainer container = storage?.container;
        if (container == null)
        {
            return;
        }

        List<Pickupable> snapshot = new List<Pickupable>();
        foreach (InventoryItem inventoryItem in container)
        {
            if (inventoryItem?.item != null)
            {
                snapshot.Add(inventoryItem.item);
            }
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            Pickupable pickupable = snapshot[i];
            if (pickupable == null)
            {
                continue;
            }

            if (container.RemoveItem(pickupable, true))
            {
                moveSessionFiltrationPickupables.Add(pickupable);
            }
        }

        Transform parkingRoot = GetOrCreateMoveSessionParkingRoot();
        for (int i = 0; i < moveSessionFiltrationPickupables.Count; i++)
        {
            Pickupable pickupable = moveSessionFiltrationPickupables[i];
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

    private static void QueueFiltrationRestore(GameObject filtrationRoot, List<Pickupable> pickupables)
    {
        if (filtrationRoot == null || pickupables == null || pickupables.Count == 0)
        {
            return;
        }

        List<Pickupable> pending = new List<Pickupable>(pickupables);
        if (TryRestoreFiltrationItemsIntoRoot(filtrationRoot, pending))
        {
            return;
        }

        if (Instance != null)
        {
            Instance.StartCoroutine(RestoreFiltrationItemsIntoRootDeferred(filtrationRoot, pending));
        }
    }

    private static IEnumerator RestoreFiltrationItemsIntoRootDeferred(GameObject filtrationRoot, List<Pickupable> pending)
    {
        if (filtrationRoot == null || pending == null || pending.Count == 0)
        {
            yield break;
        }

        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames && pending.Count > 0; frame++)
        {
            TryRestoreFiltrationItemsIntoRoot(filtrationRoot, pending);
            yield return null;
        }
    }

    private static bool TryRestoreFiltrationItemsIntoRoot(GameObject filtrationRoot, List<Pickupable> pending)
    {
        if (filtrationRoot == null || pending == null || pending.Count == 0)
        {
            return true;
        }

        StorageContainer storage = filtrationRoot.GetComponentInChildren<StorageContainer>(true)
            ?? filtrationRoot.GetComponentInParent<StorageContainer>();
        ItemsContainer container = storage?.container;
        if (container == null)
        {
            return false;
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
            InventoryItem added = new InventoryItem(pickupable);
            container.UnsafeAdd(added);
            if (added.container == container)
            {
                pending.RemoveAt(i);
            }
        }

        MarkFiltrationGeometryDirty(filtrationRoot);
        return pending.Count == 0;
    }

    private static void MarkFiltrationGeometryDirty(GameObject filtrationRoot)
    {
        if (filtrationRoot == null)
        {
            return;
        }

        BaseFiltrationMachineGeometry geometry = filtrationRoot.GetComponentInChildren<BaseFiltrationMachineGeometry>(true)
            ?? filtrationRoot.GetComponentInParent<BaseFiltrationMachineGeometry>();
        geometry?.SetDirty();
    }

    private static IEnumerator RefreshChargerVisualsDeferred(GameObject chargerRoot)
    {
        if (chargerRoot == null)
        {
            yield break;
        }

        const int maxFrames = 30;
        for (int i = 0; i < maxFrames; i++)
        {
            MonoBehaviour[] components = chargerRoot.GetComponentsInChildren<MonoBehaviour>(true);
            Component charger = null;
            for (int c = 0; c < components.Length; c++)
            {
                MonoBehaviour candidate = components[c];
                if (candidate == null)
                {
                    continue;
                }

                string typeName = candidate.GetType().Name;
                if (typeName == "BatteryCharger" || typeName == "PowerCellCharger")
                {
                    charger = candidate;
                    break;
                }
            }

            if (charger != null)
            {
                System.Reflection.MethodInfo hasChargables = charger.GetType().BaseType?.GetMethod("HasChargables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                System.Reflection.MethodInfo updateVisuals = charger.GetType().BaseType?.GetMethod("UpdateVisuals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, System.Type.EmptyTypes, null);
                System.Reflection.MethodInfo toggleUi = charger.GetType().BaseType?.GetMethod("ToggleUI", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                System.Reflection.FieldInfo openedField = charger.GetType().BaseType?.GetField("opened", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                System.Reflection.FieldInfo animatorField = charger.GetType().BaseType?.GetField("animator", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                System.Reflection.FieldInfo animParamOpenField = charger.GetType().BaseType?.GetField("animParamOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                bool hasItems = hasChargables != null && (bool)hasChargables.Invoke(charger, null);
                if (hasItems)
                {
                    openedField?.SetValue(charger, true);
                    Component animator = animatorField?.GetValue(charger) as Component;
                    object hashObj = animParamOpenField?.GetValue(charger);
                    if (animator != null && hashObj is int hash)
                    {
                        System.Reflection.MethodInfo setBool = animator.GetType().GetMethod("SetBool", new[] { typeof(int), typeof(bool) });
                        setBool?.Invoke(animator, new object[] { hash, true });
                    }

                    updateVisuals?.Invoke(charger, null);
                    toggleUi?.Invoke(charger, new object[] { true });
                }

                yield break;
            }

            yield return null;
        }
    }

    private static void QueueChargerVisualRefresh(GameObject chargerRoot)
    {
        if (chargerRoot == null || Instance == null)
        {
            return;
        }

        Instance.StartCoroutine(RefreshChargerVisualsDeferred(chargerRoot));
    }
}
