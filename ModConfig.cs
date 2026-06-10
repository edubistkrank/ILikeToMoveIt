using Nautilus.Json;
using Nautilus.Options.Attributes;

namespace ILikeToMoveIt;

[Menu("I Like To Move It")]
internal sealed class ModConfig : ConfigFile
{
    [Toggle("Block moving non-empty lockers")]
    public bool PreventMoveIfNotEmpty = false;

    [Toggle("Block moving non-empty WaterParks")]
    public bool PreventMoveWaterParkIfNotEmpty = false;

    [Toggle("Allow moving Miscellaneous")]
    public bool AllowMiscellaneous = true;

    [Toggle("Allow moving Interior Modules")]
    public bool AllowInteriorModules = true;

    [Toggle("Allow moving Exterior Modules")]
    public bool AllowExteriorModules = true;

    [Toggle("Allow moving Interior Pieces")]
    public bool AllowInteriorPieces = true;
}
