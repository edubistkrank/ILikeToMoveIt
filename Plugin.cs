using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Handlers;
using System.Collections;
using UnityEngine;

namespace ILikeToMoveIt;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    private Harmony harmony;
    internal static ManualLogSource Log { get; private set; }
    private static Plugin Instance { get; set; }
    internal static ModConfig Settings { get; private set; }

    private static bool moveSessionActive;
    private static bool moveSessionCommitted;
    private static bool moveSessionStartingPlacement;
    private static TechType moveTechType = TechType.None;
    private static Vector3 moveOriginalPosition;
    private static Quaternion moveOriginalRotation;
    private static GameObject moveOriginalObject;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Settings = OptionsPanelHandler.RegisterModOptions<ModConfig>();
        harmony = new Harmony(PluginInfo.Guid);
        harmony.PatchAll();
        Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    private static bool IsMoveModifierHeld()
    {
        return GameInput.GetButtonHeld(GameInput.Button.AltTool)
            || UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftAlt)
            || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightAlt);
    }

    private static bool IsSpanishLanguage()
    {
        string current = Language.main?.GetCurrentLanguage();
        if (!string.IsNullOrEmpty(current) && current.StartsWith("Spanish", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Application.systemLanguage == SystemLanguage.Spanish;
    }

    private static string L(string es, string en)
    {
        return IsSpanishLanguage() ? es : en;
    }

    private static bool IsMovableLocker(Constructable constructable)
    {
        if (constructable == null || !constructable.constructed)
        {
            return false;
        }

        return constructable.techType == TechType.Locker || constructable.techType == TechType.SmallLocker;
    }

    private static IEnumerator BeginPlacingAsync(TechType techType)
    {
        if (techType == TechType.None)
        {
            yield break;
        }

        moveSessionStartingPlacement = true;
        yield return Builder.BeginAsync(techType);
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
        moveSessionActive = false;
        moveSessionCommitted = false;
        moveSessionStartingPlacement = false;
        moveTechType = TechType.None;
        moveOriginalPosition = default;
        moveOriginalRotation = default;
        moveOriginalObject = null;
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

    private static bool TryMoveTargetedLocker()
    {
        if (Builder.isPlacing || !AvatarInputHandler.main.IsEnabled() || moveSessionActive)
        {
            return false;
        }

        Targeting.AddToIgnoreList(Player.main.gameObject);
        Targeting.GetTarget(30f, out GameObject target, out float distance);
        if (target == null)
        {
            return false;
        }

        Constructable constructable = target.GetComponentInParent<Constructable>();
        if (constructable == null || !constructable.constructed || distance > constructable.placeMaxDistance)
        {
            return false;
        }

        if (!IsMovableLocker(constructable))
        {
            ErrorMessage.AddMessage(L("Mover solo funciona con floor locker y wall locker", "Move only works with floor locker and wall locker"));
            return true;
        }

        StorageContainer storage = constructable.GetComponent<StorageContainer>();
        if (Settings != null && Settings.PreventMoveIfNotEmpty && storage != null && !storage.IsEmpty())
        {
            ErrorMessage.AddMessage(L("No se puede mover: tiene items", "Cannot move: contains items"));
            return true;
        }

        moveSessionActive = true;
        moveSessionCommitted = false;
        moveTechType = constructable.techType;
        moveOriginalPosition = constructable.transform.position;
        moveOriginalRotation = constructable.transform.rotation;
        moveOriginalObject = constructable.gameObject;

        Builder.ResetLast();
        constructable.gameObject.SetActive(false);
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

            GameObject originalObject = moveOriginalObject;
            if (originalObject == null)
            {
                return;
            }

            GameObject ghost = Builder.GetGhostModel();
            Vector3 targetPosition = ghost != null ? ghost.transform.position : moveOriginalPosition;
            Quaternion targetRotation = ghost != null ? ghost.transform.rotation : moveOriginalRotation;

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

            moveSessionCommitted = true;
            Builder.ResetLast();
            Builder.End();
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
            ClearMoveSession();

            if (originalObject == null)
            {
                return;
            }

            if (committed)
            {
                return;
            }

            originalObject.transform.position = originalPosition;
            originalObject.transform.rotation = originalRotation;
            originalObject.SetActive(true);
        }
    }

    [HarmonyPatch(typeof(BuilderTool), "OnHover", typeof(Constructable))]
    private static class BuilderTool_OnHover_Patch
    {
        private static void Postfix(Constructable constructable)
        {
            if (!IsMoveModifierHeld() || !IsMovableLocker(constructable))
            {
                return;
            }

            string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);

            HandReticle main = HandReticle.main;
            main.SetText(HandReticle.TextType.HandSubscript, $"Alt + {left}: {L("Mover locker", "Move locker")}", false, GameInput.Button.None);
            main.SetIcon(HandReticle.IconType.Hand, 1f);
        }
    }
}
