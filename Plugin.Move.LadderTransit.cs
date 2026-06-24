using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private static BaseLadder moveSessionHoveredBaseLadder;
    private static BaseEntranceLadder moveSessionHoveredEntranceLadder;

    private static GUIHand GetGuiHand()
    {
        return Object.FindObjectOfType<GUIHand>();
    }

    private static bool TryShowLadderHoverDuringMovePlacement()
    {
        if (!moveSessionActive || !Builder.isPlacing || moveBackend == MoveBackend.FloatingLocker)
        {
            return false;
        }

        if (Player.main == null)
        {
            return false;
        }

        Targeting.AddToIgnoreList(Player.main.gameObject);
        Targeting.GetTarget(30f, out GameObject target, out float distance);
        if (target == null || distance > 11f)
        {
            return false;
        }

        BaseLadder baseLadder = target.GetComponentInParent<BaseLadder>()
            ?? target.GetComponent<BaseLadder>()
            ?? target.GetComponentInChildren<BaseLadder>(true);

        BaseEntranceLadder entranceLadder = target.GetComponentInParent<BaseEntranceLadder>()
            ?? target.GetComponent<BaseEntranceLadder>()
            ?? target.GetComponentInChildren<BaseEntranceLadder>(true);

        if (baseLadder == null && entranceLadder == null)
        {
            moveSessionHoveredBaseLadder = null;
            moveSessionHoveredEntranceLadder = null;
            return false;
        }

        moveSessionHoveredBaseLadder = baseLadder;
        moveSessionHoveredEntranceLadder = entranceLadder;

        GUIHand hand = GetGuiHand();
        if (hand == null)
        {
            return false;
        }

        if (baseLadder != null && baseLadder.isActiveAndEnabled)
        {
            baseLadder.OnHandHover(hand);
            return true;
        }

        if (entranceLadder != null && entranceLadder.isActiveAndEnabled)
        {
            entranceLadder.OnHandHover(hand);
            return true;
        }

        return false;
    }

    private static bool TryUseLadderDuringMovePlacement()
    {
        if (!moveSessionActive || !Builder.isPlacing || moveBackend == MoveBackend.FloatingLocker)
        {
            return false;
        }

        if (Player.main == null)
        {
            return false;
        }

        BaseLadder baseLadder = moveSessionHoveredBaseLadder;
        BaseEntranceLadder entranceLadder = moveSessionHoveredEntranceLadder;

        if (baseLadder == null && entranceLadder == null)
        {
            Targeting.AddToIgnoreList(Player.main.gameObject);
            Targeting.GetTarget(30f, out GameObject target, out float distance);
            if (target == null || distance > 11f)
            {
                return false;
            }

            baseLadder = target.GetComponentInParent<BaseLadder>()
                ?? target.GetComponent<BaseLadder>()
                ?? target.GetComponentInChildren<BaseLadder>(true);

            entranceLadder = target.GetComponentInParent<BaseEntranceLadder>()
                ?? target.GetComponent<BaseEntranceLadder>()
                ?? target.GetComponentInChildren<BaseEntranceLadder>(true);
        }

        if (baseLadder == null && entranceLadder == null)
        {
            return false;
        }

        Player player = Player.main;
        if (player == null)
        {
            return false;
        }

        if (baseLadder != null && baseLadder.isActiveAndEnabled)
        {
            Vector3 exitPosition = Vector3.zero;
            Base.Direction exitDirection = Base.Direction.Above;
            object[] args = { exitPosition, exitDirection };
            System.Reflection.MethodInfo getExitPointMethod = typeof(BaseLadder).GetMethod(
                "GetExitPoint",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (getExitPointMethod != null)
            {
                bool canUse = (bool)getExitPointMethod.Invoke(baseLadder, args);
                if (canUse)
                {
                    player.SetPosition((Vector3)args[0]);
                    moveSessionHoveredBaseLadder = null;
                    moveSessionHoveredEntranceLadder = null;
                    return true;
                }
            }

            return false;
        }

        if (entranceLadder != null && entranceLadder.isActiveAndEnabled)
        {
            if (entranceLadder.targetTransform == null)
            {
                return false;
            }

            player.SetPosition(entranceLadder.targetTransform.position);
            player.SetCurrentSub(entranceLadder.GetComponentInParent<SubRoot>(), false);
            moveSessionHoveredBaseLadder = null;
            moveSessionHoveredEntranceLadder = null;
            return true;
        }

        return false;
    }
}
