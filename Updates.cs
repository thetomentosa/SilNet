using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.IO;
using System.Text.Json;

namespace SilNet;
public sealed record ReleaseInfo(string Version, Uri Page, bool Newer);
public static class Updates
{
    public const string Repository = "thetomentosa/SilNet";
    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";
    public static Version ParseVersion(string value)
    {
        string numeric = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(numeric, out var version) && version.Major >= 0 ? new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build)) : throw new FormatException("Sürüm bilgisi geçersiz.");
    }
    public static bool IsNewer(string remote, string local) => ParseVersion(remote) > ParseVersion(local);
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SilNet/" + CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string version = root.GetProperty("tag_name").GetString() ?? "";
        var page = new Uri(root.GetProperty("html_url").GetString() ?? "");
        if (!IsTrustedReleasePage(page)) throw new InvalidDataException("Güncelleme adresi doğrulanamadı.");
        return new(version, page, IsNewer(version, CurrentVersion));
    }
    public static bool IsTrustedReleasePage(Uri page) => page.Scheme == "https" && page.Host == "github.com" && page.IsDefaultPort && string.IsNullOrEmpty(page.UserInfo) && page.AbsolutePath.StartsWith("/" + Repository + "/releases/", StringComparison.Ordinal);
    public static void OpenRelease(ReleaseInfo? info = null)
    {
        var page = info?.Page ?? new Uri($"https://github.com/{Repository}/releases/latest");
        if (!IsTrustedReleasePage(page)) throw new InvalidDataException("Güncelleme adresi doğrulanamadı.");
        Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }
}
