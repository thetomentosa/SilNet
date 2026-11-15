using System.Windows;
namespace SilNet;
public partial class OnboardingWindow : Window
{
    private readonly Settings settings;
    private int step;
    private string[] workingHosts;
    public bool ConnectAfter { get; private set; }
    public OnboardingWindow(Settings current)
    {
        settings = current; workingHosts = current.Hosts.ToArray(); InitializeComponent(); ProviderChoice.ItemsSource = Provider.All;
        MinHeight = Math.Min(560, SystemParameters.WorkArea.Height - 40); Height = Math.Min(700, SystemParameters.WorkArea.Height - 30);
        ProviderChoice.SelectedItem = Provider.Find(settings.ProviderId);
        DiscordChoice.IsChecked = settings.Hosts.Contains("discord.com"); RobloxChoice.IsChecked = settings.Hosts.Contains("roblox.com"); WattpadChoice.IsChecked = settings.Hosts.Contains("wattpad.com"); DnsChoice.IsChecked = settings.SecureDns;
    }
    private string[] Choices()
    {
        var known = new[] { "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media", "roblox.com", "rbxcdn.com", "wattpad.com", "wattpad.net" };
        var hosts = workingHosts.Where(x => !known.Contains(x)).ToList();
        if (DiscordChoice.IsChecked == true) hosts.AddRange(known.Take(5));
        if (RobloxChoice.IsChecked == true) hosts.AddRange(known.Skip(5).Take(2));
        if (WattpadChoice.IsChecked == true) hosts.AddRange(known.Skip(7));
        return hosts.Distinct().ToArray();
    }
    private void ShowStep()
    {
        WelcomePage.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed; ChoicesPage.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed; ReadyPage.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed; StepLabel.Text = $"İLK KURULUM · ADIM {step + 1} / 3"; StepProgress.Value = step + 1;
        NextButton.Content = step == 2 ? "SilNet’i aç  →" : "Devam et  →";
        if (step == 2) Summary.Text = $"Sağlayıcı: {ProviderChoice.SelectedItem}\nHedef: {Choices().Length} alan adı\nŞifreli DNS: {(DnsChoice.IsChecked == true ? "Açık" : "Kapalı")}\nPencere kapanınca: Tepside çalışır";
    }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (step == 1 && Choices().Length == 0) { ErrorText.Text = "En az bir uygulama seç veya mevcut alan adlarını koru."; return; }
        ErrorText.Text = "";
        if (step < 2) { step++; ShowStep(); return; }
        try { settings.ProviderId = ((Provider)ProviderChoice.SelectedItem).Id; settings.Hosts = Choices(); settings.SecureDns = DnsChoice.IsChecked == true; settings.OnboardingComplete = true; settings.Save(); ConnectAfter = ConnectChoice.IsChecked == true; DialogResult = true; }
        catch (Exception ex) { ErrorText.Text = "Ayar kaydedilemedi: " + ex.Message; }
    }
    private void Back_Click(object sender, RoutedEventArgs e) { if (step > 0) step--; ShowStep(); }
    internal void ShowPreviewStep(int value) { step = Math.Clamp(value, 0, 2); ShowStep(); }
    private void AllApps_Click(object sender, RoutedEventArgs e)
    {
        var apps = new AppsWindow(Choices()) { Owner = this };
        if (apps.ShowDialog() != true) return;
        workingHosts = apps.Hosts;
        DiscordChoice.IsChecked = workingHosts.Contains("discord.com"); RobloxChoice.IsChecked = workingHosts.Contains("roblox.com"); WattpadChoice.IsChecked = workingHosts.Contains("wattpad.com");
    }
    private void Skip_Click(object sender, RoutedEventArgs e) { try { settings.OnboardingComplete = true; settings.Save(); DialogResult = true; } catch (Exception ex) { ErrorText.Text = ex.Message; } }
}
