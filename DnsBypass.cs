using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SilNet;

// Temporary, selected-domain UDP DNS interception. No adapter, registry, hosts-file
// or firewall changes. Unknown/malformed/non-target DNS packets pass unchanged.
public sealed class DnsBypass : IDisposable
{
    private readonly IntPtr handle;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim requests = new(16);
    private readonly object sendLock = new();
    private readonly string[] domains;
    private readonly HttpClient google = Client("8.8.8.8");
    private readonly HttpClient cloudflare = Client("1.1.1.1");
    private readonly ConcurrentDictionary<string, (DateTimeOffset Until, Lazy<Task<byte[]>> Response)> cache = new();
    private readonly Task receiver;
    public DnsBypass(string[] hosts)
    {
        domains = hosts;
        handle = Native.Open("outbound and !loopback and udp.DstPort == 53", 0, -500, 0);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            throw new IOException("Şifreli DNS katmanı açılamadı. Windows hata kodu: " + Marshal.GetLastWin32Error());
        receiver = Task.Run(Receive);
        Native.Flush();
        AppLog.Write("DNS", "Seçili alan adları için geçici DoH başladı. Windows DNS sunucuları değiştirilmedi.");
    }
    private static HttpClient Client(string address)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(3), PooledConnectionLifetime = TimeSpan.FromMinutes(3),
            ConnectCallback = async (context, token) =>
            {
                // Bootstrap by provider IP; request hostname still drives SNI and normal TLS validation.
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(address), context.DnsEndPoint.Port), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            }
        };
        return new(handler) { Timeout = TimeSpan.FromSeconds(4), DefaultRequestVersion = HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
    }
    private void Receive()
    {
        byte[] packet = new byte[65535], address = new byte[80];
        while (!stop.IsCancellationRequested && Native.Receive(handle, packet, (uint)packet.Length, out uint length, address))
        {
            if (!TryQuestion(packet.AsSpan(0, (int)length), out var question) || !Matches(question.Host, domains))
            { Send(packet, (int)length, address); continue; }
            var copy = packet.AsSpan(0, (int)length).ToArray(); var addr = address.ToArray();
            if (!requests.Wait(0)) { Respond(copy, addr, question, Failure(copy.AsSpan(question.DnsOffset, question.DnsLength).ToArray(), question.QuestionLength)); continue; }
            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] query = copy.AsSpan(question.DnsOffset, question.DnsLength).ToArray();
                    byte[] normalized = query.ToArray(); normalized[0] = normalized[1] = 0;
                    string key = Convert.ToHexString(SHA256.HashData(normalized));
                    if (cache.Count > 256) cache.Clear();
                    if (cache.TryGetValue(key, out var existing) && existing.Until < DateTimeOffset.UtcNow) cache.TryRemove(key, out _);
                    var entry = cache.GetOrAdd(key, _ => (DateTimeOffset.UtcNow.AddSeconds(30), new Lazy<Task<byte[]>>(() => Resolve(normalized, question))));
                    var response = (await entry.Response.Value).ToArray(); response[0] = query[0]; response[1] = query[1];
                    Respond(copy, addr, question, response);
                }
                catch (Exception ex)
                {
                    if (!stop.IsCancellationRequested)
                    {
                        AppLog.Write("DNS", "DoH başarısız: " + question.Host + ": " + ex.Message);
                        Respond(copy, addr, question, Failure(copy.AsSpan(question.DnsOffset, question.DnsLength).ToArray(), question.QuestionLength));
                    }
                }
                finally { requests.Release(); }
            });
        }
    }
    private async Task<byte[]> Resolve(byte[] query, Question question)
    {
        foreach (var endpoint in new[] { (google, "https://dns.google/dns-query"), (cloudflare, "https://cloudflare-dns.com/dns-query") })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Item2) { Content = new ByteArrayContent(query) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
                using var reply = await endpoint.Item1.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
                reply.EnsureSuccessStatusCode();
                if (reply.Content.Headers.ContentType?.MediaType != "application/dns-message" || reply.Content.Headers.ContentLength > 4096) throw new IOException("Geçersiz DoH yanıtı.");
                using var stream = await reply.Content.ReadAsStreamAsync(stop.Token);
                using var memory = new MemoryStream(); var chunk = new byte[4097];
                int count;
                while ((count = await stream.ReadAsync(chunk, stop.Token)) > 0) { await memory.WriteAsync(chunk.AsMemory(0, count), stop.Token); if (memory.Length > 4096) throw new IOException("DoH yanıtı çok büyük."); }
                byte[] response = memory.ToArray();
                if (response.Length < 12 + question.QuestionLength || (response[2] & 0x80) == 0 || Read16(response, 4) != 1 || !response.AsSpan(12, question.QuestionLength).SequenceEqual(query.AsSpan(12, question.QuestionLength))) throw new IOException("DoH soru/yanıt eşleşmedi.");
                AppLog.Write("DNS", "DoH çözümlendi: " + question.Host);
                return response;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
            catch (Exception ex) { AppLog.Write("DoH", ex.Message); }
        }
        throw new IOException("Google ve Cloudflare şifreli DNS sunucularına ulaşılamadı.");
    }
    private void Respond(byte[] query, byte[] address, Question question, byte[] dns)
    {
        if (stop.IsCancellationRequested) return;
        byte[] response = BuildResponse(query, question, dns);
        address[10] = (byte)(address[10] & ~2); // WINDIVERT_ADDRESS.Outbound = false
        Send(response, response.Length, address);
    }
    private void Send(byte[] packet, int length, byte[] address)
    {
        lock (sendLock)
        {
            if (stop.IsCancellationRequested) return;
            Native.Checksums(packet, (uint)length, address, 0);
            if (!Native.Send(handle, packet, (uint)length, out _, address)) AppLog.Write("DNS", "Paket gönderilemedi: " + Marshal.GetLastWin32Error());
        }
    }
    internal sealed record Question(string Host, int UdpOffset, int DnsOffset, int DnsLength, int QuestionLength, bool IPv6);
    internal static bool Matches(string host, string[] targets) => targets.Any(target => host.Equals(target, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + target, StringComparison.OrdinalIgnoreCase));
    internal static bool TryQuestion(ReadOnlySpan<byte> packet, out Question question)
    {
        question = null!; if (packet.Length < 28) return false;
        bool ipv6 = (packet[0] >> 4) == 6; int udp;
        if ((packet[0] >> 4) == 4)
        {
            udp = (packet[0] & 15) * 4;
            if (udp < 20 || packet[9] != 17 || (Read16(packet, 6) & 0x3FFF) != 0) return false;
        }
        else if (ipv6) { if (packet.Length < 48 || packet[6] != 17) return false; udp = 40; }
        else return false;
        if (packet.Length < udp + 20 || Read16(packet, udp + 2) != 53) return false;
        int udpLength = Read16(packet, udp + 4); if (udpLength < 20 || udp + udpLength > packet.Length) return false;
        int dns = udp + 8, dnsLength = udpLength - 8;
        var payload = packet.Slice(dns, dnsLength);
        if ((payload[2] & 0xF8) != 0 || Read16(payload, 4) != 1) return false;
        int cursor = 12; var labels = new List<string>();
        while (cursor < payload.Length)
        {
            int size = payload[cursor++]; if (size == 0) break;
            if (size > 63 || cursor + size > payload.Length) return false;
            var label = payload.Slice(cursor, size);
            foreach (byte c in label) if (c > 127 || c < 33) return false;
            labels.Add(Encoding.ASCII.GetString(label)); cursor += size;
            if (labels.Count > 127) return false;
        }
        if (labels.Count == 0 || cursor + 4 > payload.Length || payload[cursor - 1] != 0 || Read16(payload, cursor + 2) != 1) return false;
        string host = string.Join('.', labels).ToLowerInvariant(); if (host.Length > 253) return false;
        question = new(host, udp, dns, dnsLength, cursor + 4 - 12, ipv6); return true;
    }
    internal static byte[] BuildResponse(byte[] query, Question question, byte[] dns)
    {
        var response = new byte[question.DnsOffset + dns.Length]; query.AsSpan(0, question.DnsOffset).CopyTo(response); dns.CopyTo(response, question.DnsOffset);
        int source = question.IPv6 ? 8 : 12, destination = question.IPv6 ? 24 : 16, size = question.IPv6 ? 16 : 4;
        query.AsSpan(destination, size).CopyTo(response.AsSpan(source, size)); query.AsSpan(source, size).CopyTo(response.AsSpan(destination, size));
        if (question.IPv6) { Write16(response, 4, 8 + dns.Length); response[7] = 64; }
        else { Write16(response, 2, response.Length); response[8] = 64; response[10] = response[11] = 0; }
        query.AsSpan(question.UdpOffset + 2, 2).CopyTo(response.AsSpan(question.UdpOffset, 2)); query.AsSpan(question.UdpOffset, 2).CopyTo(response.AsSpan(question.UdpOffset + 2, 2));
        Write16(response, question.UdpOffset + 4, 8 + dns.Length); response[question.UdpOffset + 6] = response[question.UdpOffset + 7] = 0;
        return response;
    }
    private static byte[] Failure(byte[] query, int questionLength)
    {
        var answer = query.AsSpan(0, 12 + questionLength).ToArray(); answer[2] = 0x81; answer[3] = 0x82;
        Array.Clear(answer, 6, 6); return answer;
    }
    private static int Read16(ReadOnlySpan<byte> buffer, int offset) => BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
    private static void Write16(Span<byte> buffer, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset, 2), (ushort)value);
    public void Dispose()
    {
        if (stop.IsCancellationRequested) return;
        stop.Cancel(); Native.Shutdown(handle, 1);
        try { receiver.Wait(1000); } catch { }
        lock (sendLock) Native.Close(handle);
        google.Dispose(); cloudflare.Dispose(); Native.Flush();
        AppLog.Write("DNS", "Geçici DoH durduruldu. Windows DNS sunucuları değiştirilmedi.");
    }
    private static class Native
    {
        static Native() => NativeLibrary.SetDllImportResolver(typeof(DnsBypass).Assembly, (name, assembly, path) => name == "SilNetWinDivert" ? NativeLibrary.Load(Path.Combine(EngineIntegrity.EngineDirectory, "WinDivert.dll")) : IntPtr.Zero);
        [DllImport("SilNetWinDivert", EntryPoint = "WinDivertOpen", CharSet = CharSet.Ansi, SetLastError = true)] internal static extern IntPtr Open(string filter, int layer, short priority, ulong flags);
        [DllImport("SilNetWinDivert", EntryPoint = "WinDivertRecv", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Receive(IntPtr handle, [Out] byte[] packet, uint length, out uint read, [Out] byte[] address);
        [DllImport("SilNetWinDivert", EntryPoint = "WinDivertSend", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Send(IntPtr handle, byte[] packet, uint length, out uint sent, byte[] address);
        [DllImport("SilNetWinDivert", EntryPoint = "WinDivertHelperCalcChecksums", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Checksums([In, Out] byte[] packet, uint length, [In, Out] byte[] address, ulong flags);
        [DllImport("SilNetWinDivert", EntryPoint = "WinDivertShutdown", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Shutdown(IntPtr handle, int how);
        [DllImport("SilNetWinDivert", EntryPoint = "WinDivertClose", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Close(IntPtr handle);
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Flush();
    }
}
