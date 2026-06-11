using Nautilus.Json;
using Nautilus.Options.Attributes;

namespace ILikeToMoveIt;

[Menu("I Like To Move It")]
internal sealed class ModConfig : ConfigFile
{
    [Toggle("Block moving non-empty Lockers")]
    public bool PreventMoveIfNotEmpty = false;

    [Toggle("Block moving non-empty Alien Containment")]
    public bool PreventMoveWaterParkIfNotEmpty = false;

    [Toggle("Allow moving External Modules")]
    public bool AllowExternalModules = true;

    [Toggle("Allow moving Interior Modules")]
    public bool AllowInteriorModules = true;

    [Toggle("Allow moving Interior Pieces")]
    public bool AllowInteriorPieces = true;

    [Toggle("Allow moving Miscellaneous Items")]
    public bool AllowMiscellaneousItems = true;

    [Toggle("Allow moving Base Pieces")]
    public bool AllowBasePieces = true;
}
