using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace SilNet;
public sealed record WireMessage(string Kind, string Text = "", string[]? Hosts = null, string Id = "");
public sealed class EngineCommandException(string message) : IOException(message);

public sealed class EngineClient(bool elevate = true) : IAsyncDisposable
{
    private sealed class Connection(NamedPipeServerStream pipe, Process process)
    {
        public NamedPipeServerStream Pipe = pipe;
        public Process Process = process;
        public StreamWriter? Writer;
        public TaskCompletionSource<WireMessage> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WireMessage>? Pending;
        public string RequestId = "";
        public bool Closed;
    }
    private Connection? connection;
    private readonly SemaphoreSlim operations = new(1);
    public event Action<string>? Log;
    public event Action? Disconnected;
    public bool Running { get; private set; }
    private async Task<Connection> ConnectAsync()
    {
        if (connection is { Closed: false } existing && existing.Pipe.IsConnected && !existing.Process.HasExited) return existing;
        CloseConnection();
        var name = "SilNet-" + Guid.NewGuid().ToString("N");
        // Explicit USER SID ACL, rather than CurrentUserOnly's elevation-dependent OWNER check.
        // Both ends additionally verify that they are connected to the expected process ID.
        var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException("Windows kullanıcı kimliği okunamadı.");
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(sid);
        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = elevate, WorkingDirectory = AppContext.BaseDirectory, WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = !elevate };
        if (elevate) info.Verb = "runas";
        info.ArgumentList.Add("--worker"); info.ArgumentList.Add(name); info.ArgumentList.Add(Environment.ProcessId.ToString());
        Connection? current = null;
        try
        {
            var process = Process.Start(info) ?? throw new IOException("SilNet bağlantı yardımcısı açılamadı.");
            current = new(pipe, process); connection = current;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var wait = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = process.WaitForExitAsync(timeout.Token);
            var first = await Task.WhenAny(wait, exited);
            if (first == exited && !pipe.IsConnected) throw new IOException($"Bağlantı yardımcısı erken kapandı (kod {process.ExitCode}). Günlük: {AppLog.DirectoryPath}");
            await wait;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var peer) || peer != process.Id) throw new IOException("Bağlantı yardımcısının kimliği doğrulanamadı.");
            current.Writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            _ = ReadAsync(current);
            var hello = await current.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (hello.Kind != "ready" || hello.Text != "2") throw new IOException(hello.Text.Length > 0 ? hello.Text : "Bağlantı yardımcısı uyumsuz.");
            return current;
        }
        catch { pipe.Dispose(); if (ReferenceEquals(connection, current)) CloseConnection(); throw; }
    }
    private async Task ReadAsync(Connection current)
    {
        try
        {
            using var reader = new StreamReader(current.Pipe, Encoding.UTF8, leaveOpen: true);
            while (await reader.ReadLineAsync() is { } line)
            {
                var msg = JsonSerializer.Deserialize<WireMessage>(line);
                if (msg is null) continue;
                if (msg.Kind == "ready" || msg.Id == "hello") { current.Ready.TrySetResult(msg); continue; }
                if (msg.Kind == "log") { Log?.Invoke(msg.Text); continue; }
                if (!ReferenceEquals(connection, current)) continue;
                if (msg.Kind == "started") Running = true;
                if (msg.Kind is "stopped" or "exited" or "error") Running = false;
                if (msg.Id == current.RequestId) current.Pending?.TrySetResult(msg);
                if (msg.Kind == "exited") { Log?.Invoke("Motor durdu: " + msg.Text); Disconnected?.Invoke(); }
            }
        }
        catch (Exception ex) { AppLog.Write("IPC read", ex.ToString()); }
        finally
        {
            current.Closed = true;
            var error = new IOException("Bağlantı yardımcısı kapandı. Yeniden Bağlan’a basın. Ayrıntı: " + AppLog.DirectoryPath);
            current.Ready.TrySetException(error); current.Pending?.TrySetException(error);
            if (ReferenceEquals(connection, current)) { Running = false; Disconnected?.Invoke(); }
        }
    }
    private async Task<WireMessage> CommandAsync(WireMessage command)
    {
        await operations.WaitAsync();
        try
        {
            var current = await ConnectAsync();
            current.RequestId = Guid.NewGuid().ToString("N"); current.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await current.Writer!.WriteLineAsync(JsonSerializer.Serialize(command with { Id = current.RequestId }));
            var result = await current.Pending.Task.WaitAsync(TimeSpan.FromSeconds(30));
            current.Pending = null;
            if (result.Kind == "error") throw new EngineCommandException(result.Text);
            return result;
        }
        catch (EngineCommandException) { throw; }
        catch { Running = false; CloseConnection(); throw; }
        finally { operations.Release(); }
    }
    private void CloseConnection()
    {
        var previous = connection; connection = null; Running = false;
        if (previous is null) return;
        previous.Pipe.Dispose(); previous.Process.Dispose();
    }
    public async Task PingAsync() => await CommandAsync(new("ping"));
    internal int? WorkerId => connection?.Process.Id;
    public async Task DryRunAsync(string profile, string[] hosts) => await CommandAsync(new("dryrun", profile, hosts));
    public async Task StartAsync(string profile, string[] hosts) => await CommandAsync(new("start", profile, hosts));
    public async Task StopAsync() { if (connection?.Pipe.IsConnected == true) await CommandAsync(new("stop")); }
    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); } catch { }
        var process = connection?.Process; connection?.Pipe.Dispose();
        if (process is not null) { try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { } }
        CloseConnection();
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}

public static class EngineWorker
{
    public static async Task RunAsync(string name, int parentId)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^SilNet-[a-f0-9]{32}$")) return;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(20000);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var peer) || peer != parentId) throw new IOException("Arayüz kimliği doğrulanamadı.");
            using var parent = Process.GetProcessById(parentId);
            if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Arayüz dosyası doğrulanamadı.");
        }
        catch (Exception ex) { AppLog.Write("Worker connect", ex.ToString()); return; }
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var sendLock = new SemaphoreSlim(1);
        async Task Send(WireMessage msg)
        {
            await sendLock.WaitAsync();
            try { await writer.WriteLineAsync(JsonSerializer.Serialize(msg)); } finally { sendLock.Release(); }
        }
        Process? engine = null; KillJob? job = null; string? temporaryHosts = null;
        void Stop()
        {
            var previous = engine; engine = null; job?.Dispose(); job = null;
            if (previous is not null) { try { if (!previous.HasExited) previous.Kill(true); previous.WaitForExit(4000); } catch { } previous.Dispose(); }
            if (temporaryHosts is not null) { try { File.Delete(temporaryHosts); } catch { } temporaryHosts = null; }
        }
        try
        {
            await Send(new("ready", "2"));
            while (await reader.ReadLineAsync() is { } line)
            {
                string id = "";
                try
                {
                    if (line.Length > 60000) throw new IOException("Geçersiz istek.");
                    var command = JsonSerializer.Deserialize<WireMessage>(line) ?? throw new IOException("Geçersiz istek."); id = command.Id;
                    if (command.Kind == "ping") { await Send(new("pong", Id: id)); continue; }
                    if (command.Kind == "stop") { Stop(); await Send(new("stopped", Id: id)); continue; }
                    if (command.Kind is not ("start" or "dryrun")) throw new IOException("Bilinmeyen işlem.");
                    var profile = Profile.Find(command.Text);
                    var hosts = Domains.Parse(string.Join('\n', command.Hosts ?? [])); Stop();
                    var conflicts = Diagnostics.Conflicts();
                    if (command.Kind == "start" && conflicts.Length > 0) throw new IOException("Önce diğer DPI uygulamasını kapatın: " + string.Join(", ", conflicts));
                    await EngineIntegrity.VerifyAsync();
                    temporaryHosts = Path.Combine(Path.GetTempPath(), "SilNet-hosts-" + Guid.NewGuid().ToString("N") + ".txt");
                    await File.WriteAllLinesAsync(temporaryHosts, hosts, new UTF8Encoding(false));
                    var info = new ProcessStartInfo(Path.Combine(EngineIntegrity.EngineDirectory, "winws2.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = EngineIntegrity.EngineDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in profile.Arguments(EngineIntegrity.EngineDirectory, temporaryHosts)) info.ArgumentList.Add(arg);
                    if (command.Kind == "dryrun") info.ArgumentList.Add("--dry-run");
                    engine = Process.Start(info) ?? throw new IOException("Motor başlatılamadı.");
                    job = new KillJob(); job.Assign(engine);
                    var current = engine; var capture = new StringBuilder();
                    async Task Pump(StreamReader output)
                    {
                        try { int count = 0; while (await output.ReadLineAsync() is { } text) { if (++count <= 250) { lock (capture) capture.AppendLine(text); AppLog.Write("Motor", text); await Send(new("log", text.Length > 1000 ? text[..1000] : text)); } } } catch { }
                    }
                    var outputs = Task.WhenAll(Pump(current.StandardOutput), Pump(current.StandardError));
                    if (command.Kind == "dryrun")
                    {
                        await current.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); await outputs;
                        if (current.ExitCode != 0) throw new IOException("Profil motor tarafından reddedildi: " + capture);
                        Stop(); await Send(new("checked", profile.Id, Id: id)); continue;
                    }
                    await Task.Delay(1600);
                    if (current.HasExited) { await outputs; throw new IOException($"Motor açılamadı (kod {current.ExitCode}). {capture}"); }
                    await Send(new("started", profile.Id, Id: id));
                    _ = Task.Run(async () => { try { await current.WaitForExitAsync(); if (ReferenceEquals(engine, current)) await Send(new("exited", current.ExitCode.ToString())); } catch { } });
                }
                catch (Exception ex) { AppLog.Write("Worker command", ex.ToString()); Stop(); await Send(new("error", ex.Message, Id: id)); }
            }
        }
        catch (Exception ex) { AppLog.Write("Worker", ex.ToString()); }
        finally { Stop(); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}

public sealed class KillJob : IDisposable
{
    private readonly SafeFileHandle handle;
    public KillJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(); }
    }
    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(handle, process.Handle)) { try { process.Kill(true); } catch { } throw new System.ComponentModel.Win32Exception(); }
    }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
