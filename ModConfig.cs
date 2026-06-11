using Nautilus.Json;
using Nautilus.Options.Attributes;

namespace ILikeToMoveIt;

[Menu("I Like To Move It")]
internal sealed class ModConfig : ConfigFile
{
    [Toggle("Block non-empty Lockers")]
    public bool PreventMoveIfNotEmpty = false;

    [Toggle("Block non-empty Alien Containment")]
    public bool PreventMoveWaterParkIfNotEmpty = false;

    [Toggle("Block non-empty BioReactor")]
    public bool PreventMoveBioReactorIfNotEmpty = false;

    [Toggle("Block non-empty NuclearReactor")]
    public bool PreventMoveNuclearReactorIfNotEmpty = false;

    [Toggle("Allow moving Miscellaneous")]
    public bool AllowMiscellaneous = true;

    [Toggle("Allow moving Interior Modules")]
    public bool AllowInteriorModules = true;

    [Toggle("Allow moving Exterior Modules")]
    public bool AllowExteriorModules = true;

    [Toggle("Allow moving Interior Pieces")]
    public bool AllowInteriorPieces = true;
}
