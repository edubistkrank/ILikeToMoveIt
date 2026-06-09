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
    private static bool moveHasLastGhostTransform;
    private static Vector3 moveLastGhostPosition;
    private static Quaternion moveLastGhostRotation;
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
    private static WaterPark moveSessionWaterParkSource;
    private static WaterPark moveSessionWaterParkDestination;
    private static readonly List<WaterParkItem> moveSessionWaterParkItems = new List<WaterParkItem>();
    private static readonly List<Pickupable> moveSessionWaterParkPickupables = new List<Pickupable>();
    private static readonly List<Pickupable> moveSessionWaterParkPlanterPickupables = new List<Pickupable>();
    private static Transform moveSessionParkingRoot;
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

        // reset estado específico
        moveSessionFacePieceSource = null;
        moveSessionConstructableBase = null;
        moveSessionOriginalBase = null;
        moveSessionOriginalFace = null;
        moveSessionOriginalFaceType = Base.FaceType.None;
        moveSessionWaterParkSource = null;
        moveSessionWaterParkDestination = null;
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

     private static bool TryMoveTargetedLocker()
    {
        if (Builder.isPlacing || !AvatarInputHandler.main.IsEnabled() || moveSessionActive)
        {
            SetMoveReticleIcon(false);
            return false;
        }

        // Limpieza defensiva: si por cualquier razón el builder quedó colocando algo previo,
        // cerrarlo antes de iniciar un nuevo move para evitar arrastre de techType/ghost.
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

        // PRIMERO: Intentar con BaseDeconstructable (face pieces como plantside)
        // Esto debe ser ANTES de Constructable porque plantside puede tener ambos

        Constructable constructable = target.GetComponentInParent<Constructable>();
        BaseDeconstructable baseDecon = FindFaceDeconstructableForTarget(target, constructable);
        Log.LogInfo($"GetComponentInParent<BaseDeconstructable>: {(baseDecon != null ? "FOUND (" + baseDecon.gameObject.name + ")" : "NOT FOUND")}");

        // Importante: no usar fallback al "BaseDeconstructable más cercano".
        // Ese comportamiento causaba falsos positivos (p.ej. mover un locker y capturar una cara de base cercana).

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
                    if (wp == null) continue;
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

            if (waterPark != null && Settings != null && Settings.PreventMoveWaterParkIfNotEmpty && waterPark.HasItemsInside())
            {
                ErrorMessage.AddMessage(L("No se puede mover: tiene items", "Cannot move: contains items"));
                ClearMoveSession();
                return true;
            }

            CaptureWaterParkItems(waterPark);
            Log.LogInfo($"WaterPark: fauna captured={moveSessionWaterParkPickupables.Count}");
            CaptureWaterParkPlanterItems(waterPark);
            Log.LogInfo($"WaterPark: flora captured={moveSessionWaterParkPlanterPickupables.Count}");
            moveSessionWaterParkDestination = null;
            ParkCapturedWaterParkPickupables();
            ParkCapturedWaterParkPlanterPickupables();
        }

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
        ModConfig settings = Settings;
        if (settings == null)
        {
            return false;
        }

        // Piezas interiores de base (face-based) que transforman caras/adyacencias.
        if (IsInteriorFacePieceTechType(techType))
        {
            return settings.AllowInteriorPieces;
        }

        // Lockers siempre son movibles
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
                if (moveTechType == TechType.BaseWaterPark)
                {
                    Log.LogInfo($"WaterPark: FinalizeMovedWaterPark, destination={moveSessionWaterParkDestination}, fauna={moveSessionWaterParkPickupables.Count}, flora={moveSessionWaterParkPlanterPickupables.Count}");
                    FinalizeMovedWaterPark();
                }

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

    private static void FinalizeMovedWaterPark()
    {
        WaterPark source = moveSessionWaterParkSource;
        Base oldBase = moveSessionOriginalBase;
        Vector3 targetPosition = moveHasLastGhostTransform ? moveLastGhostPosition : moveOriginalPosition;
        List<WaterParkItem> itemsSnapshot = new List<WaterParkItem>(moveSessionWaterParkItems);
        List<Pickupable> pickupablesSnapshot = new List<Pickupable>(moveSessionWaterParkPickupables);
        List<Pickupable> planterPickupablesSnapshot = new List<Pickupable>(moveSessionWaterParkPlanterPickupables);

        if (TryFindWaterParkDestination(targetPosition, out WaterPark destination, out Base destinationBase))
        {
            CompleteWaterParkTransfer(source, destination, oldBase, destinationBase, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot);
            return;
        }

        if (Instance != null)
        {
            Instance.StartCoroutine(FinalizeMovedWaterParkDelayed(source, oldBase, targetPosition, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot));
            return;
        }

        if (source != null)
        {
            Object.Destroy(source.gameObject);
        }

        oldBase?.RebuildGeometry();
    }

    private static bool TryFindWaterParkDestination(Vector3 targetPosition, out WaterPark destination, out Base destinationBase)
    {
        destination = moveSessionWaterParkDestination;
        destinationBase = destination != null ? destination.GetComponentInParent<Base>() : null;
        return destination != null;
    }

    private static IEnumerator FinalizeMovedWaterParkDelayed(WaterPark source, Base oldBase, Vector3 targetPosition, List<WaterParkItem> itemsSnapshot, List<Pickupable> pickupablesSnapshot, List<Pickupable> planterPickupablesSnapshot)
    {
        const int maxFrames = 90;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;

            if (TryFindWaterParkDestination(targetPosition, out WaterPark destination, out Base destinationBase))
            {
                CompleteWaterParkTransfer(source, destination, oldBase, destinationBase, itemsSnapshot, pickupablesSnapshot, planterPickupablesSnapshot);
                yield break;
            }
        }

        if (source != null)
        {
            Object.Destroy(source.gameObject);
        }

        oldBase?.RebuildGeometry();
    }

    private static void CompleteWaterParkTransfer(WaterPark source, WaterPark destination, Base oldBase, Base destinationBase, List<WaterParkItem> itemsSnapshot, List<Pickupable> pickupablesSnapshot, List<Pickupable> planterPickupablesSnapshot)
    {
        bool restored = false;
        if (pickupablesSnapshot != null && pickupablesSnapshot.Count > 0)
        {
            for (int i = 0; i < pickupablesSnapshot.Count; i++)
            {
                Pickupable pickupable = pickupablesSnapshot[i];
                if (pickupable == null)
                {
                    continue;
                }

                if (!pickupable.gameObject.activeSelf)
                {
                    pickupable.gameObject.SetActive(true);
                }

                destination.AddItem(pickupable);
                Vector3 clampedPosition = pickupable.transform.position;
                destination.EnsurePointIsInside(ref clampedPosition);
                pickupable.transform.position = clampedPosition;
                restored = true;
            }
        }

        if (planterPickupablesSnapshot != null && planterPickupablesSnapshot.Count > 0 && Instance != null)
        {
            Instance.StartCoroutine(RestoreWaterParkPlanterItemsDeferred(destination, planterPickupablesSnapshot));
        }

        if (!restored && itemsSnapshot != null && itemsSnapshot.Count > 0)
        {
            for (int i = 0; i < itemsSnapshot.Count; i++)
            {
                WaterParkItem item = itemsSnapshot[i];
                if (item != null && item.GetWaterPark() != destination)
                {
                    item.SetWaterPark(destination);
                    restored = true;
                }
            }
        }

        if (!restored && source != null && source.HasItemsInside())
        {
            WaterPark.TransferValue(source, destination);
        }

        Base newBase = destinationBase ?? destination.GetComponentInParent<Base>();
        if (source != null && !ReferenceEquals(source, destination))
        {
            Object.Destroy(source.gameObject);
        }

        oldBase?.RebuildGeometry();
        if (newBase != null && !ReferenceEquals(newBase, oldBase))
        {
            newBase.RebuildGeometry();
        }
    }

    private static void CaptureWaterParkItems(WaterPark waterPark)
    {
        moveSessionWaterParkItems.Clear();
        moveSessionWaterParkPickupables.Clear();
        if (waterPark == null)
        {
            return;
        }

        System.Reflection.FieldInfo itemsField = typeof(WaterPark).GetField("items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (itemsField == null)
        {
            return;
        }

        if (itemsField.GetValue(waterPark.rootWaterPark) is List<WaterParkItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                WaterParkItem item = items[i];
                if (item != null)
                {
                    moveSessionWaterParkItems.Add(item);
                    Pickupable pickupable = item.GetComponent<Pickupable>();
                    if (pickupable != null)
                    {
                        moveSessionWaterParkPickupables.Add(pickupable);
                    }
                }
            }
        }
    }

    private static void CaptureWaterParkPlanterItems(WaterPark waterPark)
    {
        moveSessionWaterParkPlanterPickupables.Clear();
        if (waterPark == null)
        {
            return;
        }

        // WaterPark normal: campo planter directo
        if (waterPark.planter != null)
        {
            EnsurePlanterReady(waterPark.planter);
            CaptureFromPlanter(waterPark.planter);
        }

        // LargeRoomWaterPark: usa LargeRoomWaterParkPlanter con leftPlanter y rightPlanter
        System.Reflection.FieldInfo plantersField = waterPark.GetType().GetField("planters",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object plantersObj = plantersField?.GetValue(waterPark);
        if (plantersObj != null)
        {
            System.Type plantersType = plantersObj.GetType();
            foreach (string name in new[] { "leftPlanter", "rightPlanter" })
            {
                System.Reflection.FieldInfo f = plantersType.GetField(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(plantersObj) is Planter p)
                {
                    EnsurePlanterReady(p);
                    CaptureFromPlanter(p);
                }
            }
        }
    }

    private static void CaptureFromPlanter(Planter planter)
    {
        StorageContainer storageContainer = planter.storageContainer;
        ItemsContainer container = storageContainer?.container;
        if (container == null)
        {
            CaptureWaterParkPlanterItemsFromSlots(planter);
            return;
        }

        List<InventoryItem> snapshot = new List<InventoryItem>();
        foreach (InventoryItem inventoryItem in container)
        {
            if (inventoryItem != null)
            {
                snapshot.Add(inventoryItem);
            }
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            Pickupable pickupable = snapshot[i].item;
            if (pickupable == null || moveSessionWaterParkPlanterPickupables.Contains(pickupable))
            {
                continue;
            }

            Plantable plantable = pickupable.GetComponent<Plantable>();
            if (plantable != null)
            {
                planter.RemoveItem(plantable);
            }
            else
            {
                container.RemoveItem(pickupable, true);
            }

            moveSessionWaterParkPlanterPickupables.Add(pickupable);
        }
    }

    private static void CaptureWaterParkPlanterItemsFromSlots(Planter planter)
    {
        if (planter == null)
        {
            return;
        }

        CaptureWaterParkPlanterItemsFromSlotArray(planter, "bigPlantSlots");
        CaptureWaterParkPlanterItemsFromSlotArray(planter, "smallPlantSlots");
    }

    private static void CaptureWaterParkPlanterItemsFromSlotArray(Planter planter, string fieldName)
    {
        try
        {
            System.Reflection.FieldInfo slotsField = typeof(Planter).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (!(slotsField?.GetValue(planter) is System.Array slots))
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                object slot = slots.GetValue(i);
                if (slot == null)
                {
                    continue;
                }

                System.Reflection.FieldInfo plantableField = slot.GetType().GetField("plantable", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Plantable plantable = plantableField?.GetValue(slot) as Plantable;
                Pickupable pickupable = plantable != null ? plantable.GetComponent<Pickupable>() : null;
                if (pickupable == null || moveSessionWaterParkPlanterPickupables.Contains(pickupable))
                {
                    continue;
                }

                planter.RemoveItem(plantable);
                moveSessionWaterParkPlanterPickupables.Add(pickupable);
            }
        }
        catch
        {
        }
    }

    private static void ParkCapturedWaterParkPickupables()
    {
        if (moveSessionWaterParkPickupables.Count == 0)
        {
            return;
        }

        Transform parkingRoot = GetOrCreateMoveSessionParkingRoot();
        for (int i = 0; i < moveSessionWaterParkPickupables.Count; i++)
        {
            Pickupable pickupable = moveSessionWaterParkPickupables[i];
            if (pickupable == null)
            {
                continue;
            }

            WaterParkItem item = pickupable.GetComponent<WaterParkItem>();
            if (item != null)
            {
                item.SetWaterPark(null);
            }

            pickupable.transform.SetParent(parkingRoot, true);
            if (pickupable.gameObject.activeSelf)
            {
                pickupable.gameObject.SetActive(false);
            }
        }
    }

    private static void ParkCapturedWaterParkPlanterPickupables()
    {
        if (moveSessionWaterParkPlanterPickupables.Count == 0)
        {
            return;
        }

        Transform parkingRoot = GetOrCreateMoveSessionParkingRoot();
        for (int i = 0; i < moveSessionWaterParkPlanterPickupables.Count; i++)
        {
            Pickupable pickupable = moveSessionWaterParkPlanterPickupables[i];
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

    private static IEnumerator RestoreWaterParkPlanterItemsDeferred(WaterPark destination, List<Pickupable> planterPickupablesSnapshot)
    {
        if (destination == null || planterPickupablesSnapshot == null || planterPickupablesSnapshot.Count == 0)
        {
            yield break;
        }

        List<Pickupable> pending = new List<Pickupable>(planterPickupablesSnapshot);
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames && pending.Count > 0; frame++)
        {
            List<Planter> planters = GetWaterParkPlanters(destination);
            foreach (Planter planter in planters)
            {
                EnsurePlanterReady(planter);
            }

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pickupable pickupable = pending[i];
                if (pickupable == null)
                {
                    pending.RemoveAt(i);
                    continue;
                }

                // La semilla/planta dentro de un planter es un item de inventario invisible:
                // su Pickupable.gameObject permanece INACTIVO y el Planter spawnea su propio
                // modelo visible en el slot (plantable.Spawn). Si lo activamos, el modelo
                // original queda flotando alineado en el storageRoot (duplicado visual).
                if (pickupable.gameObject.activeSelf)
                {
                    pickupable.gameObject.SetActive(false);
                }

                pickupable.ResetTechTypeOverride();

                bool added = false;
                foreach (Planter planter in planters)
                {
                    ItemsContainer container = planter.storageContainer?.container;
                    if (container == null)
                    {
                        continue;
                    }

                    InventoryItem result = container.AddItem(pickupable);
                    if (result != null)
                    {
                        added = true;
                        break;
                    }
                }

                if (added)
                {
                    pending.RemoveAt(i);
                }
            }

            yield return null;
        }
    }

    private static List<Planter> GetWaterParkPlanters(WaterPark waterPark)
    {
        List<Planter> result = new List<Planter>();
        if (waterPark == null)
        {
            return result;
        }

        if (waterPark.planter != null)
        {
            result.Add(waterPark.planter);
        }

        System.Reflection.FieldInfo plantersField = waterPark.GetType().GetField("planters",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object plantersObj = plantersField?.GetValue(waterPark);
        if (plantersObj != null)
        {
            System.Type t = plantersObj.GetType();
            foreach (string name in new[] { "leftPlanter", "rightPlanter" })
            {
                System.Reflection.FieldInfo f = t.GetField(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(plantersObj) is Planter p)
                {
                    result.Add(p);
                }
            }
        }

        return result;
    }

    private static void EnsurePlanterReady(Planter planter)
    {
        if (planter == null)
        {
            return;
        }

        try
        {
            if (planter.storageContainer != null && !planter.storageContainer.enabled)
            {
                planter.storageContainer.enabled = true;
            }

            System.Reflection.MethodInfo initialize = typeof(Planter).GetMethod("Initialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            initialize?.Invoke(planter, null);

            System.Reflection.MethodInfo subscribe = typeof(Planter).GetMethod("Subscribe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            subscribe?.Invoke(planter, new object[] { true });
        }
        catch
        {
        }
    }

    private static Transform GetOrCreateMoveSessionParkingRoot()
    {
        if (moveSessionParkingRoot != null)
        {
            return moveSessionParkingRoot;
        }

        GameObject root = new GameObject("ILikeToMoveIt_WaterParkParkingRoot");
        root.hideFlags = HideFlags.HideAndDontSave;
        moveSessionParkingRoot = root.transform;
        return moveSessionParkingRoot;
    }

    [HarmonyPatch(typeof(Base), "SpawnModule", new[] { typeof(GameObject), typeof(Base.Face) })]
    private static class Base_SpawnModule_Patch
    {
        private static void Postfix(GameObject __result)
        {
            if (!moveSessionActive || moveTechType != TechType.BaseWaterPark || __result == null)
            {
                return;
            }

            WaterPark waterPark = __result.GetComponent<WaterPark>() ?? __result.GetComponentInChildren<WaterPark>(true);
            if (waterPark != null)
            {
                moveSessionWaterParkDestination = waterPark;
            }
        }
    }

    [HarmonyPatch(typeof(WaterParkGeometry), "CanDeconstruct")]
    private static class WaterParkGeometry_CanDeconstruct_Patch
    {
        private static bool Prefix(ref bool __result, ref string reason)
        {
            if (!moveSessionActive || moveBackend != MoveBackend.Face || moveTechType != TechType.BaseWaterPark)
            {
                return true;
            }

            if (Settings != null && Settings.PreventMoveWaterParkIfNotEmpty)
            {
                return true;
            }

            reason = null;
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
