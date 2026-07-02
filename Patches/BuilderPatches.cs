using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    [HarmonyPatch(typeof(Builder), "TryPlace")]
    private static class Builder_TryPlace_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!moveSessionActive || moveSessionSuppressPlaceUntilFrame < 0)
            {
                return true;
            }

            if (Time.frameCount > moveSessionSuppressPlaceUntilFrame)
            {
                moveSessionSuppressPlaceUntilFrame = -1;
                return true;
            }

            __result = false;
            return false;
        }

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
}
