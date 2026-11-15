using System.Windows;
using System.Windows.Media;
namespace SilNet;
public sealed record TargetService(string Id, string Name, string Category, string Hex, string[] Hosts)
{
    public Geometry Glyph => (Geometry)Application.Current.FindResource(char.ToUpperInvariant(Id[0]) + Id[1..] + "Logo");
    public Brush Color => (Brush)new BrushConverter().ConvertFromString(Hex)!;
    public static readonly TargetService[] All = [
        new("discord", "Discord", "Sohbet", "#5865F2", ["discord.com","discord.gg","discordapp.com","discordapp.net","discord.media"]),
        new("roblox", "Roblox", "Oyun", "#172B46", ["roblox.com","rbxcdn.com"]),
        new("wattpad", "Wattpad", "Hikâyeler", "#FF6122", ["wattpad.com","wattpad.net"]),
        new("youtube", "YouTube", "Video", "#FF0033", ["youtube.com","youtu.be","googlevideo.com","ytimg.com"]),
        new("instagram", "Instagram", "Sosyal", "#D3398D", ["instagram.com","cdninstagram.com"]),
        new("x", "X", "Sosyal", "#172B46", ["x.com","twitter.com","twimg.com","t.co"]),
        new("tiktok", "TikTok", "Video", "#172B46", ["tiktok.com","tiktokcdn.com","tiktokv.com"]),
        new("twitch", "Twitch", "Yayın", "#9146FF", ["twitch.tv","ttvnw.net","jtvnw.net"]),
        new("reddit", "Reddit", "Topluluk", "#FF4500", ["reddit.com","redd.it","redditstatic.com","redditmedia.com"]),
        new("telegram", "Telegram", "Sohbet · Web", "#26A5E4", ["telegram.org","t.me","telegram.me"]),
        new("whatsapp", "WhatsApp", "Sohbet · Web", "#25D366", ["whatsapp.com","whatsapp.net"]),
        new("spotify", "Spotify", "Müzik", "#1DB954", ["spotify.com","scdn.co","spotifycdn.com"]),
        new("steam", "Steam", "Oyun", "#213B5A", ["steampowered.com","steamcommunity.com","steamstatic.com","steamcontent.com"]),
        new("pornhub", "Pornhub", "Web", "#D98200", ["pornhub.com","phncdn.com"])
    ];
    public static string[] Merge(string[] existing, IEnumerable<TargetService> selected)
    {
        var known = All.SelectMany(s => s.Hosts).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return existing.Where(host => !known.Contains(host)).Concat(selected.SelectMany(s => s.Hosts)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
public sealed class ServiceChoice(TargetService service, bool selected)
{
    public TargetService Service { get; } = service;
    public bool Selected { get; set; } = selected;
}
