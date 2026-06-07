using Nautilus.Json;
using Nautilus.Options.Attributes;

namespace ILikeToMoveIt;

[Menu("I Like To Move It")]
internal sealed class ModConfig : ConfigFile
{
    [Toggle("No mover si tiene items")]
    public bool PreventMoveIfNotEmpty = false;
}
