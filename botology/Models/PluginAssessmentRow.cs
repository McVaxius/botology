namespace botology.Models;

public sealed record PluginAssessmentRow(
    PluginCatalogEntry Entry,
    PluginRuntimeState? RuntimeState,
    AssessmentResult Assessment,
    bool Ignored,
    PluginRepositoryMetadata? Metadata = null)
{
    public const int CurrentDalamudApiLevel = 15;

    public bool IsInstalled => RuntimeState != null;

    public bool IsLoaded => RuntimeState?.IsLoaded == true;

    public bool IsAssessable => IsLoaded;

    public bool HasLocalChanges => Entry.HasLocalChanges;

    public bool IsUnavailableForCurrentPatch
        => Metadata?.DalamudApiLevel is int apiLevel && apiLevel < CurrentDalamudApiLevel;
}
