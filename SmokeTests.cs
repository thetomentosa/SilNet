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
        await using (var reconnect = new EngineClient(false)) { await reconnect.PingAsync(); }
        checks.Add("PASS: Pipe kapanışından sonra yeni bağlantı");
        Check(Updates.IsNewer("v0.10.0", "0.2.0"), "Sürüm kıyaslaması sayısal yapılır");
        Check(!Updates.IsNewer("v0.2.0", "0.2.0"), "Aynı sürüm güncelleme olarak gösterilmez");
        Check(!Updates.IsTrustedReleasePage(new Uri("https://github.com.evil.example/thetomentosa/SilNet/releases/v1")), "Sahte GitHub alan adı reddedilir");
        Check(!Updates.IsTrustedReleasePage(new Uri("https://github.com/other/SilNet/releases/v1")), "Başka depodan güncelleme reddedilir");
        Check(Provider.All.All(p => p.Candidates.All(id => Profile.All.Any(x => x.Id == id))), "Bütün sağlayıcı profilleri geçerli motor yöntemlerine bağlıdır");
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
