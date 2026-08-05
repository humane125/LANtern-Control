using Lantern.Core.Control;

namespace Lantern.Core.Services;

public static class ServiceDefinitionCatalog
{
    public static ServiceDefinition Other { get; } =
        new("other", "Other", []);

    public static IReadOnlyList<ServiceDefinition> All { get; } =
    [
        Define("youtube", "YouTube", "youtube.com", "youtu.be", "youtube-nocookie.com",
            "googlevideo.com", "ytimg.com", "youtubei.googleapis.com", "youtube.googleapis.com"),
        Define("discord", "Discord", "discord.com", "discord.gg", "discordapp.com",
            "discordapp.net", "discord.media", "discordcdn.com"),
        Define("instagram", "Instagram", "instagram.com", "cdninstagram.com", "instagr.am"),
        Define("facebook", "Facebook", "facebook.com", "facebook.net", "fb.com", "fbcdn.net"),
        Define("messenger", "Messenger", "messenger.com", "m.me"),
        Define("snapchat", "Snapchat", "snapchat.com", "sc-cdn.net", "snap.com", "snapkit.com"),
        Define("tiktok", "TikTok", "tiktok.com", "tiktokv.com", "tiktokcdn.com",
            "byteoversea.com", "ibyteimg.com", "muscdn.com"),
        Define("netflix", "Netflix", "netflix.com", "netflix.net", "nflxvideo.net",
            "nflximg.net", "nflxext.com", "nflxso.net"),
        Define("twitch", "Twitch", "twitch.tv", "twitchcdn.net", "jtvnw.net"),
        Define("spotify", "Spotify", "spotify.com", "spotifycdn.com", "scdn.co"),
        Define("steam", "Steam", "steampowered.com", "steamcommunity.com", "steamstatic.com",
            "steamcontent.com", "steamserver.net"),
        Define("epic-games", "Epic Games", "epicgames.com", "unrealengine.com",
            "epicgames.dev"),
        Define("xbox", "Xbox", "xbox.com", "xboxlive.com", "xboxservices.com"),
        Define("playstation", "PlayStation", "playstation.com", "playstation.net", "sonyentertainmentnetwork.com"),
        Define("whatsapp", "WhatsApp", "whatsapp.com", "whatsapp.net"),
        Define("telegram", "Telegram", "telegram.org", "telegram.me", "t.me"),
    ];

    public static ServiceDefinition MatchDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return Other;
        }

        string normalized;
        try
        {
            normalized = TrafficPolicy.NormalizeDomain(domain);
        }
        catch (FormatException)
        {
            return Other;
        }

        return All.FirstOrDefault(service => service.Domains.Any(candidate =>
                   MatchesDomain(normalized, candidate))) ??
               Other;
    }

    private static bool MatchesDomain(string normalized, string candidate) =>
        normalized.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
        (normalized.Length > candidate.Length &&
         normalized[normalized.Length - candidate.Length - 1] == '.' &&
         normalized.AsSpan().EndsWith(
             candidate.AsSpan(),
             StringComparison.OrdinalIgnoreCase));

    private static ServiceDefinition Define(string id, string name, params string[] domains) =>
        new(id, name, domains);
}
