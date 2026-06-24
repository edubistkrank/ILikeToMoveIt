using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private static bool IsFloatingLockerMoveTarget(GameObject target, out Pickupable pickupable, out GameObject root)
    {
        pickupable = null;
        root = null;
        if (target == null)
        {
            return false;
        }

        Pickupable candidate = target.GetComponentInParent<Pickupable>()
            ?? target.GetComponent<Pickupable>()
            ?? target.GetComponentInChildren<Pickupable>(true);
        if (candidate == null)
        {
            return false;
        }

        if (candidate.GetTechType() != TechType.SmallStorage)
        {
            return false;
        }

        Constructable constructable = candidate.GetComponentInParent<Constructable>();
        if (constructable != null)
        {
            return false;
        }

        root = candidate.gameObject;
        pickupable = candidate;
        return true;
    }

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

    private static bool TryBeginFloatingLockerMove(Pickupable pickupable, GameObject root)
    {
        if (pickupable == null || root == null || moveSessionActive)
        {
            return false;
        }

        BeginMoveSessionCore(
            pickupable.GetTechType(),
            root.transform.position,
            root.transform.rotation,
            root,
            MoveBackend.FloatingLocker);

        moveSessionFloatingPickupable = pickupable;
        moveSessionFloatingWasPickupable = pickupable.isPickupable;
        pickupable.isPickupable = false;

        Component rigidbody = root.GetComponent("Rigidbody");
        moveSessionFloatingRigidbody = rigidbody;
        if (rigidbody != null)
        {
            System.Type rigidbodyType = rigidbody.GetType();
            System.Reflection.PropertyInfo isKinematicProp = rigidbodyType.GetProperty("isKinematic");
            System.Reflection.PropertyInfo useGravityProp = rigidbodyType.GetProperty("useGravity");
            System.Reflection.PropertyInfo velocityProp = rigidbodyType.GetProperty("velocity");
            System.Reflection.PropertyInfo angularVelocityProp = rigidbodyType.GetProperty("angularVelocity");

            moveSessionFloatingRigidbodyWasKinematic = isKinematicProp != null && (bool)isKinematicProp.GetValue(rigidbody, null);
            moveSessionFloatingRigidbodyUsedGravity = useGravityProp != null && (bool)useGravityProp.GetValue(rigidbody, null);

            isKinematicProp?.SetValue(rigidbody, true, null);
            useGravityProp?.SetValue(rigidbody, false, null);
            velocityProp?.SetValue(rigidbody, Vector3.zero, null);
            angularVelocityProp?.SetValue(rigidbody, Vector3.zero, null);
        }

        moveSessionFloatingColliders.Clear();
        moveSessionFloatingColliderEnabledStates.Clear();
        System.Type colliderType = System.Type.GetType("UnityEngine.Collider, UnityEngine.PhysicsModule");
        Component[] colliders = colliderType != null
            ? root.GetComponentsInChildren(colliderType, true)
            : new Component[0];
        for (int i = 0; i < colliders.Length; i++)
        {
            Component collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            System.Reflection.PropertyInfo enabledProp = collider.GetType().GetProperty("enabled");
            bool wasEnabled = enabledProp != null && (bool)enabledProp.GetValue(collider, null);
            moveSessionFloatingColliders.Add(collider);
            moveSessionFloatingColliderEnabledStates.Add(wasEnabled);
            enabledProp?.SetValue(collider, false, null);
        }

        return true;
    }

    private static void UpdateFloatingLockerPreview()
    {
        if (!moveSessionActive || moveBackend != MoveBackend.FloatingLocker || moveOriginalObject == null)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 origin = camera.transform.position;
        Vector3 forward = camera.transform.forward;
        Vector3 targetPosition = origin + forward * 3f;

        moveOriginalObject.transform.position = targetPosition;
        moveOriginalObject.transform.rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);
    }

    private static void RestoreFloatingLockerState()
    {
        if (moveSessionFloatingPickupable != null)
        {
            moveSessionFloatingPickupable.isPickupable = moveSessionFloatingWasPickupable;
        }

        if (moveSessionFloatingRigidbody != null)
        {
            System.Type rigidbodyType = moveSessionFloatingRigidbody.GetType();
            System.Reflection.PropertyInfo isKinematicProp = rigidbodyType.GetProperty("isKinematic");
            System.Reflection.PropertyInfo useGravityProp = rigidbodyType.GetProperty("useGravity");
            isKinematicProp?.SetValue(moveSessionFloatingRigidbody, moveSessionFloatingRigidbodyWasKinematic, null);
            useGravityProp?.SetValue(moveSessionFloatingRigidbody, moveSessionFloatingRigidbodyUsedGravity, null);
        }

        int count = moveSessionFloatingColliders.Count;
        for (int i = 0; i < count; i++)
        {
            Component collider = moveSessionFloatingColliders[i];
            if (collider == null)
            {
                continue;
            }

            bool enabled = i < moveSessionFloatingColliderEnabledStates.Count
                ? moveSessionFloatingColliderEnabledStates[i]
                : true;
            System.Reflection.PropertyInfo enabledProp = collider.GetType().GetProperty("enabled");
            enabledProp?.SetValue(collider, enabled, null);
        }

        moveSessionFloatingColliders.Clear();
        moveSessionFloatingColliderEnabledStates.Clear();
        moveSessionFloatingPickupable = null;
        moveSessionFloatingRigidbody = null;
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

    [HarmonyPatch(typeof(BuilderTool), "OnLeftHandDown")]
    private static class BuilderTool_OnLeftHandDown_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (moveSessionActive && moveBackend == MoveBackend.FloatingLocker)
            {
                moveSessionCommitted = true;
                Builder.End();
                __result = false;
                return false;
            }

            if (Builder.isPlacing)
            {
                return true;
            }

            if (!IsMoveModifierHeld())
            {
                return true;
            }

            __result = TryMoveTargetedLocker();
            return false;
        }
    }

    [HarmonyPatch(typeof(BuilderTool), "OnRightHandDown")]
    private static class BuilderTool_OnRightHandDown_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!moveSessionActive || moveBackend != MoveBackend.FloatingLocker)
            {
                return true;
            }

            moveSessionCommitted = false;
            Builder.End();
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Builder), "TryPlace")]
    private static class Builder_TryPlace_Patch
    {
        private static void Postfix(bool __result)
        {
            if (!moveSessionActive || !__result)
            {
                return;
            }

            if (moveBackend == MoveBackend.Face)
            {
                Log.LogInfo("Builder_TryPlace_Patch: Face piece placed by vanilla, marking committed");
                InstantBuildMovedFacePiece();

                if ((moveTechType == TechType.BaseBioReactor || moveTechType == TechType.BaseNuclearReactor)
                    && moveSessionReactorPickupables != null
                    && moveSessionReactorPickupables.Count > 0)
                {
                    GameObject destinationRoot = moveSessionReactorDestinationRoot;
                    if (destinationRoot != null)
                    {
                        QueueReactorRestore(destinationRoot, moveTechType, moveSessionReactorPickupables);
                    }
                }

                if (moveTechType == TechType.BaseFiltrationMachine
                    && moveSessionFiltrationPickupables != null
                    && moveSessionFiltrationPickupables.Count > 0)
                {
                    GameObject destinationFiltrationRoot = moveSessionFiltrationDestinationRoot;
                    if (destinationFiltrationRoot != null)
                    {
                        QueueFiltrationRestore(destinationFiltrationRoot, moveSessionFiltrationPickupables);
                    }
                }

                if (moveTechType == TechType.BaseWaterPark)
                {
                    Log.LogInfo($"WaterPark: FinalizeMovedWaterPark, destination={moveSessionWaterParkDestination}, fauna={moveSessionWaterParkPickupables.Count}, flora={moveSessionWaterParkPlanterPickupables.Count}");
                    FinalizeMovedWaterPark();
                }

                if (moveSessionConstructableBase != null)
                {
                    Object.Destroy(moveSessionConstructableBase.gameObject);
                    moveSessionConstructableBase = null;
                }

                moveSessionCommitted = true;
                moveSessionStartingPlacement = false;
                Builder.ResetLast();
                Builder.End();
                return;
            }

            HandleRegularItemPlacement();

            moveSessionCommitted = true;
            moveSessionStartingPlacement = false;
            Builder.ResetLast();
            Builder.End();
        }

        private static void Prefix()
        {
            if (!moveSessionActive)
            {
                return;
            }

            GameObject ghost = Builder.GetGhostModel();
            if (ghost != null)
            {
                moveHasLastGhostTransform = true;
                moveLastGhostPosition = ghost.transform.position;
                moveLastGhostRotation = ghost.transform.rotation;
            }
        }

        private static void HandleRegularItemPlacement()
        {
            GameObject originalObject = moveOriginalObject;
            if (originalObject == null)
            {
                return;
            }

            GameObject ghost = Builder.GetGhostModel();
            Vector3 targetPosition;
            Quaternion targetRotation;

            if (ghost != null)
            {
                targetPosition = ghost.transform.position;
                targetRotation = ghost.transform.rotation;
            }
            else if (moveHasLastGhostTransform)
            {
                targetPosition = moveLastGhostPosition;
                targetRotation = moveLastGhostRotation;
            }
            else
            {
                targetPosition = moveOriginalPosition;
                targetRotation = moveOriginalRotation;
            }

            Constructable placed = FindPlacedUnconstructedLocker(moveTechType, targetPosition);
            if (placed != null)
            {
                targetPosition = placed.transform.position;
                targetRotation = placed.transform.rotation;
                Object.Destroy(placed.gameObject);
            }

            originalObject.transform.position = targetPosition;
            originalObject.transform.rotation = targetRotation;
            originalObject.SetActive(true);

            if (moveTechType == TechType.BatteryCharger || moveTechType == TechType.PowerCellCharger)
            {
                QueueChargerVisualRefresh(originalObject);
            }

            if ((moveTechType == TechType.BaseBioReactor || moveTechType == TechType.BaseNuclearReactor)
                && moveSessionReactorPickupables != null
                && moveSessionReactorPickupables.Count > 0)
            {
                QueueReactorRestore(originalObject, moveTechType, moveSessionReactorPickupables);
            }
        }
    }

    [HarmonyPatch(typeof(Builder), "End")]
    private static class Builder_End_Patch
    {
        private static void Postfix()
        {
            if (!moveSessionActive || moveSessionStartingPlacement)
            {
                return;
            }

            bool committed = moveSessionCommitted;
            GameObject originalObject = moveOriginalObject;
            Vector3 originalPosition = moveOriginalPosition;
            Quaternion originalRotation = moveOriginalRotation;
            MoveBackend backend = moveBackend;
            Base originalBase = moveSessionOriginalBase;
            Base.Face? originalFace = moveSessionOriginalFace;
            Base.FaceType originalFaceType = moveSessionOriginalFaceType;
            TechType techType = moveTechType;
            ConstructableBase transientConstructable = moveSessionConstructableBase;
            GameObject reactorSourceRoot = moveSessionReactorSourceRoot;
            GameObject reactorDestinationRoot = moveSessionReactorDestinationRoot;

            WaterPark waterParkSource = moveSessionWaterParkSource;
            List<WaterParkItem> waterParkItems = new List<WaterParkItem>(moveSessionWaterParkItems);
            List<Pickupable> waterParkPickupables = new List<Pickupable>(moveSessionWaterParkPickupables);
            List<Pickupable> waterParkPlanterPickupables = new List<Pickupable>(moveSessionWaterParkPlanterPickupables);
            List<Pickupable> reactorPickupables = moveSessionReactorPickupables != null
                ? new List<Pickupable>(moveSessionReactorPickupables)
                : new List<Pickupable>();
            List<Pickupable> filtrationPickupables = moveSessionFiltrationPickupables != null
                ? new List<Pickupable>(moveSessionFiltrationPickupables)
                : new List<Pickupable>();
            bool hasWaterParkPayload = waterParkSource != null
                || waterParkItems.Count > 0
                || waterParkPickupables.Count > 0
                || waterParkPlanterPickupables.Count > 0;

            if (committed)
            {
                if (backend == MoveBackend.FloatingLocker)
                {
                    RestoreFloatingLockerState();
                    ClearMoveSession();
                    return;
                }

                if (backend == MoveBackend.Face
                    && (techType == TechType.BaseBioReactor || techType == TechType.BaseNuclearReactor)
                    && reactorSourceRoot != null
                    && !ReferenceEquals(reactorSourceRoot, reactorDestinationRoot))
                {
                    Object.Destroy(reactorSourceRoot);
                }

                if (backend == MoveBackend.Face
                    && techType == TechType.BaseFiltrationMachine
                    && moveSessionFiltrationSourceRoot != null
                    && !ReferenceEquals(moveSessionFiltrationSourceRoot, moveSessionFiltrationDestinationRoot))
                {
                    Object.Destroy(moveSessionFiltrationSourceRoot);
                }

                if (transientConstructable != null)
                {
                    Object.Destroy(transientConstructable.gameObject);
                }

                ClearMoveSession();
                return;
            }

            if (backend == MoveBackend.Face)
            {
                if (originalBase != null && originalFace != null)
                {
                    try
                    {
                        if (hasWaterParkPayload || techType == TechType.BaseWaterPark || originalFaceType == Base.FaceType.WaterPark)
                        {
                            if (transientConstructable != null)
                            {
                                transientConstructable.gameObject.SetActive(true);
                                transientConstructable.SetState(true, true);

                                WaterPark restoredAtOrigin = originalBase.GetModule(originalFace.Value) as WaterPark;
                                if (restoredAtOrigin != null)
                                {
                                    ForceWaterParkVisible(restoredAtOrigin);
                                    Base destinationBase = restoredAtOrigin.GetComponentInParent<Base>() ?? originalBase;
                                    bool destroySource = waterParkSource != null && !ReferenceEquals(waterParkSource, restoredAtOrigin);
                                    CompleteWaterParkTransfer(waterParkSource, restoredAtOrigin, originalBase, destinationBase, waterParkItems, waterParkPickupables, waterParkPlanterPickupables, false, destroySource);
                                }
                                else
                                {
                                    RestoreCanceledWaterPark(originalBase, originalFace.Value, waterParkSource, waterParkItems, waterParkPickupables, waterParkPlanterPickupables);
                                }
                            }
                            else
                            {
                                RestoreCanceledFacePiece(originalBase, originalFace.Value, originalFaceType);
                                RestoreCanceledWaterPark(originalBase, originalFace.Value, waterParkSource, waterParkItems, waterParkPickupables, waterParkPlanterPickupables);
                            }
                        }
                        else
                        {
                            RestoreCanceledFacePiece(originalBase, originalFace.Value, originalFaceType);
                            Log.LogInfo($"Builder_End_Patch: restored canceled face piece {originalFaceType}");

                            if ((techType == TechType.BaseBioReactor || techType == TechType.BaseNuclearReactor)
                                && reactorPickupables.Count > 0)
                            {
                                GameObject originRoot = GetModuleRootFromBaseFace(originalBase, originalFace.Value);
                                QueueReactorRestore(originRoot, techType, reactorPickupables);
                            }

                            if (techType == TechType.BaseFiltrationMachine
                                && filtrationPickupables.Count > 0)
                            {
                                GameObject originFiltrationRoot = GetModuleRootFromBaseFace(originalBase, originalFace.Value);
                                QueueFiltrationRestore(originFiltrationRoot, filtrationPickupables);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.LogError($"Builder_End_Patch restore failed: {ex.Message}");
                    }
                }

                if (transientConstructable != null)
                {
                    Object.Destroy(transientConstructable.gameObject);
                }

                ClearMoveSession();
                return;
            }

            if (originalObject != null)
            {
                originalObject.transform.position = originalPosition;
                originalObject.transform.rotation = originalRotation;
                originalObject.SetActive(true);

                if ((techType == TechType.BaseBioReactor || techType == TechType.BaseNuclearReactor)
                    && moveSessionReactorPickupables != null
                    && moveSessionReactorPickupables.Count > 0)
                {
                    QueueReactorRestore(originalObject, techType, moveSessionReactorPickupables);
                }

                if (techType == TechType.BatteryCharger || techType == TechType.PowerCellCharger)
                {
                    QueueChargerVisualRefresh(originalObject);
                }
            }

            if (transientConstructable != null)
            {
                Object.Destroy(transientConstructable.gameObject);
            }

            if (backend == MoveBackend.FloatingLocker)
            {
                RestoreFloatingLockerState();
                ClearMoveSession();
                return;
            }

            ClearMoveSession();
        }
    }

    [HarmonyPatch(typeof(BuilderTool), "OnHover", typeof(Constructable))]
    private static class BuilderTool_OnHover_Patch
    {
        private static void Postfix(Constructable constructable)
        {
            if (!IsMoveModifierHeld() || !IsMovableBySettings(constructable))
            {
                return;
            }

            HandReticle main = HandReticle.main;
            string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
            main.SetText(HandReticle.TextType.HandSubscript, $"{L("Mover", "Move")} (Alt + {left})", false, GameInput.Button.None);
            main.SetIcon(HandReticle.IconType.Hand, 1f);
            SetMoveReticleIcon(true);
        }
    }

    [HarmonyPatch(typeof(BuilderTool), "Update")]
    private static class BuilderTool_Update_Patch
    {
        private static void Postfix()
        {
            if (moveSessionActive && moveBackend == MoveBackend.FloatingLocker)
            {
                UpdateFloatingLockerPreview();

                HandReticle mainReticle = HandReticle.main;
                if (mainReticle != null)
                {
                    mainReticle.SetText(HandReticle.TextType.Hand, L("Mover contenedor impermeable", "Move waterproof locker"), false, GameInput.Button.LeftHand);
                    mainReticle.SetText(HandReticle.TextType.HandSubscript, string.Empty, false, GameInput.Button.None);
                    mainReticle.SetIcon(HandReticle.IconType.Hand, 1f);
                }

                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    moveSessionCommitted = false;
                    Builder.End();
                    return;
                }

                SetMoveReticleIcon(true);
                return;
            }

            if (!IsMoveModifierHeld() || Builder.isPlacing || moveSessionActive)
            {
                SetMoveReticleIcon(false);
                return;
            }

            Targeting.AddToIgnoreList(Player.main.gameObject);
            Targeting.GetTarget(30f, out GameObject target, out float distance);
            if (target == null)
            {
                SetMoveReticleIcon(false);
                return;
            }

            BaseDeconstructable baseDecon = target.GetComponentInParent<BaseDeconstructable>();
            if (IsTransientFaceDeconstructable(baseDecon))
            {
                SetMoveReticleIcon(false);
                return;
            }

            if (baseDecon != null && distance <= 11f)
            {
                TechType recipeType = GetBaseDeconstructableTechType(baseDecon);
                if (recipeType != TechType.None && IsMovableBySettings(recipeType))
                {
                    string name = Language.main.Get(recipeType.AsString());
                    HandReticle main = HandReticle.main;
                    string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
                    main.SetText(HandReticle.TextType.Hand, name, false, GameInput.Button.None);
                    main.SetText(HandReticle.TextType.HandSubscript, $"{L("Mover", "Move")} (Alt + {left})", false, GameInput.Button.None);
                    main.SetIcon(HandReticle.IconType.Hand, 1f);
                    SetMoveReticleIcon(true);
                    return;
                }
            }

            Constructable constructable = target.GetComponentInParent<Constructable>();
            if (constructable != null && IsMovableBySettings(constructable))
            {
                HandReticle main = HandReticle.main;
                string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
                main.SetText(HandReticle.TextType.HandSubscript, $"{L("Mover", "Move")} (Alt + {left})", false, GameInput.Button.None);
                main.SetIcon(HandReticle.IconType.Hand, 1f);
                SetMoveReticleIcon(true);
                return;
            }

            if (IsFloatingLockerMoveTarget(target, out _, out _) && distance <= 11f)
            {
                HandReticle main = HandReticle.main;
                string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
                main.SetText(HandReticle.TextType.Hand, L("Contenedor impermeable", "Waterproof locker"), false, GameInput.Button.None);
                main.SetText(HandReticle.TextType.HandSubscript, $"{L("Mover", "Move")} (Alt + {left})", false, GameInput.Button.None);
                main.SetIcon(HandReticle.IconType.Hand, 1f);
                SetMoveReticleIcon(true);
                return;
            }

            SetMoveReticleIcon(false);
        }
    }

    [HarmonyPatch(typeof(Inventory), "DestroyItem", new[] { typeof(TechType), typeof(bool) })]
    private static class Inventory_DestroyItem_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!moveSessionActive || !moveSessionIsFacePiece || !bypassResourceConsumption)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Base), "SpawnModule", new[] { typeof(GameObject), typeof(Base.Face) })]
    private static class Base_SpawnModule_FiltrationTrack_Patch
    {
        private static void Postfix(GameObject __result)
        {
            if (!moveSessionActive || moveBackend != MoveBackend.Face || __result == null)
            {
                return;
            }

            if (moveTechType != TechType.BaseFiltrationMachine)
            {
                return;
            }

            moveSessionFiltrationDestinationRoot = __result;
        }
    }

    [HarmonyPatch(typeof(BaseFiltrationMachineGeometry), "CanDeconstruct")]
    private static class BaseFiltrationMachineGeometry_CanDeconstruct_Patch
    {
        private static bool Prefix(ref bool __result, ref string reason)
        {
            if (!moveSessionActive || moveBackend != MoveBackend.Face || moveTechType != TechType.BaseFiltrationMachine)
            {
                return true;
            }

            reason = null;
            __result = true;
            return false;
        }
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

    [HarmonyPatch(typeof(Constructable), "SetState", new[] { typeof(bool), typeof(bool) })]
    private static class Constructable_SetState_ChargerVisuals_Patch
    {
        private static void Postfix(Constructable __instance, bool value)
        {
            if (!value || __instance == null)
            {
                return;
            }

            if (__instance.techType != TechType.BatteryCharger && __instance.techType != TechType.PowerCellCharger)
            {
                return;
            }

            if (Instance != null)
            {
                Instance.StartCoroutine(RefreshChargerVisualsDeferred(__instance.gameObject));
            }
        }
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
