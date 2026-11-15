using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SilNet;
public static class SmokeTests
{
    public static async Task RunAsync(string destination)
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks.Add("PASS: " + label); }
        Check(Domains.Parse("discord.com\nDiscord.com\nroblox.com").Length == 2, "Alan adları yinelenmeden saklanır");
        foreach (var input in new[] { "https://discord.com", "discord.com & calc.exe", "--lua-init=evil", "127.0.0.1", "discord..com", "" })
        {
            bool rejected = false; try { Domains.Parse(input); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Geçersiz hedef reddedildi: " + input);
        }
        await EngineIntegrity.VerifyAsync(); checks.Add("PASS: Resmi motor paketinin bütünlüğü doğrulandı");
        var temporary = Path.Combine(Path.GetTempPath(), "SilNet-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var path in Directory.GetFiles(EngineIntegrity.EngineDirectory, "*", SearchOption.AllDirectories))
            {
                var output = Path.Combine(temporary, Path.GetRelativePath(EngineIntegrity.EngineDirectory, path));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(path, output);
            }
            await File.AppendAllTextAsync(Path.Combine(temporary, "lua", "zapret-antidpi.lua"), "\n--tampered");
            bool rejected = false;
            try { await EngineIntegrity.VerifyAsync(temporary); } catch (IOException) { rejected = true; }
            Check(rejected, "Değiştirilmiş motor paketi çalıştırılmadan reddedilir");
        }
        finally { Directory.Delete(temporary, true); }
        // Validate real OS lifetime cleanup without starting a network driver or requesting elevation.
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--test-child");
        using (var child = Process.Start(info)!)
        {
            using (var job = new KillJob()) { job.Assign(child); Check(!child.HasExited, "Deneme alt süreci çalışıyor"); }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(child.HasExited, "Job kapatılınca alt süreç işletim sistemi tarafından durdurulur");
        }
        await using (var ipc = new EngineClient(false))
        {
            await ipc.PingAsync(); checks.Add("PASS: Gerçek ayrı yardımcı süreç ile ACL + PID doğrulamalı pipe el sıkışması");
            await ipc.PingAsync(); await ipc.StopAsync(); await ipc.PingAsync();
            checks.Add("PASS: Ardışık pipe istekleri ve stop sonrası yeniden kullanım");
            using var helper = Process.GetProcessById(ipc.WorkerId!.Value);
            helper.Kill(); await helper.WaitForExitAsync();
            await ipc.PingAsync();
            checks.Add("PASS: Yardımcı beklenmedik kapanınca aynı istemci yeni pipe bağlantısı kurar");
        }
        var reconnect = new EngineClient(false); await reconnect.PingAsync();
        await reconnect.DisposeAsync();
        Check(reconnect.LastExitCode == 0, "Arayüz kapatınca yardımcı 0 koduyla ve hata penceresi olmadan sonlanır");
        checks.Add("PASS: Pipe kapanışından sonra yeni bağlantı");
        Check(Updates.IsNewer("v0.10.0", "0.2.0"), "Sürüm kıyaslaması sayısal yapılır");
        Check(!Updates.IsNewer("v0.2.0", "0.2.0"), "Aynı sürüm güncelleme olarak gösterilmez");
        Check(!Updates.IsTrustedReleasePage(new Uri("https://github.com.evil.example/thetomentosa/SilNet/releases/v1")), "Sahte GitHub alan adı reddedilir");
        Check(!Updates.IsTrustedReleasePage(new Uri("https://github.com/other/SilNet/releases/v1")), "Başka depodan güncelleme reddedilir");
        Check(Provider.All.All(p => p.Candidates.All(id => Profile.All.Any(x => x.Id == id))), "Bütün sağlayıcı profilleri geçerli motor yöntemlerine bağlıdır");
        var dns = Convert.FromHexString("12340100000100000000000007646973636F726403636F6D0000010001");
        var packet = new byte[28 + dns.Length]; packet[0] = 0x45; packet[9] = 17; packet[12] = 10; packet[15] = 2; packet[16] = 192; packet[17] = 168; packet[18] = 1; packet[19] = 1;
        packet[20] = 0xC4; packet[21] = 0x50; packet[23] = 53; packet[25] = (byte)(8 + dns.Length); dns.CopyTo(packet, 28);
        Check(DnsBypass.TryQuestion(packet, out var question) && question.Host == "discord.com", "IPv4 UDP DNS sorusu doğru ayrıştırılır");
        Check(DnsBypass.Matches("updates.discord.com", ["discord.com"]) && !DnsBypass.Matches("evildiscord.com", ["discord.com"]) && !DnsBypass.Matches("discord.com.evil.net", ["discord.com"]), "DNS hedef sınırı yalnızca gerçek alt alan adlarını kapsar");
        var merged = TargetService.Merge(["discord.com", "private.example.com"], [TargetService.All.Single(s => s.Id == "roblox")]);
        Check(merged.Contains("private.example.com") && merged.Contains("roblox.com") && !merged.Contains("discord.com"), "Uygulama seçimi özel alan adlarını korur ve kaldırılan seçimi temizler");
        Check(!DnsBypass.TryQuestion(packet.AsSpan(0, 31), out _), "Kesilmiş DNS paketi değiştirilmeden bırakılır");
        var answer = dns.ToArray(); answer[2] = 0x81; answer[3] = 0x80;
        var reply = DnsBypass.BuildResponse(packet, question, answer);
        Check(reply[12] == 192 && reply[16] == 10 && reply[21] == 53 && reply[22] == 0xC4 && reply[23] == 0x50 && reply[28] == 0x12 && reply[29] == 0x34, "DNS yanıtı IP/port yönlerini ve işlem kimliğini korur");
        checks.Add("NOT RUN: Yönetici izniyle WinDivert yükleme / gerçek ISS erişimi / UDP");
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static async Task ElevatedAsync(string destination)
    {
        var checks = new List<string>();
        async Task Record(string text) { checks.Add(text); await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true })); }
        await Record("START: UAC ile ayrı yardımcı süreç + motor testi. DNS ayarları değiştirilmez.");
        await using var engine = new EngineClient();
        engine.Log += text => AppLog.Write("Elevated test", text);
        await engine.PingAsync(); await Record("PASS: Normal yetkili arayüz ile yönetici yardımcı arasında ACL + PID doğrulamalı pipe bağlantısı");
        foreach (var profile in Profile.All)
        {
            await engine.DryRunAsync(profile.Id, Domains.Defaults);
            await Record("PASS: Gerçek winws2 --dry-run: " + profile.Id);
        }
        await engine.DryRunAsync(Profile.All[0].Id, Domains.Defaults, includeQuic: true);
        await Record("PASS: İsteğe bağlı QUIC profili de gerçek motor tarafından kabul edildi.");
        if (Diagnostics.Conflicts().Length > 0) { await Record("SKIP: Başka DPI motoru çalıştığı için canlı sürücü testi yapılmadı."); return; }
        foreach (var profile in Profile.All)
        {
            await engine.StartAsync(profile.Id, Domains.Defaults);
            if (!engine.Running) throw new IOException("Motor başlamadı: " + profile.Id);
            await Record("PASS: Gerçek WinDivert motor başlangıcı: " + profile.Id);
            var results = await Task.WhenAll(new[] { "discord.com", "roblox.com" }.Select(host => Diagnostics.ProbeAsync(host)));
            foreach (var result in results) await Record($"NETWORK {profile.Id}: {result.Host}: {result.Detail} ({result.Milliseconds} ms)");
            await engine.StopAsync(); await Record("PASS: Gerçek motor durdurma: " + profile.Id);
        }
    }
}
