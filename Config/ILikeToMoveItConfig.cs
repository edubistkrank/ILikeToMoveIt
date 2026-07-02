using BepInEx.Configuration;

namespace ILikeToMoveIt;

internal sealed class ILikeToMoveItConfig
{
    internal ConfigEntry<bool> PreventMoveIfNotEmpty { get; private set; }

    internal ConfigEntry<bool> PreventMoveWaterParkIfNotEmpty { get; private set; }

    internal ConfigEntry<bool> AllowExternalModules { get; private set; }

    internal ConfigEntry<bool> AllowInteriorModules { get; private set; }

    internal ConfigEntry<bool> AllowInteriorPieces { get; private set; }

    internal ConfigEntry<bool> AllowMiscellaneousItems { get; private set; }

    internal ConfigEntry<bool> AllowBasePieces { get; private set; }

    internal static ILikeToMoveItConfig Bind(ConfigFile config)
    {
        return new ILikeToMoveItConfig
        {
            PreventMoveIfNotEmpty = config.Bind("Restrictions", "PreventMoveIfNotEmpty", false, "Block moving non-empty Lockers."),
            PreventMoveWaterParkIfNotEmpty = config.Bind("Restrictions", "PreventMoveWaterParkIfNotEmpty", false, "Block moving non-empty Alien Containment."),
            AllowExternalModules = config.Bind("Categories", "AllowExternalModules", true, "Allow moving External Modules."),
            AllowInteriorModules = config.Bind("Categories", "AllowInteriorModules", true, "Allow moving Interior Modules."),
            AllowInteriorPieces = config.Bind("Categories", "AllowInteriorPieces", true, "Allow moving Interior Pieces."),
            AllowMiscellaneousItems = config.Bind("Categories", "AllowMiscellaneousItems", true, "Allow moving Miscellaneous Items."),
            AllowBasePieces = config.Bind("Categories", "AllowBasePieces", true, "Allow moving Base Pieces.")
        };
    }
}
