using HarmonyLib;
using System.Collections;
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

        if (constructable == null || !IsInteriorFacePieceTechType(constructable.techType))
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

        if (IsInteriorFacePieceTechType(techType))
        {
            return settings.AllowInteriorPieces;
        }

        if (techType == TechType.Locker || techType == TechType.SmallLocker)
        {
            return true;
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
                return settings.AllowExteriorModules;
            case TechGroup.Miscellaneous:
                return settings.AllowMiscellaneous;
            default:
                return false;
        }
    }

    private static bool IsInteriorFacePieceTechType(TechType techType)
    {
        switch (techType)
        {
            case TechType.BaseWindow:
            case TechType.BaseHatch:
            case TechType.BaseLadder:
            case TechType.BaseReinforcement:
            case TechType.BaseBulkhead:
            case TechType.BasePartition:
            case TechType.BasePartitionDoor:
            case TechType.BasePlanter:
            case TechType.BaseFiltrationMachine:
            case TechType.BaseWaterPark:
            case TechType.BaseBioReactor:
            case TechType.BaseNuclearReactor:
                return true;
            default:
                return false;
        }
    }
}
