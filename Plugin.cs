using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Handlers;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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

    private static Sprite _moveSprite;
    private static Sprite[] _originalHandSprites;
    private static bool _showMoveIcon;

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

    private void LateUpdate()
    {
        if (_showMoveIcon)
        {
            EnsureMoveSprite();
            ApplyMoveSpritesToHandIcon();
        }
        else
        {
            RestoreOriginalHandSprites();
        }
        _showMoveIcon = false;
    }

    private static void EnsureMoveSprite()
    {
        if (_moveSprite != null)
        {
            return;
        }

        System.Reflection.Assembly assembly = typeof(Plugin).Assembly;
        using (System.IO.Stream stream = assembly.GetManifestResourceStream("ILikeToMoveIt.move.png"))
        {
            if (stream == null)
            {
                Log?.LogWarning("[ILikeToMoveIt] Embedded resource 'ILikeToMoveIt.move.png' not found.");
                return;
            }

            byte[] bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                Log?.LogWarning("[ILikeToMoveIt] move.png failed to decode.");
                return;
            }

            _moveSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            Log?.LogInfo("[ILikeToMoveIt] move.png loaded from embedded resource.");
        }
    }

    private static void CacheOriginalHandSprites()
    {
        if (_originalHandSprites != null || HandReticle.main == null)
        {
            return;
        }

        foreach (uGUI_HandReticleIcon icon in HandReticle.main.icons)
        {
            if (icon.type != HandReticle.IconType.Hand)
            {
                continue;
            }

            _originalHandSprites = new Sprite[icon.graphic.Length];
            for (int i = 0; i < icon.graphic.Length; i++)
            {
                if (icon.graphic[i] is Image img)
                {
                    _originalHandSprites[i] = img.sprite;
                }
            }

            return;
        }
    }

    private static void ApplyMoveSpritesToHandIcon()
    {
        if (_moveSprite == null || HandReticle.main == null)
        {
            return;
        }

        CacheOriginalHandSprites();

        foreach (uGUI_HandReticleIcon icon in HandReticle.main.icons)
        {
            if (icon.type != HandReticle.IconType.Hand)
            {
                continue;
            }

            foreach (Graphic g in icon.graphic)
            {
                if (g is Image img)
                {
                    img.sprite = _moveSprite;
                }
            }

            return;
        }
    }

    private static void RestoreOriginalHandSprites()
    {
        if (_originalHandSprites == null || HandReticle.main == null)
        {
            return;
        }

        foreach (uGUI_HandReticleIcon icon in HandReticle.main.icons)
        {
            if (icon.type != HandReticle.IconType.Hand)
            {
                continue;
            }

            for (int i = 0; i < icon.graphic.Length && i < _originalHandSprites.Length; i++)
            {
                if (icon.graphic[i] is Image img && _originalHandSprites[i] != null)
                {
                    img.sprite = _originalHandSprites[i];
                }
            }

            return;
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

    private static bool IsMovableBySettings(Constructable constructable)
    {
        if (constructable == null || !constructable.constructed)
        {
            return false;
        }

        TechType techType = constructable.techType;
        if (techType == TechType.Locker || techType == TechType.SmallLocker)
        {
            return true;
        }

        if (!CraftData.GetBuilderIndex(techType, out TechGroup group, out _, out _))
        {
            return false;
        }

        ModConfig settings = Settings;
        if (settings == null)
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
            if (!IsMoveModifierHeld() || !IsMovableBySettings(constructable))
            {
                return;
            }

            string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);

            HandReticle main = HandReticle.main;
            main.SetText(HandReticle.TextType.HandSubscript, $"Alt + {left}: {L("Mover locker", "Move locker")}", false, GameInput.Button.None);
            main.SetIcon(HandReticle.IconType.Hand, 1f);
            _showMoveIcon = true;
        }
    }
}
