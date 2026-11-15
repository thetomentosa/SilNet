using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SilNet;
public partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly EngineClient engine = new();
    private readonly bool preview;
    private CancellationTokenSource? cancellation;
    private ReleaseInfo? release;
    private bool busy, closing, allowClose, initialized, directAccess, checkingUpdate;
    private static readonly Dictionary<string, string[]> ServiceHosts = new()
    {
        ["discord"] = ["discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media"],
        ["roblox"] = ["roblox.com", "rbxcdn.com"],
        ["youtube"] = ["youtube.com", "youtu.be", "googlevideo.com", "ytimg.com"]
    };
    public MainWindow(bool previewMode = false)
    {
        preview = previewMode; InitializeComponent();
        ProviderPicker.ItemsSource = Provider.All; ProviderPicker.SelectedItem = Provider.Find(settings.ProviderId);
        ProfilePicker.ItemsSource = Profile.All; ProfilePicker.SelectedItem = Profile.Find(settings.ProfileId);
        ProfileDetail.Text = Profile.Find(settings.ProfileId).Detail;
        ProviderDetail.Text = Provider.Find(settings.ProviderId).Category;
        AutomaticCheck.IsChecked = settings.Automatic; AutoUpdatesCheck.IsChecked = settings.AutoUpdates;
        HostsEditor.Text = string.Join(Environment.NewLine, settings.Hosts);
        SetServiceChips(); VersionText.Text = "SilNet " + Updates.CurrentVersion + " · Windows x64";
        NetworkLabel.Text = LocalNetwork.Label;
        engine.Log += text => Dispatcher.BeginInvoke(() => AddLog(text));
        engine.Disconnected += () => Dispatcher.BeginInvoke(UpdateState);
        initialized = true; RefreshMemory(); UpdateState();
        AddLog("SilNet hazır. DNS ve güvenlik duvarı ayarları değiştirilmedi.");
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!preview && settings.AutoUpdates) { await Task.Delay(1500); if (!closing && settings.AutoUpdates) await CheckUpdates(false); }
    }
    private void AddLog(string text)
    {
        AppLog.Write("UI", text); LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {text}\n");
        if (LogBox.Text.Length > 50000) LogBox.Text = LogBox.Text[^35000..];
        LogBox.ScrollToEnd();
    }
    private void Say(string text, bool error = false)
    {
        string display = text.Length > 350 ? text[..350] + "… Ayrıntılar Tanılama’da." : text;
        Notice.Text = display; Notice.Foreground = new SolidColorBrush(error ? Color.FromRgb(172, 64, 80) : Color.FromRgb(99, 122, 157));
        AddLog(text);
    }
    private void UpdateState()
    {
        bool active = engine.Running;
        StateLabel.Text = busy ? "BAĞLANTI KONTROL EDİLİYOR" : active ? "SILNET ETKİN" : directAccess ? "ERİŞİM AÇIK" : "BAĞLANTI KAPALI";
        StateChip.Background = new SolidColorBrush(active || directAccess ? Color.FromRgb(232, 246, 244) : Color.FromRgb(238, 242, 248));
        StateDot.Fill = new SolidColorBrush(active || directAccess ? Color.FromRgb(38, 150, 136) : Color.FromRgb(145, 160, 183));
        HeroTitle.Text = busy ? "Senin için deneniyor…" : active ? "SilNet şimdi etkin." : directAccess ? "Zaten erişebiliyorsun." : "Bir dokunuşla başla.";
        HeroDescription.Text = active ? "Bağlantı yöntemin çalışıyor.\nSite erişimini ayrıca kontrol edebilirsin." : directAccess ? "Test edilen siteler motorsuz açılıyor.\nBağlantına müdahale etmeye gerek yok." : "Sağlayıcını seç. SilNet uygun yöntemi\nbu bağlantıda deneyerek bulsun.";
        ConnectButton.Content = busy ? "Bekle…" : active ? "Durdur" : directAccess ? "Yeniden dene" : "Bağlan";
        ConnectionHint.Text = active ? Profile.Find(settings.ProfileId).Name + " · Bu pencere kapanınca durur." : "Başlatırken Windows izin ister.";
        MotorStatus.Text = active ? "Çalışıyor · " + Profile.Find(settings.ProfileId).Name : "Kapalı";
        ConnectButton.IsEnabled = VerifyButton.IsEnabled = DiagTestButton.IsEnabled = !busy;
        ProviderPicker.IsEnabled = AutomaticCheck.IsEnabled = SaveHostsButton.IsEnabled = DefaultHostsButton.IsEnabled = HostsEditor.IsEnabled = !busy && !active;
        ProfilePicker.IsEnabled = !busy && !active && !settings.Automatic;
        DiscordChip.IsEnabled = RobloxChip.IsEnabled = YoutubeChip.IsEnabled = !busy && !active;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        NetworkLabel.Text = LocalNetwork.Label;
    }
    private void RefreshMemory()
    {
        var key = LocalNetwork.Key(settings.ProviderId);
        MemoryLabel.Text = settings.Networks.TryGetValue(key, out var memory)
            ? $"Son doğrulama: {memory.CheckedAt.LocalDateTime:dd.MM HH:mm}\n{Profile.Find(memory.ProfileId).Name} · Yalnızca bu cihazda."
            : "Henüz bu ağda doğrulanmış bir ayar yok.\nSonuçlar yalnızca cihazında saklanır.";
    }
    private async Task RunUi(Func<CancellationToken, Task> action, bool cancellable = false)
    {
        if (busy || closing) return;
        busy = true; cancellation = new(); UpdateState();
        CancelButton.IsEnabled = true; CancelButton.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { try { await engine.StopAsync(); } catch { } Say("İşlem iptal edildi. Motor durduruldu."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { Say("Windows izni verilmedi. Bağlan’a basıp tekrar deneyebilirsin."); }
        catch (Exception ex) { AppLog.Write("Operation", ex.ToString()); try { await engine.StopAsync(); } catch { } Say(ex.Message, true); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; CancelButton.Visibility = Visibility.Collapsed; UpdateState(); RefreshMemory(); }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunUi(async token =>
    {
        directAccess = false;
        if (engine.Running) { await engine.StopAsync(); Say("SilNet durduruldu. Ağ ayarların aynı kaldı."); return; }
        await EngineIntegrity.VerifyAsync(); token.ThrowIfCancellationRequested();
        if (settings.Automatic) await FindWorkingProfile(token);
        else
        {
            Say("Bağlantı motoru açılıyor. Windows izin isteyebilir.");
            await engine.StartAsync(settings.ProfileId, settings.Hosts); token.ThrowIfCancellationRequested(); UpdateState();
            Say("Yöntem etkin. Site erişimi kontrol ediliyor…");
            await ProbeAndRemember(token);
        }
    }, true);
    private string[] TestHosts()
    {
        var selected = new List<string>();
        foreach (var root in new[] { "discord.com", "roblox.com", "youtube.com" }) if (settings.Hosts.Contains(root)) selected.Add(root);
        if (selected.Count == 0) selected.AddRange(settings.Hosts.Take(3));
        return selected.Take(3).ToArray();
    }
    private async Task<ProbeResult[]> ProbeAll(CancellationToken token)
    {
        var results = await Task.WhenAll(TestHosts().Select(host => Diagnostics.ProbeAsync(host, token)));
        ResultsList.ItemsSource = results; int count = results.Count(x => x.Success);
        TestStatus.Text = $"{count}/{results.Length} site yanıt verdi";
        foreach (var result in results) AddLog($"{result.Host}: {result.Detail} ({result.Milliseconds} ms)");
        return results;
    }
    private void Remember(ProbeResult[] results, string network)
    {
        if (results.Length == 0 || !results.All(x => x.Success) || network != LocalNetwork.Key(settings.ProviderId)) return;
        settings.Networks[network] = new(settings.ProfileId, DateTimeOffset.Now, results.Select(x => x.Host).ToArray());
        settings.Save(); RefreshMemory();
    }
    private async Task ProbeAndRemember(CancellationToken token)
    {
        string network = LocalNetwork.Key(settings.ProviderId); var results = await ProbeAll(token);
        if (engine.Running) Remember(results, network);
        Say($"HTTPS kontrolü: {results.Count(x => x.Success)}/{results.Length} site yanıt verdi. Ayrıntılar Tanılama’da.");
    }
    private async Task FindWorkingProfile(CancellationToken token)
    {
        string network = LocalNetwork.Key(settings.ProviderId);
        Say("Önce mevcut bağlantın kontrol ediliyor…");
        var baseline = await ProbeAll(token); int baseScore = baseline.Count(x => x.Success);
        if (baseScore == baseline.Length) { directAccess = true; Say("Test edilen siteler zaten açılıyor. Motor kapalı bırakıldı."); return; }
        if (baseline.All(x => x.Detail == "DNS çözümlenemedi")) { Say("Alan adları çözümlenemiyor. Önce internet ve DNS ayarını kontrol et; SilNet DNS ayarını değiştirmez.", true); return; }
        var candidates = new List<string>();
        if (settings.Networks.TryGetValue(network, out var saved) && Profile.All.Any(p => p.Id == saved.ProfileId)) candidates.Add(saved.ProfileId);
        candidates.AddRange(Provider.Find(settings.ProviderId).Candidates);
        string? best = null; int bestScore = baseScore;
        foreach (var candidate in candidates.Distinct())
        {
            token.ThrowIfCancellationRequested(); var profile = Profile.Find(candidate);
            Say($"{profile.Name} deneniyor… Windows izin isteyebilir.");
            try { await engine.StartAsync(profile.Id, settings.Hosts); }
            catch (EngineCommandException ex) { AddLog(ex.Message); continue; }
            token.ThrowIfCancellationRequested();
            var results = await ProbeAll(token);
            if (network != LocalNetwork.Key(settings.ProviderId)) { await engine.StopAsync(); throw new IOException("Test sırasında ağ değişti. Yeni bağlantıda tekrar dene."); }
            int score = results.Count(x => x.Success);
            if (score == results.Length)
            {
                settings.ProfileId = profile.Id; settings.Save(); ProfilePicker.SelectedItem = profile;
                Remember(results, network); Say($"Uygun yöntem bulundu: {profile.Name}. {score}/{results.Length} siteye HTTPS erişimi doğrulandı."); return;
            }
            if (score > bestScore) { bestScore = score; best = profile.Id; }
            await engine.StopAsync();
        }
        if (best is not null)
        {
            token.ThrowIfCancellationRequested(); await engine.StartAsync(best, settings.Hosts);
            settings.ProfileId = best; settings.Save(); ProfilePicker.SelectedItem = Profile.Find(best);
            Say($"Kısmi erişim: {bestScore}/{baseline.Length} site yanıt verdi. Yöntem açık; tüm siteler doğrulanmadı.");
        }
        else { await engine.StopAsync(); Say("Bu bağlantıda doğrulanmış bir yöntem bulunamadı. Motor kapatıldı. DNS ve bağlantı sonuçlarını Tanılama’da incele.", true); }
    }
    private async void Test_Click(object sender, RoutedEventArgs e) => await RunUi(async token => { Say("HTTPS erişimi kontrol ediliyor…"); await ProbeAndRemember(token); }, true);
    private void Cancel_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; Notice.Text = "İşlem durduruluyor…"; }
    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || busy || ProviderPicker.SelectedItem is not Provider provider) return;
        settings.ProviderId = provider.Id; ProviderDetail.Text = provider.Category; directAccess = false;
        TrySave(); RefreshMemory(); UpdateState();
    }
    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilePicker.SelectedItem is not Profile profile) return;
        if (ProfileDetail is not null) ProfileDetail.Text = profile.Detail;
        if (!initialized || busy) return;
        settings.ProfileId = profile.Id; TrySave();
    }
    private void Automatic_Changed(object sender, RoutedEventArgs e) { if (!initialized) return; settings.Automatic = AutomaticCheck.IsChecked == true; TrySave(); UpdateState(); }
    private void TrySave() { try { settings.Save(); } catch (Exception ex) { Say("Ayar kaydedilemedi: " + ex.Message, true); } }
    private void SetServiceChips()
    {
        DiscordChip.IsChecked = settings.Hosts.Contains("discord.com"); RobloxChip.IsChecked = settings.Hosts.Contains("roblox.com"); YoutubeChip.IsChecked = settings.Hosts.Contains("youtube.com");
    }
    private void Service_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || busy || sender is not CheckBox chip) return;
        string key = (string)chip.Tag; var hosts = settings.Hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var host in ServiceHosts[key]) { if (chip.IsChecked == true) hosts.Add(host); else hosts.Remove(host); }
        if (hosts.Count == 0) { initialized = false; chip.IsChecked = true; initialized = true; Say("En az bir site seçili olmalı."); return; }
        settings.Hosts = hosts.ToArray(); HostsEditor.Text = string.Join(Environment.NewLine, settings.Hosts); directAccess = false; TrySave(); UpdateState();
    }
    private void SaveHosts_Click(object sender, RoutedEventArgs e)
    {
        try { settings.Hosts = Domains.Parse(HostsEditor.Text); settings.Save(); HostsEditor.Text = string.Join(Environment.NewLine, settings.Hosts); initialized = false; SetServiceChips(); initialized = true; directAccess = false; Say($"{settings.Hosts.Length} alan adı kaydedildi."); }
        catch (Exception ex) { Say(ex.Message, true); }
    }
    private void Defaults_Click(object sender, RoutedEventArgs e) { HostsEditor.Text = string.Join(Environment.NewLine, Domains.Defaults); Say("Varsayılan liste getirildi. Uygulamak için Siteleri kaydet’e bas."); }
    private void Navigate(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);
    internal void ShowPage(string page)
    {
        HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        TargetsPage.Visibility = page == "targets" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in new[] { HomeNav, TargetsNav, DiagnosticsNav, SettingsNav })
        {
            bool selected = (string)nav.Tag == page;
            nav.Background = selected ? new SolidColorBrush(Color.FromRgb(231, 239, 255)) : Brushes.Transparent;
            nav.Foreground = new SolidColorBrush(selected ? Color.FromRgb(37, 99, 235) : Color.FromRgb(100, 118, 145));
        }
    }
    private async Task CheckUpdates(bool manual)
    {
        if (checkingUpdate) return; checkingUpdate = true; UpdatePill.IsEnabled = false;
        try
        {
            release = await Updates.CheckAsync();
            if (release?.Newer == true)
            {
                UpdatePill.Content = "Yeni sürüm: " + release.Version + "  ↗"; UpdatePill.Background = new SolidColorBrush(Color.FromRgb(231, 239, 255));
                if (manual) Say("Yeni sürüm hazır. Üstteki sürüm düğmesiyle GitHub yayınını açabilirsin.");
            }
            else if (manual) Say(release is null ? "Henüz kararlı bir GitHub yayını yok." : "SilNet güncel. Sürüm " + Updates.CurrentVersion);
        }
        catch (Exception ex) { AppLog.Write("Update", ex.Message); if (manual) Say("Güncellemeler kontrol edilemedi. İnternet bağlantını kontrol edip tekrar dene."); }
        finally { checkingUpdate = false; UpdatePill.IsEnabled = true; }
    }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (release?.Newer == true) { Updates.OpenRelease(release); return; } await CheckUpdates(true);
    }
    private void UpdatePreference_Changed(object sender, RoutedEventArgs e) { if (!initialized) return; settings.AutoUpdates = AutoUpdatesCheck.IsChecked == true; TrySave(); }
    private void Github_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com/" + Updates.Repository) { UseShellExecute = true });
    private void OpenLogs_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(AppLog.DirectoryPath); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { AppLog.DirectoryPath }, UseShellExecute = true }); }
    private void CopyLog_Click(object sender, RoutedEventArgs e) { try { Clipboard.SetText(LogBox.Text); Say("Günlük panoya kopyalandı."); } catch { Say("Pano kullanılamıyor. Tekrar dene.", true); } }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return; e.Cancel = true; if (closing) return;
        closing = true; cancellation?.Cancel(); IsEnabled = false; await Task.Yield();
        while (busy) await Task.Delay(100);
        await engine.DisposeAsync(); allowClose = true; Close(); Application.Current.Shutdown();
    }
}
