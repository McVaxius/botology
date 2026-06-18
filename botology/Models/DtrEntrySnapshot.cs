namespace botology.Models;

public sealed record DtrEntrySnapshot(
    string Title,
    string Text,
    string Tooltip,
    bool PluginShown,
    bool UserHidden,
    bool HasClickAction,
    int Order,
    string? OwnerInternalName = null,
    string? OwnerName = null,
    string? OwnerManifestInternalName = null,
    string? OwnerManifestName = null)
{
    internal object? OwnerPluginHandle { get; init; }

    public bool IsOwnerMatched { get; init; }

    public bool HasOwnerMetadata =>
        OwnerPluginHandle != null ||
        !string.IsNullOrWhiteSpace(OwnerInternalName) ||
        !string.IsNullOrWhiteSpace(OwnerName) ||
        !string.IsNullOrWhiteSpace(OwnerManifestInternalName) ||
        !string.IsNullOrWhiteSpace(OwnerManifestName);

    public bool UserVisible => !UserHidden;

    public bool EffectiveVisible => PluginShown && !UserHidden;

    public string StateLabel
    {
        get
        {
            if (EffectiveVisible)
                return "Visible";

            return UserHidden ? "Hidden" : "Plugin hidden";
        }
    }
}
