using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Handlers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    private static bool moveSessionPlacementInitialized;
    private static TechType moveTechType = TechType.None;
    private static Vector3 moveOriginalPosition;
    private static Quaternion moveOriginalRotation;
    private static GameObject moveOriginalObject;
    private static bool moveSessionIsFacePiece;
    private enum MoveBackend
    {
        Regular,
        Face
    }
    private static MoveBackend moveBackend;
    private static BaseDeconstructable moveSessionFacePieceSource;
    private static ConstructableBase moveSessionConstructableBase;
    private static Base moveSessionOriginalBase;
    private static Base.Face? moveSessionOriginalFace;
    private static Base.FaceType moveSessionOriginalFaceType;
    private static bool bypassResourceConsumption;
    private static readonly System.Reflection.FieldInfo baseDeconstructableRecipeField = typeof(BaseDeconstructable).GetField(
        "recipe",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    private static Sprite moveReticleSprite;
    private static readonly Dictionary<object, Sprite> originalReticleSprites = new Dictionary<object, Sprite>();

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Settings = OptionsPanelHandler.RegisterModOptions<ModConfig>();
        harmony = new Harmony(PluginInfo.Guid);
        LoadMoveReticleSprite();
        harmony.PatchAll();
        StartCoroutine(WarmupReticleMovePath());
        Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded.");
    }

    private static void LoadMoveReticleSprite()
    {
        try
        {
            var asm = typeof(Plugin).Assembly;
            string resourceName = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("assets.move.png", System.StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("move.png", System.StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(resourceName))
            {
                Log.LogWarning("LoadMoveReticleSprite: Embedded move.png not found.");
                return;
            }

            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return;
                }

                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                tex.name = "ILikeToMoveIt_MoveReticle";
                System.Type imageConversionType = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                System.Reflection.MethodInfo loadImageMethod = imageConversionType?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (loadImageMethod == null)
                {
                    Log.LogWarning("LoadMoveReticleSprite: LoadImage method not found.");
                    return;
                }

                object loaded = loadImageMethod.Invoke(null, new object[] { tex, data });
                if (!(loaded is bool ok) || !ok)
                {
                    return;
                }

                moveReticleSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                Log.LogInfo($"LoadMoveReticleSprite: Loaded {resourceName} ({tex.width}x{tex.height})");
            }
        }
        catch (System.Exception ex)
        {
            Log.LogError($"LoadMoveReticleSprite failed: {ex.Message}");
        }
    }

    private static void SetMoveReticleIcon(bool enabled)
    {
        if (moveReticleSprite == null)
        {
            return;
        }

        HandReticle reticle = HandReticle.main;
        if (reticle == null || reticle.icons == null)
        {
            return;
        }

        for (int i = 0; i < reticle.icons.Count; i++)
        {
            uGUI_HandReticleIcon icon = reticle.icons[i];
            if (icon == null || icon.type != HandReticle.IconType.Hand || icon.graphic == null)
            {
                continue;
            }

            System.Reflection.FieldInfo graphicsField = typeof(uGUI_HandReticleIcon).GetField("graphic");
            if (graphicsField == null)
            {
                continue;
            }

            System.Array graphics = graphicsField.GetValue(icon) as System.Array;
            if (graphics == null)
            {
                continue;
            }

            for (int g = 0; g < graphics.Length; g++)
            {
                object graphic = graphics.GetValue(g);
                if (graphic == null)
                {
                    continue;
                }

                System.Reflection.PropertyInfo spriteProperty = graphic.GetType().GetProperty("sprite");
                if (spriteProperty == null || !spriteProperty.CanRead || !spriteProperty.CanWrite)
                {
                    continue;
                }

                if (enabled)
                {
                    if (!originalReticleSprites.ContainsKey(graphic))
                    {
                        Sprite current = spriteProperty.GetValue(graphic, null) as Sprite;
                        originalReticleSprites[graphic] = current;
                    }

                    spriteProperty.SetValue(graphic, moveReticleSprite, null);
                }
                else if (originalReticleSprites.TryGetValue(graphic, out Sprite original))
                {
                    spriteProperty.SetValue(graphic, original, null);
                }
            }
        }
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

    private static IEnumerator WarmupReticleMovePath()
    {
        yield return null;

        string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
        string _ = $"{L("Mover", "Move")} (Alt + {left})";
    }

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

    private static IEnumerator BeginPlacingAsync(TechType techType)
    {
        if (techType == TechType.None)
        {
            yield break;
        }

        moveSessionStartingPlacement = true;

        if (moveBackend == MoveBackend.Regular)
        {
            // Solo para items regulares usar Builder.BeginAsync
            Log.LogInfo($"BeginPlacingAsync: Regular item - using Builder.BeginAsync({techType})");
            yield return Builder.BeginAsync(techType);
        }
        else
        {
            Log.LogInfo($"BeginPlacingAsync: Face piece - cleaning transient deconstructable and opening blueprint");

            if (moveSessionConstructableBase != null && !moveSessionConstructableBase.constructed)
            {
                Log.LogInfo($"BeginPlacingAsync: Destroying transient ConstructableBase '{moveSessionConstructableBase.gameObject.name}' before Builder.BeginAsync");
                moveSessionConstructableBase.gameObject.SetActive(false);
                Object.Destroy(moveSessionConstructableBase.gameObject);
                moveSessionConstructableBase = null;
                yield return null;
            }

            Builder.ResetLast();
            yield return Builder.BeginAsync(techType);
        }

        moveSessionStartingPlacement = false;
        moveSessionPlacementInitialized = false;
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
        moveSessionPlacementInitialized = false;
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
        bypassResourceConsumption = false;
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

        // reset estado específico
        moveSessionFacePieceSource = null;
        moveSessionConstructableBase = null;
        moveSessionOriginalBase = null;
        moveSessionOriginalFace = null;
        moveSessionOriginalFaceType = Base.FaceType.None;
        bypassResourceConsumption = false;
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
            SetMoveReticleIcon(false);
            return false;
        }

        Targeting.AddToIgnoreList(Player.main.gameObject);
        Targeting.GetTarget(30f, out GameObject target, out float distance);
        if (target == null)
        {
            SetMoveReticleIcon(false);
            return false;
        }

        // PRIMERO: Intentar con BaseDeconstructable (face pieces como plantside)
        // Esto debe ser ANTES de Constructable porque plantside puede tener ambos

        // Buscar TODOS los BaseDeconstructable cercanos para debug
        BaseDeconstructable[] allBaseDeco = Object.FindObjectsOfType<BaseDeconstructable>();
        Log.LogInfo($"=== TryMoveTargetedLocker: Total BD in scene: {allBaseDeco.Length}, Target: {target.name}, Distance: {distance} ===");

        BaseDeconstructable baseDecon = target.GetComponentInParent<BaseDeconstructable>();
        if (IsTransientFaceDeconstructable(baseDecon))
        {
            Log.LogInfo("GetComponentInParent<BaseDeconstructable>: transient ghost detected, ignoring");
            baseDecon = null;
        }
        Log.LogInfo($"GetComponentInParent<BaseDeconstructable>: {(baseDecon != null ? "FOUND (" + baseDecon.gameObject.name + ")" : "NOT FOUND")}");

        // Si no encontramos con GetComponentInParent, buscar el más cercano
        if (baseDecon == null && distance <= 11f)
        {
            float closestDist = float.MaxValue;
            foreach (BaseDeconstructable bd in allBaseDeco)
            {
                if (IsTransientFaceDeconstructable(bd))
                {
                    continue;
                }

                float dist = Vector3.Distance(Player.main.transform.position, bd.transform.position);
                TechType bdTech = GetBaseDeconstructableTechType(bd);
                Log.LogInfo($"  Nearby BD: {bd.gameObject.name}, TechType: {bdTech}, Distance: {dist}");
                if (dist < closestDist && dist <= 11f)
                {
                    closestDist = dist;
                    baseDecon = bd;
                }
            }
            if (baseDecon != null)
            {
                Log.LogInfo($"Found closest BaseDeconstructable: {baseDecon.gameObject.name}");
            }
        }

        if (baseDecon != null && distance <= 11f)
        {
            Log.LogInfo($"TryMoveTargetedLocker: Found BaseDeconstructable");
            TechType recipeType = GetBaseDeconstructableTechType(baseDecon);
            Log.LogInfo($"TryMoveTargetedLocker: BaseDeconstructable recipeType={recipeType}");
            if (recipeType != TechType.None && IsMovableBySettings(recipeType))
            {
                Log.LogInfo($"TryMoveTargetedLocker: BaseDeconstructable is movable ({recipeType}), calling TryMoveBaseFacePiece");
                return TryMoveBaseFacePiece(baseDecon);
            }
            else
            {
                Log.LogWarning($"TryMoveTargetedLocker: BaseDeconstructable found but recipeType={recipeType}, movable={IsMovableBySettings(recipeType)}");
            }
        }

        // DESPUÉS: Intentar con Constructable (regular items, lockers)
        Constructable constructable = target.GetComponentInParent<Constructable>();
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

            Builder.ResetLast();
            constructable.gameObject.SetActive(false);
            if (Instance != null)
            {
                Instance.StartCoroutine(BeginPlacingAsync(moveTechType));
            }

            return true;
        }

        Log.LogInfo($"TryMoveTargetedLocker: No valid object found");
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

        // Verificar si está habilitado
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

        Log.LogInfo($"TryMoveBaseFacePiece: Calling Builder.ResetLast()");
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

        Log.LogInfo($"TryMoveBaseFacePiece: Calling baseDecon.Deconstruct()");
        baseDecon.Deconstruct();
        Log.LogInfo($"TryMoveBaseFacePiece: Deconstruct() completed");

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

    private static TechType GetBaseDeconstructableTechType(BaseDeconstructable baseDecon)
    {
        if (baseDecon == null)
            return TechType.None;

        if (baseDeconstructableRecipeField != null)
        {
            object value = baseDeconstructableRecipeField.GetValue(baseDecon);
            if (value is TechType techType)
                return techType;
        }

        return TechType.None;
    }

    private static bool IsMovableBySettings(TechType techType)
    {
        // Lockers siempre son movibles
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

            // Para face pieces, vanilla maneja todo automáticamente
            // No necesitamos hacer nada especial
            if (moveBackend == MoveBackend.Face)
            {
                Log.LogInfo($"Builder_TryPlace_Patch: Face piece placed by vanilla, marking committed");
                InstantBuildMovedFacePiece();

                moveSessionCommitted = true;
                moveSessionStartingPlacement = false;
                Builder.ResetLast();
                Builder.End();
                return;
            }

            // Para items regulares, mantener la lógica original
            HandleRegularItemPlacement();

            moveSessionCommitted = true;
            moveSessionStartingPlacement = false;
            Builder.ResetLast();
            Builder.End();
        }

        private static void HandleRegularItemPlacement()
        {
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

            // Ignorar End transitorios durante/justo tras BeginAsync antes de entrar realmente en modo placement.
            if (!moveSessionPlacementInitialized)
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
            ClearMoveSession();

            if (committed)
            {
                return;
            }

            // Si fue cancelado:
            if (backend == MoveBackend.Face)
            {
                if (originalBase != null && originalFace != null)
                {
                    try
                    {
                        originalBase.SetFaceType(originalFace.Value, originalFaceType);
                        originalBase.RebuildGeometry();
                        Log.LogInfo($"Builder_End_Patch: restored canceled face piece {originalFaceType}");
                    }
                    catch (System.Exception ex)
                    {
                        Log.LogError($"Builder_End_Patch restore failed: {ex.Message}");
                    }
                }

                return;
            }

            if (originalObject != null)
            {
                // Para items regulares, restaurar posición y rotación
                originalObject.transform.position = originalPosition;
                originalObject.transform.rotation = originalRotation;
                originalObject.SetActive(true);
            }
        }
    }

    [HarmonyPatch(typeof(Builder), "Update")]
    private static class Builder_Update_MoveSession_Patch
    {
        private static void Postfix()
        {
            if (moveSessionActive && Builder.isPlacing)
            {
                moveSessionPlacementInitialized = true;
            }
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

            // Detectar si estamos apuntando a un plantside
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

    [HarmonyPatch(typeof(Builder), "CreateGhost", new[] { typeof(TechType) })]
    private static class Builder_CreateGhost_Patch
    {
        private static bool Prefix(TechType techType, ref GameObject __result)
        {
            return true;
        }
    }
}
