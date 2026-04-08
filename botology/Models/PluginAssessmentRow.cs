namespace botology.Models;

public sealed record PluginAssessmentRow(
    PluginCatalogEntry Entry,
    PluginRuntimeState? RuntimeState,
    AssessmentResult Assessment,
    bool Ignored)
{
    public bool IsInstalled => RuntimeState != null;

    public bool IsLoaded => RuntimeState?.IsLoaded == true;

    public bool IsAssessable => IsLoaded;
}
