using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private static bool IsFloatingLockerMoveTarget(GameObject target, out Pickupable pickupable, out GameObject root)
    {
        pickupable = null;
        root = null;
        if (target == null)
        {
            return false;
        }

        Pickupable candidate = target.GetComponentInParent<Pickupable>()
            ?? target.GetComponent<Pickupable>()
            ?? target.GetComponentInChildren<Pickupable>(true);
        if (candidate == null)
        {
            return false;
        }

        if (candidate.GetTechType() != TechType.SmallStorage)
        {
            return false;
        }

        Constructable constructable = candidate.GetComponentInParent<Constructable>();
        if (constructable != null)
        {
            return false;
        }

        root = candidate.gameObject;
        pickupable = candidate;
        return true;
    }

    private static bool TryBeginFloatingLockerMove(Pickupable pickupable, GameObject root)
    {
        if (pickupable == null || root == null || moveSessionActive)
        {
            return false;
        }

        BeginMoveSessionCore(
            pickupable.GetTechType(),
            root.transform.position,
            root.transform.rotation,
            root,
            MoveBackend.FloatingLocker);

        moveSessionFloatingPickupable = pickupable;
        moveSessionFloatingWasPickupable = pickupable.isPickupable;
        pickupable.isPickupable = false;

        Component rigidbody = root.GetComponent("Rigidbody");
        moveSessionFloatingRigidbody = rigidbody;
        if (rigidbody != null)
        {
            System.Type rigidbodyType = rigidbody.GetType();
            System.Reflection.PropertyInfo isKinematicProp = rigidbodyType.GetProperty("isKinematic");
            System.Reflection.PropertyInfo useGravityProp = rigidbodyType.GetProperty("useGravity");
            System.Reflection.PropertyInfo velocityProp = rigidbodyType.GetProperty("velocity");
            System.Reflection.PropertyInfo angularVelocityProp = rigidbodyType.GetProperty("angularVelocity");

            moveSessionFloatingRigidbodyWasKinematic = isKinematicProp != null && (bool)isKinematicProp.GetValue(rigidbody, null);
            moveSessionFloatingRigidbodyUsedGravity = useGravityProp != null && (bool)useGravityProp.GetValue(rigidbody, null);

            isKinematicProp?.SetValue(rigidbody, true, null);
            useGravityProp?.SetValue(rigidbody, false, null);
            velocityProp?.SetValue(rigidbody, Vector3.zero, null);
            angularVelocityProp?.SetValue(rigidbody, Vector3.zero, null);
        }

        moveSessionFloatingColliders.Clear();
        moveSessionFloatingColliderEnabledStates.Clear();
        System.Type colliderType = System.Type.GetType("UnityEngine.Collider, UnityEngine.PhysicsModule");
        Component[] colliders = colliderType != null
            ? root.GetComponentsInChildren(colliderType, true)
            : new Component[0];
        for (int i = 0; i < colliders.Length; i++)
        {
            Component collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            System.Reflection.PropertyInfo enabledProp = collider.GetType().GetProperty("enabled");
            bool wasEnabled = enabledProp != null && (bool)enabledProp.GetValue(collider, null);
            moveSessionFloatingColliders.Add(collider);
            moveSessionFloatingColliderEnabledStates.Add(wasEnabled);
            enabledProp?.SetValue(collider, false, null);
        }

        return true;
    }

    private static void UpdateFloatingLockerPreview()
    {
        if (!moveSessionActive || moveBackend != MoveBackend.FloatingLocker || moveOriginalObject == null)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 origin = camera.transform.position;
        Vector3 forward = camera.transform.forward;
        Vector3 targetPosition = origin + forward * 3f;

        moveOriginalObject.transform.position = targetPosition;
        moveOriginalObject.transform.rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);
    }

    private static void RestoreFloatingLockerState()
    {
        if (moveSessionFloatingPickupable != null)
        {
            moveSessionFloatingPickupable.isPickupable = moveSessionFloatingWasPickupable;
        }

        if (moveSessionFloatingRigidbody != null)
        {
            System.Type rigidbodyType = moveSessionFloatingRigidbody.GetType();
            System.Reflection.PropertyInfo isKinematicProp = rigidbodyType.GetProperty("isKinematic");
            System.Reflection.PropertyInfo useGravityProp = rigidbodyType.GetProperty("useGravity");
            isKinematicProp?.SetValue(moveSessionFloatingRigidbody, moveSessionFloatingRigidbodyWasKinematic, null);
            useGravityProp?.SetValue(moveSessionFloatingRigidbody, moveSessionFloatingRigidbodyUsedGravity, null);
        }

        int count = moveSessionFloatingColliders.Count;
        for (int i = 0; i < count; i++)
        {
            Component collider = moveSessionFloatingColliders[i];
            if (collider == null)
            {
                continue;
            }

            bool enabled = i < moveSessionFloatingColliderEnabledStates.Count
                ? moveSessionFloatingColliderEnabledStates[i]
                : true;
            System.Reflection.PropertyInfo enabledProp = collider.GetType().GetProperty("enabled");
            enabledProp?.SetValue(collider, enabled, null);
        }

        moveSessionFloatingColliders.Clear();
        moveSessionFloatingColliderEnabledStates.Clear();
        moveSessionFloatingPickupable = null;
        moveSessionFloatingRigidbody = null;
    }

}
