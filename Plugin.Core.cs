using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private static bool IsMovableBySettings(Constructable constructable)
    {
        if (constructable == null || !constructable.constructed)
        {
            return false;
        }

        return IsMovableBySettings(constructable.techType);
    }

    private static bool IsTransientFaceDeconstructable(BaseDeconstructable baseDecon)
    {
        if (baseDecon == null)
        {
            return false;
        }

        ConstructableBase constructableBase = baseDecon.GetComponentInParent<ConstructableBase>();
        return constructableBase != null && !constructableBase.constructed;
    }

    private static BaseDeconstructable FindFaceDeconstructableForTarget(GameObject target, Constructable constructable)
    {
        BaseDeconstructable baseDecon = target?.GetComponent<BaseDeconstructable>()
            ?? target?.GetComponentInParent<BaseDeconstructable>()
            ?? target?.GetComponentInChildren<BaseDeconstructable>(true)
            ?? constructable?.GetComponent<BaseDeconstructable>()
            ?? constructable?.GetComponentInParent<BaseDeconstructable>()
            ?? constructable?.GetComponentInChildren<BaseDeconstructable>(true);

        if (!IsTransientFaceDeconstructable(baseDecon))
        {
            return baseDecon;
        }

        baseDecon = null;

        if (constructable == null || (!IsInteriorPieceTechType(constructable.techType) && !IsBasePieceTechType(constructable.techType)))
        {
            return null;
        }

        Base constructableBase = constructable.GetComponentInParent<Base>();
        float maxDistSqr = 9f;
        Vector3 origin = constructable.transform.position;
        BaseDeconstructable[] all = Object.FindObjectsOfType<BaseDeconstructable>();
        for (int i = 0; i < all.Length; i++)
        {
            BaseDeconstructable candidate = all[i];
            if (candidate == null || IsTransientFaceDeconstructable(candidate))
            {
                continue;
            }

            TechType recipe = GetBaseDeconstructableTechType(candidate);
            if (recipe != constructable.techType)
            {
                continue;
            }

            if (constructableBase != null && candidate.GetComponentInParent<Base>() != constructableBase)
            {
                continue;
            }

            float distSqr = (candidate.transform.position - origin).sqrMagnitude;
            if (distSqr <= maxDistSqr)
            {
                baseDecon = candidate;
                maxDistSqr = distSqr;
            }
        }

        return baseDecon;
    }

    private static IEnumerator BeginPlacingAsync(TechType techType)
    {
        if (techType == TechType.None)
        {
            yield break;
        }

        moveSessionStartingPlacement = true;

        if (moveBackend == MoveBackend.Regular)
        {
            Log.LogInfo($"BeginPlacingAsync: Regular item - using Builder.BeginAsync({techType})");
            yield return Builder.BeginAsync(techType);
        }
        else
        {
            Log.LogInfo("BeginPlacingAsync: Face piece - cleaning transient deconstructable and opening blueprint");

            if (moveSessionConstructableBase != null && !moveSessionConstructableBase.constructed)
            {
                Log.LogInfo($"BeginPlacingAsync: Destroying transient ConstructableBase '{moveSessionConstructableBase.gameObject.name}' before Builder.BeginAsync");
                moveSessionConstructableBase.gameObject.SetActive(false);
                if (moveTechType != TechType.BaseWaterPark)
                {
                    Object.Destroy(moveSessionConstructableBase.gameObject);
                    moveSessionConstructableBase = null;
                }

                yield return null;
            }

            Builder.ResetLast();
            yield return Builder.BeginAsync(techType);
        }

        moveSessionStartingPlacement = false;
    }

    private static Constructable FindPlacedUnconstructedLocker(TechType techType, Vector3 position)
    {
        Constructable best = null;
        float bestDistSqr = 100f;
        Constructable[] all = Object.FindObjectsOfType<Constructable>();
        for (int i = 0; i < all.Length; i++)
        {
            Constructable c = all[i];
            if (c == null || c.techType != techType || c.constructed)
            {
                continue;
            }

            float distSqr = (c.transform.position - position).sqrMagnitude;
            if (distSqr <= bestDistSqr)
            {
                bestDistSqr = distSqr;
                best = c;
            }
        }

        return best;
    }

    private static void ClearMoveSession()
    {
        Log.LogInfo($"ClearMoveSession: Clearing state, backend={moveBackend}");
        moveSessionActive = false;
        moveSessionCommitted = false;
        moveSessionStartingPlacement = false;
        moveTechType = TechType.None;
        moveOriginalPosition = default;
        moveOriginalRotation = default;
        moveOriginalObject = null;
        moveSessionIsFacePiece = false;
        moveBackend = MoveBackend.Regular;
        moveSessionFacePieceSource = null;
        moveSessionConstructableBase = null;
        moveSessionOriginalBase = null;
        moveSessionOriginalFace = null;
        moveSessionOriginalFaceType = Base.FaceType.None;
        moveSessionWaterParkSource = null;
        moveSessionWaterParkDestination = null;
        moveSessionWaterParkUseVanillaFauna = false;
        if (moveSessionReactorPickupables != null)
        {
            moveSessionReactorPickupables.Clear();
        }

        moveSessionReactorTechType = TechType.None;
        moveSessionReactorSourceRoot = null;
        moveSessionReactorDestinationRoot = null;
        if (moveSessionFiltrationPickupables != null)
        {
            moveSessionFiltrationPickupables.Clear();
        }

        moveSessionFiltrationSourceRoot = null;
        moveSessionFiltrationDestinationRoot = null;
        moveSessionReactorStateCaptured = false;
        moveSessionReactorPower = 0f;
        moveSessionReactorToConsume = 0f;
        moveSessionWaterParkItems.Clear();
        moveSessionWaterParkPickupables.Clear();
        moveSessionWaterParkPlanterPickupables.Clear();
        bypassResourceConsumption = false;
        moveHasLastGhostTransform = false;
        moveLastGhostPosition = default;
        moveLastGhostRotation = default;
    }

    private static void InstantBuildMovedFacePiece()
    {
        ConstructableBase[] all = Object.FindObjectsOfType<ConstructableBase>();
        for (int i = 0; i < all.Length; i++)
        {
            ConstructableBase cb = all[i];
            if (cb != null && cb.techType == moveTechType && !cb.constructed)
            {
                Log.LogInfo($"InstantBuildMovedFacePiece: forcing built state on '{cb.gameObject.name}'");
                cb.SetState(true, true);
                return;
            }
        }

        Log.LogWarning("InstantBuildMovedFacePiece: no unconstructed ConstructableBase found");
    }

    private static void BeginMoveSessionCore(TechType techType, Vector3 originalPosition, Quaternion originalRotation, GameObject originalObject, MoveBackend backend)
    {
        moveSessionActive = true;
        moveSessionCommitted = false;
        moveSessionStartingPlacement = false;
        moveTechType = techType;
        moveOriginalPosition = originalPosition;
        moveOriginalRotation = originalRotation;
        moveOriginalObject = originalObject;
        moveBackend = backend;
        moveSessionIsFacePiece = backend == MoveBackend.Face;

        moveSessionFacePieceSource = null;
        moveSessionConstructableBase = null;
        moveSessionOriginalBase = null;
        moveSessionOriginalFace = null;
        moveSessionOriginalFaceType = Base.FaceType.None;
        moveSessionWaterParkSource = null;
        moveSessionWaterParkDestination = null;
        moveSessionWaterParkUseVanillaFauna = false;
        if (moveSessionReactorPickupables == null)
        {
            moveSessionReactorPickupables = new List<Pickupable>();
        }
        else
        {
            moveSessionReactorPickupables.Clear();
        }

        moveSessionReactorTechType = TechType.None;
        moveSessionReactorSourceRoot = null;
        moveSessionReactorDestinationRoot = null;
        if (moveSessionFiltrationPickupables == null)
        {
            moveSessionFiltrationPickupables = new List<Pickupable>();
        }
        else
        {
            moveSessionFiltrationPickupables.Clear();
        }

        moveSessionFiltrationSourceRoot = null;
        moveSessionFiltrationDestinationRoot = null;
        moveSessionReactorStateCaptured = false;
        moveSessionReactorPower = 0f;
        moveSessionReactorToConsume = 0f;
        moveSessionWaterParkItems.Clear();
        moveSessionWaterParkPickupables.Clear();
        moveSessionWaterParkPlanterPickupables.Clear();
        bypassResourceConsumption = false;
        moveHasLastGhostTransform = false;
        moveLastGhostPosition = default;
        moveLastGhostRotation = default;
    }

    [HarmonyPatch(typeof(BuilderTool), "GetCustomUseText")]
    private static class BuilderTool_GetCustomUseText_Patch
    {
        private static void Postfix(ref string __result)
        {
            string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
            string hint = $"Alt + {left}: {L("Mover locker", "Move locker")}";
            __result = string.IsNullOrEmpty(__result)
                ? hint
                : $"{__result}\n{hint}";
        }
    }

    private static TechType GetBaseDeconstructableTechType(BaseDeconstructable baseDecon)
    {
        if (baseDecon == null)
        {
            return TechType.None;
        }

        if (baseDeconstructableRecipeField != null)
        {
            object value = baseDeconstructableRecipeField.GetValue(baseDecon);
            if (value is TechType techType)
            {
                return techType;
            }
        }

        return TechType.None;
    }

    private static bool IsMovableBySettings(TechType techType)
    {
        ModConfig settings = Settings;
        if (settings == null)
        {
            return false;
        }

        if (IsBasePieceTechType(techType))
        {
            return settings.AllowBasePieces;
        }

        if (IsInteriorPieceTechType(techType))
        {
            return settings.AllowInteriorPieces;
        }

        if (IsInteriorModuleTechType(techType))
        {
            return settings.AllowInteriorModules;
        }

        if (IsExternalModuleTechType(techType))
        {
            return settings.AllowExternalModules;
        }

        if (IsMiscellaneousItemTechType(techType))
        {
            return settings.AllowMiscellaneousItems;
        }

        if (!CraftData.GetBuilderIndex(techType, out TechGroup group, out _, out _))
        {
            return false;
        }

        switch (group)
        {
            case TechGroup.InteriorModules:
                return settings.AllowInteriorModules;
            case TechGroup.ExteriorModules:
                return settings.AllowExternalModules;
            case TechGroup.Miscellaneous:
                return settings.AllowMiscellaneousItems;
            default:
                return false;
        }
    }

    private static bool IsBasePieceTechType(TechType techType)
    {
        switch (techType)
        {
            case TechType.BaseHatch:
            case TechType.BaseWindow:
            case TechType.BaseReinforcement:
                return true;
            default:
                return false;
        }
    }

    private static bool IsInteriorPieceTechType(TechType techType)
    {
        switch (techType)
        {
            case TechType.BaseLadder:
            case TechType.BaseBulkhead:
            case TechType.BasePartition:
            case TechType.BasePartitionDoor:
            case TechType.BaseFiltrationMachine:
            case TechType.BaseWaterPark:
            case TechType.BaseBioReactor:
            case TechType.BaseNuclearReactor:
            case TechType.BaseUpgradeConsole:
                return true;
            default:
                return false;
        }
    }

    private static bool IsExternalModuleTechType(TechType techType)
    {
        switch (techType)
        {
            case TechType.SolarPanel:
            case TechType.ThermalPlant:
            case TechType.PowerTransmitter:
            case TechType.Spotlight:
            case TechType.BasePipeConnector:
            case TechType.PipeSurfaceFloater:
                return true;
            default:
                return false;
        }
    }

    private static bool IsInteriorModuleTechType(TechType techType)
    {
        switch (techType)
        {
            case TechType.Fabricator:
            case TechType.Radio:
            case TechType.MedicalCabinet:
            case TechType.SmallLocker:
            case TechType.Locker:
            case TechType.BatteryCharger:
            case TechType.PowerCellCharger:
            case TechType.Aquarium:
            case TechType.Workbench:
            case TechType.PlanterPot:
            case TechType.PlanterPot2:
            case TechType.PlanterPot3:
            case TechType.PlanterBox:
            case TechType.PlanterShelf:
                return true;
            default:
                return false;
        }
    }

    private static bool IsMiscellaneousItemTechType(TechType techType)
    {
        switch (techType)
        {
            case TechType.Bench:
            case TechType.Bed1:
            case TechType.Bed2:
            case TechType.NarrowBed:
            case TechType.StarshipDesk:
            case TechType.StarshipChair:
            case TechType.StarshipChair2:
            case TechType.StarshipChair3:
            case TechType.Sign:
            case TechType.PictureFrame:
            case TechType.BarTable:
            case TechType.Trashcans:
            case TechType.LabTrashcan:
            case TechType.VendingMachine:
            case TechType.CoffeeVendingMachine:
            case TechType.LabCounter:
            case TechType.BasePlanter:
            case TechType.SingleWallShelf:
            case TechType.WallShelves:
            case TechType.JackSepticEye:
            case TechType.DioramaHullPlate:
            case TechType.MarkiplierHullPlate:
            case TechType.MuyskermHullPlate:
            case TechType.LordMinionHullPlate:
            case TechType.JackSepticEyeHullPlate:
            case TechType.IGPHullPlate:
            case TechType.GilathissHullPlate:
                return true;
            default:
                return false;
        }
    }
}
