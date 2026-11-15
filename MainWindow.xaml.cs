using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SilNet;
public partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly EngineClient engine = new();
    private readonly bool preview;
    private CancellationTokenSource? cancellation;
    private ReleaseInfo? release;
    private bool busy, closing, allowClose, initialized, directAccess, checkingUpdate;
    private bool exitRequested, trayTipShown;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? trayConnect;
    private static readonly Dictionary<string, string[]> ServiceHosts = new()
    {
        ["discord"] = ["discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media"],
        ["roblox"] = ["roblox.com", "rbxcdn.com"],
        ["wattpad"] = ["wattpad.com", "wattpad.net"]
    };
    public MainWindow(bool previewMode = false)
    {
        preview = previewMode; InitializeComponent();
        if (!preview) { MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height - 40); Height = Math.Min(Height, SystemParameters.WorkArea.Height - 30); MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width - 40); Width = Math.Min(Width, SystemParameters.WorkArea.Width - 30); }
        ProviderPicker.ItemsSource = Provider.All; ProviderPicker.SelectedItem = Provider.Find(settings.ProviderId);
        ProfilePicker.ItemsSource = Profile.All; ProfilePicker.SelectedItem = Profile.Find(settings.ProfileId);
        ProfileDetail.Text = Profile.Find(settings.ProfileId).Detail;
        ProviderDetail.Text = Provider.Find(settings.ProviderId).Category;
        AutomaticCheck.IsChecked = settings.Automatic; AutoUpdatesCheck.IsChecked = settings.AutoUpdates;
        SecureDnsCheck.IsChecked = settings.SecureDns; QuicCheck.IsChecked = settings.UseQuic;
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
        if (!preview)
        {
            SetupTray();
            var powerScale = new ScaleTransform(1, 1); ConnectButton.RenderTransform = powerScale; ConnectButton.RenderTransformOrigin = new Point(0.5, 0.5);
            void ScalePower(double value)
            {
                if (!SystemParameters.ClientAreaAnimation) return;
                powerScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(130)));
                powerScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(130)));
            }
            ConnectButton.MouseEnter += (_, _) => ScalePower(1.035); ConnectButton.MouseLeave += (_, _) => ScalePower(1);
            if (SystemParameters.ClientAreaAnimation)
            {
                Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                var slide = new TranslateTransform(0, 9); Shell.RenderTransform = slide;
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase() });
            }
            if (!settings.OnboardingComplete) RunSetup();
        }
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
        if (error) { FeedbackBanner.Visibility = Visibility.Visible; FeedbackText.Text = display; }
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
        ConnectionHint.Text = active ? Profile.Find(settings.ProfileId).Name + " · Tepside çalışmaya devam eder." : "Başlatırken Windows izin ister.";
        MotorStatus.Text = active ? "Çalışıyor · " + Profile.Find(settings.ProfileId).Name : "Kapalı";
        DnsStatus.Text = active && settings.SecureDns ? "Seçili sitelerde şifreli DNS" : "Kalıcı DNS ayarın korunur";
        ConnectButton.IsEnabled = VerifyButton.IsEnabled = DiagTestButton.IsEnabled = !busy;
        ProviderPicker.IsEnabled = AutomaticCheck.IsEnabled = SaveHostsButton.IsEnabled = DefaultHostsButton.IsEnabled = HostsEditor.IsEnabled = !busy && !active;
        ProfilePicker.IsEnabled = !busy && !active && !settings.Automatic;
        SecureDnsCheck.IsEnabled = QuicCheck.IsEnabled = !busy && !active;
        DiscordChip.IsEnabled = RobloxChip.IsEnabled = WattpadChip.IsEnabled = !busy && !active;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        NetworkLabel.Text = LocalNetwork.Label;
        if (trayConnect is not null) { trayConnect.Enabled = !busy; trayConnect.Text = active ? "Bağlantıyı durdur" : "Bağlan"; }
        if (!preview && SystemParameters.ClientAreaAnimation)
        {
            PowerHalo.BeginAnimation(OpacityProperty, busy || active ? new DoubleAnimation(0.3, 0.85, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        }
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
        FeedbackBanner.Visibility = Visibility.Collapsed;
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
            await engine.StartAsync(settings.ProfileId, settings.Hosts, settings.SecureDns, settings.UseQuic); token.ThrowIfCancellationRequested(); UpdateState();
            Say("Yöntem etkin. Site erişimi kontrol ediliyor…");
            await ProbeAndRemember(token);
        }
    }, true);
    private string[] TestHosts()
    {
        var selected = new List<string>();
        foreach (var root in new[] { "discord.com", "roblox.com", "wattpad.com" }) if (settings.Hosts.Contains(root)) selected.Add(root);
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
        if (baseline.Any(x => x.Detail.StartsWith("DNS engelleme")) && !settings.SecureDns) { Say("DNS engeli algılandı. Ayarlar’dan ‘Seçili sitelerde şifreli DNS’ seçeneğini açıp tekrar Bağlan’a bas.", true); return; }
        var candidates = new List<string>();
        if (settings.Networks.TryGetValue(network, out var saved) && Profile.All.Any(p => p.Id == saved.ProfileId)) candidates.Add(saved.ProfileId);
        candidates.AddRange(Provider.Find(settings.ProviderId).Candidates);
        string? best = null; int bestScore = baseScore;
        foreach (var candidate in candidates.Distinct())
        {
            token.ThrowIfCancellationRequested(); var profile = Profile.Find(candidate);
            Say($"{profile.Name} deneniyor… Windows izin isteyebilir.");
            try { await engine.StartAsync(profile.Id, settings.Hosts, settings.SecureDns, settings.UseQuic); }
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
            token.ThrowIfCancellationRequested(); await engine.StartAsync(best, settings.Hosts, settings.SecureDns, settings.UseQuic);
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
        DiscordChip.IsChecked = settings.Hosts.Contains("discord.com"); RobloxChip.IsChecked = settings.Hosts.Contains("roblox.com"); WattpadChip.IsChecked = settings.Hosts.Contains("wattpad.com");
    }
    private void Service_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || busy || sender is not CheckBox chip) return;
        string key = (string)chip.Tag; var hosts = settings.Hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var host in ServiceHosts[key]) { if (chip.IsChecked == true) hosts.Add(host); else hosts.Remove(host); }
        if (hosts.Count == 0) { initialized = false; chip.IsChecked = true; initialized = true; Say("En az bir site seçili olmalı."); return; }
        settings.Hosts = hosts.ToArray(); HostsEditor.Text = string.Join(Environment.NewLine, settings.Hosts); directAccess = false; TrySave(); UpdateState();
    }
    private void MoreApps_Click(object sender, RoutedEventArgs e)
    {
        if (busy || engine.Running) { Say("Uygulama seçimini değiştirmek için önce bağlantıyı durdur."); return; }
        var apps = new AppsWindow(settings.Hosts) { Owner = this };
        if (apps.ShowDialog() != true) return;
        settings.Hosts = apps.Hosts; TrySave(); initialized = false; SetServiceChips(); initialized = true;
        HostsEditor.Text = string.Join(Environment.NewLine, settings.Hosts); directAccess = false;
        Say($"{TargetService.All.Count(s => s.Hosts.Any(settings.Hosts.Contains))} uygulama seçili. Değişiklikler sonraki bağlantıda uygulanır.");
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
        if (!preview && SystemParameters.ClientAreaAnimation)
        {
            var selectedPage = page switch { "targets" => TargetsPage, "diagnostics" => DiagnosticsPage, "settings" => SettingsPage, _ => HomePage };
            selectedPage.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        }
    }
    private async Task CheckUpdates(bool manual)
    {
        if (checkingUpdate) return; checkingUpdate = true; UpdatePill.IsEnabled = false;
        UpdatePill.Content = "Kontrol ediliyor…";
        if (manual) { FeedbackBanner.Visibility = Visibility.Visible; FeedbackText.Text = "GitHub’daki SilNet sürümleri kontrol ediliyor…"; }
        try
        {
            release = await Updates.CheckAsync();
            if (release?.Newer == true)
            {
                UpdatePill.Content = "Yeni sürüm: " + release.Version + "  ↗"; UpdatePill.Background = new SolidColorBrush(Color.FromRgb(231, 239, 255));
                if (manual) { FeedbackText.Text = "Yeni sürüm hazır: " + release.Version + ". Resmî indirme sayfası açılıyor."; Updates.OpenRelease(release); }
            }
            else
            {
                UpdatePill.Content = release is null ? "Güncellemeler  ↗" : "Güncel · " + Updates.CurrentVersion + "  ✓";
                if (manual) { string text = release is null ? "GitHub deposu hazır; ilk kurulum yayını hazırlanıyor. Uygulamanın bağlantı işlevi bundan bağımsızdır." : "En yeni SilNet sürümünü kullanıyorsun: " + Updates.CurrentVersion; FeedbackText.Text = text; Say(text); }
            }
        }
        catch (Exception ex) { AppLog.Write("Update", ex.Message); UpdatePill.Content = "Tekrar kontrol et  ↻"; if (manual) { FeedbackText.Text = "GitHub’a ulaşılamadı. Bağlantını kontrol edip tekrar dene."; Say(FeedbackText.Text); } }
        finally { checkingUpdate = false; UpdatePill.IsEnabled = true; }
    }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (release?.Newer == true) { Updates.OpenRelease(release); return; } await CheckUpdates(true);
    }
    private void UpdatePreference_Changed(object sender, RoutedEventArgs e) { if (!initialized) return; settings.AutoUpdates = AutoUpdatesCheck.IsChecked == true; TrySave(); }
    private void SecureDns_Changed(object sender, RoutedEventArgs e) { if (!initialized) return; settings.SecureDns = SecureDnsCheck.IsChecked == true; TrySave(); }
    private void Quic_Changed(object sender, RoutedEventArgs e) { if (!initialized) return; settings.UseQuic = QuicCheck.IsChecked == true; TrySave(); }
    private void RunSetup()
    {
        var wizard = new OnboardingWindow(settings) { Owner = this };
        if (wizard.ShowDialog() == true)
        {
            initialized = false; ProviderPicker.SelectedItem = Provider.Find(settings.ProviderId); ProviderDetail.Text = Provider.Find(settings.ProviderId).Category;
            HostsEditor.Text = string.Join(Environment.NewLine, settings.Hosts); SetServiceChips(); SecureDnsCheck.IsChecked = settings.SecureDns; QuicCheck.IsChecked = settings.UseQuic;
            initialized = true; UpdateState();
            if (wizard.ConnectAfter) Connect_Click(this, new RoutedEventArgs());
        }
    }
    private void Setup_Click(object sender, RoutedEventArgs e) { if (busy || engine.Running) { Say("Önce bağlantıyı durdur."); return; } RunSetup(); }
    private void SetupTray()
    {
        if (tray is not null) return;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("SilNet’i aç", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        trayConnect = new Forms.ToolStripMenuItem("Bağlan", null, (_, _) => Dispatcher.Invoke(() => Connect_Click(this, new RoutedEventArgs()))); menu.Items.Add(trayConnect);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => Dispatcher.Invoke(RequestExit));
        tray = new Forms.NotifyIcon { Text = "SilNet · Bağlantı yardımcısı", Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideToTray(); };
        Application.Current.SessionEnding += (_, _) => { exitRequested = true; };
    }
    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void HideToTray()
    {
        Hide();
        if (tray is not null && !trayTipShown) { trayTipShown = true; tray.ShowBalloonTip(2500, "SilNet arka planda", "Açmak için SilNet simgesine çift tıkla. Tamamen kapatmak için sağ tık → Çıkış.", Forms.ToolTipIcon.Info); }
    }
    internal void RequestExit() { exitRequested = true; Close(); }
    private void Exit_Click(object sender, RoutedEventArgs e) => RequestExit();
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        string uninstall = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!File.Exists(uninstall)) { MessageBox.Show("Bu taşınabilir sürüm. Çıkış yaptıktan sonra uygulama klasörünü silebilirsin. Kişisel ayar ve günlükleri silmek için ‘Ayarları temizle ve çık’ düğmesini kullan.", "SilNet"); return; }
        if (MessageBox.Show("SilNet durdurulup kaldırma sihirbazı açılacak. Kişisel ayarları silme seçeneği kaldırma sonunda sorulur. Devam edilsin mi?", "SilNet’i kaldır", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        App.PendingUninstaller = uninstall; RequestExit();
    }
    private void ResetExit_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("SilNet’in bu kullanıcıya ait ayarları, bağlantı hafızası ve günlükleri silinecek; program kapanacak. Devam edilsin mi?", "SilNet verilerini temizle", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        App.CleanupUserData = true; RequestExit();
    }
    private void Github_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com/" + Updates.Repository) { UseShellExecute = true });
    private void OpenLogs_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(AppLog.DirectoryPath); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { AppLog.DirectoryPath }, UseShellExecute = true }); }
    private void CopyLog_Click(object sender, RoutedEventArgs e) { try { Clipboard.SetText(LogBox.Text); Say("Günlük panoya kopyalandı."); } catch { Say("Pano kullanılamıyor. Tekrar dene.", true); } }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested && !preview) { e.Cancel = true; HideToTray(); return; }
        if (allowClose) return; e.Cancel = true; if (closing) return;
        closing = true; cancellation?.Cancel(); IsEnabled = false; await Task.Yield();
        while (busy) await Task.Delay(100);
        await engine.DisposeAsync(); if (tray is not null) { tray.Visible = false; tray.Dispose(); tray = null; } allowClose = true; Close(); Application.Current.Shutdown();
    }
}
