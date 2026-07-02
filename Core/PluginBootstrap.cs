using BepInEx;
using HarmonyLib;
using Nautilus.Handlers;
using Nautilus.Options;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ILikeToMoveIt;

public sealed partial class Plugin
{
    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Settings = ILikeToMoveItConfig.Bind(Config);
        OptionsPanelHandler.RegisterModOptions(new ILikeToMoveItOptions(Settings));
        harmony = new Harmony(PluginInfo.Guid);
        LoadMoveReticleSprite();
        harmony.PatchAll();
        ApplyIsCellUnderConstructionPatch(harmony);
        StartCoroutine(WarmupReticleMovePath());
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

    private static IEnumerator WarmupReticleMovePath()
    {
        yield return null;

        string left = GameInput.FormatButton(GameInput.Button.LeftHand, false);
        string _ = $"{L("Mover", "Move")} (Alt + {left})";
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
}
