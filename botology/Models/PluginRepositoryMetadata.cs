using System;

namespace botology.Models;

public sealed record PluginRepositoryMetadata(
    string? Description = null,
    string? Author = null,
    int? DalamudApiLevel = null,
    long? Downloads = null,
    DateTimeOffset? LastUpdateUtc = null);
