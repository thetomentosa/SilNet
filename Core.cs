using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SilNet;

public sealed record Profile(string Id, string Name, string Detail, string Strategy)
{
    public static readonly Profile[] All = [
        new("overlap", "Türkiye · Örtüşmeli", "Türkiye sağlayıcı önayarlarından uyarlanmış başlangıç yöntemi. Bu ağda test edilmeli.", "multidisorder:pos=2:seqovl=1"),
        new("split", "Dengeli", "TLS isteğini parçalara ayırır. Düşük müdahaleli alternatif.", "multisplit:pos=1,midsld"),
        new("disorder", "Ters sıralı", "TLS parçalarını farklı sırayla gönderir.", "multidisorder:pos=1,midsld"),
        new("vodafone", "Vodafone · Alternatif", "TTL 5 ile parçalama. Ağ rotasına göre sonuç değişebilir.", "multisplit:blob=fake_default_tls:ip_ttl=5:pos=2:nodrop:repeats=1"),
        new("telekom-mobile", "Telekom Mobil · Alternatif", "Kısa ömürlü sahte paket. Mobil bağlantıda test edilmeli.", "fake:blob=0x00000000:ip_ttl=5:repeats=1"),
        new("fake", "Uyumluluk · Sahte TLS", "Sahte TLS paketi ve ters sıralı parçalama uygular.", "multidisorder:pos=midsld")
    ];
    public static Profile Find(string id) => All.FirstOrDefault(p => p.Id == id) ?? throw new InvalidDataException("Geçersiz profil.");
    public override string ToString() => Name;
    public string[] Arguments(string engineDir, string hostsPath, bool includeQuic = false)
    {
        var args = new List<string> { "--wf-tcp-out=443", "--lua-init=@" + Path.Combine(engineDir, "lua", "zapret-lib.lua"), "--lua-init=@" + Path.Combine(engineDir, "lua", "zapret-antidpi.lua"), "--filter-tcp=443", "--filter-l7=tls", "--hostlist=" + hostsPath, "--out-range=-d10", "--payload=tls_client_hello" };
        if (includeQuic) args.Insert(1, "--wf-udp-out=443");
        if (Id == "fake") args.Add("--lua-desync=fake:blob=fake_default_tls:tcp_md5:tcp_seq=-10000:repeats=6");
        args.Add("--lua-desync=" + Strategy);
        if (includeQuic) args.AddRange(["--new", "--filter-udp=443", "--filter-l7=quic", "--hostlist=" + hostsPath, "--payload=quic_initial", "--lua-desync=fake:blob=fake_default_quic:repeats=6"]);
        return args.ToArray();
    }
}

public static class Domains
{
    public static readonly string[] Defaults = ["discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media", "roblox.com", "rbxcdn.com"];
    public static string[] Parse(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length is 0 or > 200) throw new InvalidDataException("1–200 alan adı girin; her satıra bir alan adı yazın.");
        return lines.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string Normalize(string value)
    {
        string host;
        try { host = new IdnMapping().GetAscii(value.Trim().TrimEnd('.')).ToLowerInvariant(); }
        catch { throw new InvalidDataException("Geçersiz alan adı: " + value); }
        if (host.Length > 253 || !host.Contains('.') || !Regex.IsMatch(host, @"\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+\z") || host.Split('.').Any(x => x.Length > 63) || System.Net.IPAddress.TryParse(host, out _))
            throw new InvalidDataException("Yalnızca alan adı yazın (örnek: discord.com). URL, IP ve komut kabul edilmez.");
        return host;
    }
}

public sealed class Settings
{
    public string ProfileId { get; set; } = "overlap";
    public string ProviderId { get; set; } = "auto";
    public bool AutoUpdates { get; set; } = true;
    public bool Automatic { get; set; } = true;
    public bool SecureDns { get; set; } = true;
    public bool UseQuic { get; set; }
    public bool OnboardingComplete { get; set; }
    public Dictionary<string, ConnectionMemory> Networks { get; set; } = new();
    public string[] Hosts { get; set; } = Domains.Defaults;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SilNet");
    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            Profile.Find(settings.ProfileId); settings.ProviderId = Provider.Find(settings.ProviderId).Id;
            settings.Networks = (settings.Networks ?? new()).Where(p => p.Value is not null && Profile.All.Any(profile => profile.Id == p.Value.ProfileId)).ToDictionary(p => p.Key, p => p.Value);
            settings.Hosts = Domains.Parse(string.Join('\n', settings.Hosts)); return settings;
        }
        catch { return new(); }
    }
    public void Save()
    {
        Profile.Find(ProfileId); Hosts = Domains.Parse(string.Join('\n', Hosts));
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}

public static class EngineIntegrity
{
    public const string Commit = "6b4375fa04f6bd7017eec379e16e462d3ea8df66";
    public static string EngineDirectory => Path.Combine(AppContext.BaseDirectory, "engines", "zapret2");
    public static async Task VerifyAsync(string? root = null)
    {
        root ??= EngineDirectory;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SilNet.engine-integrity.json") ?? throw new IOException("Doğrulama listesi bulunamadı.");
        var hashes = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream) ?? [];
        if (hashes.Count < 5) throw new InvalidDataException("Motor paketi hazırlanmadı.");
        foreach (var pair in hashes)
        {
            var path = Path.GetFullPath(Path.Combine(root, pair.Key));
            if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Geçersiz paket yolu.");
            if (!File.Exists(path)) throw new IOException("Motor dosyası eksik: " + pair.Key + ". Paketi yeniden çıkarın.");
            using var file = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Motor dosyası doğrulanamadı: " + pair.Key + ". Orijinal paketi yeniden çıkarın.");
        }
    }
}

public sealed record ProbeResult(string Host, bool Success, string Detail, long Milliseconds, long DnsMilliseconds = 0)
{
    public string TotalLabel => $"{Milliseconds} ms toplam";
    public string TimingInfo => $"DNS: {DnsMilliseconds} ms · Toplam HTTPS: {Milliseconds} ms. Bu değer Discord ses veya oyun pingi değildir.";
}
public static class Diagnostics
{
    public static async Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellation = default)
    {
        host = Domains.Normalize(host);
        var watch = Stopwatch.StartNew();
        long dnsMilliseconds = 0;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, deadline.Token);
            dnsMilliseconds = watch.ElapsedMilliseconds;
            if (addresses.Any(ip => ip.ToString() == "195.175.254.2")) return new(host, false, "DNS engelleme yanıtı algılandı; şifreli DNS gerekli", watch.ElapsedMilliseconds);
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12), DefaultRequestVersion = System.Net.HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
            string path = host == "discord.com" ? "/api/v10/gateway" : "/";
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://" + host + path);
            request.Headers.UserAgent.ParseAdd("SilNet/0.3");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            // 4xx responses prove TLS/HTTP transport, but do not prove application access.
            var success = (int)response.StatusCode is >= 200 and < 400;
            return new(host, success, success ? "HTTPS yanıtı alındı" : $"HTTP {(int)response.StatusCode} — erişim doğrulanamadı", watch.ElapsedMilliseconds, dnsMilliseconds);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { return new(host, false, "Bağlantı zaman aşımına uğradı", watch.ElapsedMilliseconds); }
        catch (OperationCanceledException) { return new(host, false, "Bağlantı zaman aşımına uğradı", watch.ElapsedMilliseconds); }
        catch (System.Net.Sockets.SocketException) { return new(host, false, "DNS çözümlenemedi", watch.ElapsedMilliseconds); }
        catch (HttpRequestException ex) { return new(host, false, ex.HttpRequestError switch { HttpRequestError.NameResolutionError => "DNS çözümlenemedi", HttpRequestError.SecureConnectionError => "Güvenli bağlantı kurulamadı", _ => "HTTPS bağlantısı kurulamadı" }, watch.ElapsedMilliseconds); }
    }
    public static string[] Conflicts() => new[] { "winws", "winws2", "goodbyedpi" }.Where(name => Process.GetProcessesByName(name).Any()).ToArray();
}
