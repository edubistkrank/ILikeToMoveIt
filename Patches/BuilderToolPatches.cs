using HarmonyLib;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
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

    [HarmonyPatch(typeof(BuilderTool), "OnLeftHandDown")]
    private static class BuilderTool_OnLeftHandDown_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (TryUseLadderDuringMovePlacement())
            {
                __result = false;
                return false;
            }

            if (moveSessionActive && moveBackend == MoveBackend.FloatingLocker)
            {
                moveSessionCommitted = true;
                Builder.End();
                __result = false;
                return false;
            }

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

    [HarmonyPatch(typeof(BuilderTool), "OnRightHandDown")]
    private static class BuilderTool_OnRightHandDown_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!moveSessionActive || moveBackend != MoveBackend.FloatingLocker)
            {
                return true;
            }

            moveSessionCommitted = false;
            Builder.End();
            __result = false;
            return false;
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
            bool showingLadderHover = TryShowLadderHoverDuringMovePlacement();
            if (showingLadderHover)
            {
                if (GameInput.GetButtonDown(GameInput.Button.LeftHand))
                {
                    TryUseLadderDuringMovePlacement();
                }

                SetMoveReticleIcon(false);
                return;
            }

            if (moveSessionActive && moveBackend == MoveBackend.FloatingLocker)
            {
                UpdateFloatingLockerPreview();

                HandReticle mainReticle = HandReticle.main;
                if (mainReticle != null)
                {
                    mainReticle.SetText(HandReticle.TextType.Hand, L("Mover contenedor impermeable", "Move waterproof locker"), false, GameInput.Button.LeftHand);
                    mainReticle.SetText(HandReticle.TextType.HandSubscript, string.Empty, false, GameInput.Button.None);
                    mainReticle.SetIcon(HandReticle.IconType.Hand, 1f);
                }

                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    moveSessionCommitted = false;
                    Builder.End();
                    return;
                }

                SetMoveReticleIcon(true);
                return;
            }

            if (!IsMoveModifierHeld() || Builder.isPlacing || moveSessionActive)
            {
                SetMoveReticleIcon(false);
                return;
            }

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

            if (IsFloatingLockerMoveTarget(target, out _, out _) && distance <= 11f)
            {
                HandReticle main = HandReticle.main;
                string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
                main.SetText(HandReticle.TextType.Hand, L("Contenedor impermeable", "Waterproof locker"), false, GameInput.Button.None);
                main.SetText(HandReticle.TextType.HandSubscript, $"{L("Mover", "Move")} (Alt + {left})", false, GameInput.Button.None);
                main.SetIcon(HandReticle.IconType.Hand, 1f);
                SetMoveReticleIcon(true);
                return;
            }

            SetMoveReticleIcon(false);
        }
    }
}
