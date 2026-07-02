using Nautilus.Options;

namespace ILikeToMoveIt;

internal sealed class ILikeToMoveItOptions : ModOptions
{
    private readonly ILikeToMoveItConfig config;

    internal ILikeToMoveItOptions(ILikeToMoveItConfig config) : base("I Like To Move It")
    {
        this.config = config;
        AddOptions();
    }

    private void AddOptions()
    {
        AddToggle(
            "PreventMoveIfNotEmpty",
            "Block moving non-empty Lockers",
            "Block moving non-empty Lockers.",
            config.PreventMoveIfNotEmpty);

        AddToggle(
            "PreventMoveWaterParkIfNotEmpty",
            "Block moving non-empty Alien Containment",
            "Block moving non-empty Alien Containment.",
            config.PreventMoveWaterParkIfNotEmpty);

        AddToggle(
            "AllowExternalModules",
            "Allow moving External Modules",
            "Allow moving External Modules.",
            config.AllowExternalModules);

        AddToggle(
            "AllowInteriorModules",
            "Allow moving Interior Modules",
            "Allow moving Interior Modules.",
            config.AllowInteriorModules);

        AddToggle(
            "AllowInteriorPieces",
            "Allow moving Interior Pieces",
            "Allow moving Interior Pieces.",
            config.AllowInteriorPieces);

        AddToggle(
            "AllowMiscellaneousItems",
            "Allow moving Miscellaneous Items",
            "Allow moving Miscellaneous Items.",
            config.AllowMiscellaneousItems);

        AddToggle(
            "AllowBasePieces",
            "Allow moving Base Pieces",
            "Allow moving Base Pieces.",
            config.AllowBasePieces);
    }

    private void AddToggle(string id, string label, string tooltip, BepInEx.Configuration.ConfigEntry<bool> entry)
    {
        var option = ModToggleOption.Create(id, label, entry.Value, tooltip);
        option.OnChanged += (_, args) => entry.Value = args.Value;
        AddItem(option);
    }
}
