using System.Collections.Generic;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
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
        Face,
        FloatingLocker
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
    private static List<Pickupable> moveSessionReactorPickupables;
    private static TechType moveSessionReactorTechType = TechType.None;
    private static GameObject moveSessionReactorSourceRoot;
    private static GameObject moveSessionReactorDestinationRoot;
    private static List<Pickupable> moveSessionFiltrationPickupables;
    private static GameObject moveSessionFiltrationSourceRoot;
    private static GameObject moveSessionFiltrationDestinationRoot;
    private static bool moveSessionReactorStateCaptured;
    private static float moveSessionReactorPower;
    private static float moveSessionReactorToConsume;
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
    private static Component moveSessionFloatingRigidbody;
    private static bool moveSessionFloatingRigidbodyWasKinematic;
    private static bool moveSessionFloatingRigidbodyUsedGravity;
    private static Pickupable moveSessionFloatingPickupable;
    private static bool moveSessionFloatingWasPickupable;
    private static int moveSessionSuppressPlaceUntilFrame;
    private static readonly List<Component> moveSessionFloatingColliders = new List<Component>();
    private static readonly List<bool> moveSessionFloatingColliderEnabledStates = new List<bool>();
}
