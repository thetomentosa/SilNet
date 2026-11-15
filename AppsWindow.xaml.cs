using System.Windows;
namespace SilNet;
public partial class AppsWindow : Window
{
    private readonly string[] existing;
    private readonly ServiceChoice[] choices;
    public string[] Hosts { get; private set; }
    public AppsWindow(string[] hosts)
    {
        existing = hosts; Hosts = hosts; InitializeComponent();
        choices = TargetService.All.Select(s => new ServiceChoice(s, s.Hosts.Any(hosts.Contains))).ToArray(); ChoicesList.ItemsSource = choices;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var hosts = TargetService.Merge(existing, choices.Where(x => x.Selected).Select(x => x.Service));
        if (hosts.Length == 0) { ErrorText.Text = "En az bir uygulama veya özel alan adı seçili olmalı."; return; }
        Hosts = hosts; DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Service_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { DataContext: ServiceChoice choice } && choice.Service.Id == "pornhub")
        { EasterEgg.Visibility = Visibility.Visible; EasterEggText.Text = "🧪 Araştırma modu açıldı. Peçeteni hazırla; bu araştırma göz yaşartabilir. 😉😘"; }
    }
    private void Service_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { DataContext: ServiceChoice choice } && choice.Service.Id == "pornhub") EasterEgg.Visibility = Visibility.Collapsed;
    }
}
