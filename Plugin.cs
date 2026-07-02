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
}
