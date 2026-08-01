namespace Lantern.Core.Control;

public sealed record DomainBlockPreset(string Name, IReadOnlyList<string> Domains)
{
    public string Summary => $"{Domains.Count} domain{(Domains.Count == 1 ? string.Empty : "s")}";
}

public static class DomainBlockPresetCatalog
{
    public static IReadOnlyList<DomainBlockPreset> All { get; } =
    [
        new DomainBlockPreset(
            "YouTube",
            [
                "youtube.com",
                "youtu.be",
                "youtube-nocookie.com",
                "googlevideo.com",
                "ytimg.com",
                "youtubei.googleapis.com",
                "youtube.googleapis.com",
            ]),
        new DomainBlockPreset(
            "Instagram",
            [
                "instagram.com",
                "cdninstagram.com",
                "instagr.am",
                "facebook.com",
                "facebook.net",
                "fbcdn.net",
                "fbsbx.com",
            ]),
        new DomainBlockPreset(
            "Facebook",
            [
                "facebook.com",
                "facebook.net",
                "fb.com",
                "fbcdn.net",
                "fbsbx.com",
            ]),
        new DomainBlockPreset(
            "Snapchat",
            [
                "snapchat.com",
                "sc-cdn.net",
                "snap.com",
                "snapkit.com",
            ]),
        new DomainBlockPreset(
            "Discord",
            [
                "discord.com",
                "discord.gg",
                "discordapp.com",
                "discordapp.net",
                "discord.media",
                "discordcdn.com",
            ]),
        new DomainBlockPreset(
            "Messenger",
            [
                "messenger.com",
                "m.me",
                "facebook.com",
                "facebook.net",
                "fbcdn.net",
                "fbsbx.com",
            ]),
    ];
}
