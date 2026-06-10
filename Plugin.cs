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
public sealed partial class Plugin : BaseUnityPlugin
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
    private static bool moveSessionWaterParkUseVanillaFauna;
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
}
