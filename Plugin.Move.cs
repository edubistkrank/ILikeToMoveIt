using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
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
            if (constructable.techType == TechType.BaseBioReactor
                && Settings != null
                && Settings.PreventMoveBioReactorIfNotEmpty
                && ReactorHasContentOnRoot(constructable.gameObject, TechType.BaseBioReactor))
            {
                ErrorMessage.AddMessage(L("No se puede mover: BioReactor con combustible", "Cannot move: BioReactor contains fuel"));
                return true;
            }

            if (constructable.techType == TechType.BaseNuclearReactor
                && Settings != null
                && Settings.PreventMoveNuclearReactorIfNotEmpty
                && ReactorHasContentOnRoot(constructable.gameObject, TechType.BaseNuclearReactor))
            {
                ErrorMessage.AddMessage(L("No se puede mover: NuclearReactor con barras", "Cannot move: NuclearReactor contains rods"));
                return true;
            }

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

            if (recipeType == TechType.BaseBioReactor
                && Settings != null
                && Settings.PreventMoveBioReactorIfNotEmpty
                && ReactorHasContentOnRoot(reactorRoot, recipeType))
            {
                ErrorMessage.AddMessage(L("No se puede mover: BioReactor con combustible", "Cannot move: BioReactor contains fuel"));
                ClearMoveSession();
                return true;
            }

            if (recipeType == TechType.BaseNuclearReactor
                && Settings != null
                && Settings.PreventMoveNuclearReactorIfNotEmpty
                && ReactorHasContentOnRoot(reactorRoot, recipeType))
            {
                ErrorMessage.AddMessage(L("No se puede mover: NuclearReactor con barras", "Cannot move: NuclearReactor contains rods"));
                ClearMoveSession();
                return true;
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
            bool hasWaterParkPayload = waterParkSource != null
                || waterParkItems.Count > 0
                || waterParkPickupables.Count > 0
                || waterParkPlanterPickupables.Count > 0;

            if (committed)
            {
                if (backend == MoveBackend.Face
                    && (techType == TechType.BaseBioReactor || techType == TechType.BaseNuclearReactor)
                    && reactorSourceRoot != null
                    && !ReferenceEquals(reactorSourceRoot, reactorDestinationRoot))
                {
                    Object.Destroy(reactorSourceRoot);
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
                                originalBase.SetFaceType(originalFace.Value, originalFaceType);
                                originalBase.RebuildGeometry();
                                RestoreCanceledWaterPark(originalBase, originalFace.Value, waterParkSource, waterParkItems, waterParkPickupables, waterParkPlanterPickupables);
                            }
                        }
                        else
                        {
                            originalBase.SetFaceType(originalFace.Value, originalFaceType);
                            originalBase.RebuildGeometry();
                            Log.LogInfo($"Builder_End_Patch: restored canceled face piece {originalFaceType}");

                            if ((techType == TechType.BaseBioReactor || techType == TechType.BaseNuclearReactor)
                                && reactorPickupables.Count > 0)
                            {
                                GameObject originRoot = GetModuleRootFromBaseFace(originalBase, originalFace.Value);
                                QueueReactorRestore(originRoot, techType, reactorPickupables);
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
            }

            if (transientConstructable != null)
            {
                Object.Destroy(transientConstructable.gameObject);
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
}
