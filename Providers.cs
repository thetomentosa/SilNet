using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace SilNet;
public sealed record ConnectionMemory(string ProfileId, DateTimeOffset CheckedAt, string[] TestedHosts);
public sealed record Provider(string Id, string Name, string Category, string[] Candidates)
{
    // Community starting points, not ISP certifications. TT/SOL/Kablonet/mobile
    // methods adapted from cagritaskn/SplitWire-Turkey presets2.txt (MIT).
    public static readonly Provider[] All = [
        new("auto", "Otomatik / bilmiyorum", "En uygun yöntemi test ederek bul", ["overlap", "split", "disorder", "fake", "vodafone", "telekom-mobile"]),
        new("turktelekom", "Türk Telekom", "Fiber · VDSL · ADSL", ["overlap", "disorder", "split", "fake"]),
        new("superonline", "Turkcell Superonline", "Fiber · VDSL", ["overlap", "disorder", "split", "fake"]),
        new("turknet", "TurkNet", "GigaFiber · VDSL", ["overlap", "split", "disorder", "fake"]),
        new("vodafone", "Vodafone Net", "Fiber · VDSL", ["vodafone", "overlap", "split", "fake"]),
        new("kablonet", "Türksat Kablonet", "Kablo · Fiber", ["overlap", "disorder", "split", "fake"]),
        new("millenicom", "Millenicom", "Fiber · VDSL", ["overlap", "split", "disorder", "fake"]),
        new("netspeed", "Netspeed", "Fiber · VDSL", ["overlap", "split", "disorder", "fake"]),
        new("comnet", "Comnet", "Fiber · VDSL", ["overlap", "split", "disorder", "fake"]),
        new("ttmobile", "Türk Telekom Mobil", "Mobil veri · Hotspot", ["telekom-mobile", "overlap", "split", "fake"]),
        new("turkcellmobile", "Turkcell Mobil", "Mobil veri · Superbox", ["overlap", "disorder", "split", "fake"]),
        new("vodafonemobile", "Vodafone Mobil", "Mobil veri · Hotspot", ["overlap", "vodafone", "split", "fake"])
    ];
    public static Provider Find(string id) => All.FirstOrDefault(x => x.Id == id) ?? All[0];
    public override string ToString() => Name;
}
public static class LocalNetwork
{
    private static NetworkInterface? Active => NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.GetIPProperties().GatewayAddresses.Count > 0);
    public static string Label => Active?.Name ?? "Ağ bağlantısı bulunamadı";
    // Local-only fingerprint; neither interface identity nor gateway is sent to a server.
    public static string Key(string provider)
    {
        var adapter = Active;
        string value = provider + "|" + adapter?.Id + "|" + string.Join(',', adapter?.GetIPProperties().GatewayAddresses.Select(x => x.Address.ToString()) ?? []);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }
}
