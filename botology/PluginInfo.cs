namespace botology;

internal static class PluginInfo
{
    public const string DisplayName = "Botology";
    public const string InternalName = "botology";
    public const string Command = "/botology";
    public const string Visibility = "Private";
    public const string Summary = "Compatibility manager for semi-passive plugins so overlapping bots stop stepping on each other.";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";
    public const string DiscordUrl = "https://discord.gg/VsXqydsvpu";
    public const string DiscordFeedbackNote = "Scroll down to \"The Dumpster Fire\" channel to discuss issues / suggestions for specific plugins.";
    public const string Description = "Let's face it, you are a Botologist now, you need proper analysis of all of your tools so they work and play nice together.";

    public static readonly string[] CommandAliases =
    {
        "/bot",
        "/bottist",
        "/botologist",
    };
}
