using Nautilus.Json;
using Nautilus.Options.Attributes;

namespace ILikeToMoveIt;

[Menu("I Like To Move It")]
internal sealed class ModConfig : ConfigFile
{
    [Toggle("Block moving non-empty lockers")]
    public bool PreventMoveIfNotEmpty = false;
}
