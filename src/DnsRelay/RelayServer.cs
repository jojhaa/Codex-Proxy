// Copyright (c) Time Silent. SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CodexProxy.Dns;

// 只监听随机回环端口，不安装证书、不解密 TLS、不改系统代理/DNS。
public sealed class RelayServer : IAsyncDisposable
{
    private readonly RelaySettings settings;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolver;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim slots = new(128);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Task> clients = new();
    private long nextId;
    private Task? acceptLoop;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public RelayServer(RelaySettings settings, Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    { this.settings = settings; this.resolver = resolver ?? ((host, ct) => System.Net.Dns.GetHostAddressesAsync(host, ct)); }
    public void Start() { listener.Start(128); acceptLoop = Accept(); }
    private async Task Accept()
    {
        try {
            while (!lifetime.IsCancellationRequested) {
                await slots.WaitAsync(lifetime.Token);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(lifetime.Token); }
                catch { slots.Release(); throw; }
                long id = Interlocked.Increment(ref nextId);
                var task = Handle(client);
                clients[id] = task;
                _ = task.ContinueWith(_ => { clients.TryRemove(id, out var ignored); slots.Release(); }, TaskScheduler.Default);
            }
        } catch (OperationCanceledException) { }
    }
    public static async Task<string> ReadHeader(Stream stream, CancellationToken ct)
    {
        var bytes = new List<byte>(); var one = new byte[1];
        while (bytes.Count < 32768) {
            if (await stream.ReadAsync(one, ct) == 0) throw new IOException("请求头不完整。");
            bytes.Add(one[0]);
            int n = bytes.Count;
            if (n >= 4 && bytes[n-4] == 13 && bytes[n-3] == 10 && bytes[n-2] == 13 && bytes[n-1] == 10)
                return Encoding.ASCII.GetString(bytes.ToArray());
        }
        throw new IOException("请求头超限。");
    }
    private static string Authority(string host, int port) => (host.Contains(':') ? $"[{host}]" : host) + $":{port}";
    private static async Task ForwardBody(string header, Stream input, Stream output, CancellationToken ct)
    {
        var lines = header.Split("\r\n");
        var lengths = lines.Where(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var encodings = lines.Where(x => x.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (lengths.Length > 1 || encodings.Length > 1 || (lengths.Length > 0 && encodings.Length > 0))
            throw new IOException("请求体长度有歧义。");
        async Task CopyExactly(long count) {
            var buffer = new byte[16384];
            while (count > 0) {
                int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), ct);
                if (read == 0) throw new IOException("请求体不完整。");
                await output.WriteAsync(buffer.AsMemory(0, read), ct); count -= read;
            }
        }
        if (encodings.Length == 0) {
            if (lengths.Length == 0) return;
            if (!long.TryParse(lengths[0].Split(':', 2)[1].Trim(), out long count) || count < 0) throw new IOException("请求长度无效。");
            await CopyExactly(count); return;
        }
        if (!encodings[0].Split(':', 2)[1].Trim().Equals("chunked", StringComparison.OrdinalIgnoreCase)) throw new IOException("不支持的分块编码。");
        async Task<string> Line() {
            var bytes = new List<byte>(); var one = new byte[1];
            while (bytes.Count < 8192) {
                await input.ReadExactlyAsync(one, ct); bytes.Add(one[0]);
                if (bytes.Count >= 2 && bytes[^2] == 13 && bytes[^1] == 10) return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw new IOException("分块头超限。");
        }
        while (true) {
            string line = await Line();
            if (!long.TryParse(line.Trim().Split(';')[0], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out long size) || size < 0) throw new IOException("分块长度无效。");
            await output.WriteAsync(Encoding.ASCII.GetBytes(line), ct);
            if (size == 0) {
                int trailerBytes = 0;
                do { line = await Line(); trailerBytes += line.Length; if (trailerBytes > 32768) throw new IOException("尾部字段超限。");
                    await output.WriteAsync(Encoding.ASCII.GetBytes(line), ct);
                } while (line != "\r\n");
                return;
            }
            await CopyExactly(size);
            line = await Line(); if (line != "\r\n") throw new IOException("分块结束无效。");
            await output.WriteAsync("\r\n"u8.ToArray(), ct);
        }
    }
    private async Task<TcpClient> OpenTunnel(string target, int port, CancellationToken ct)
    {
        var upstream = new TcpClient();
        try {
            await upstream.ConnectAsync(settings.Host, settings.Port, ct);
            var stream = upstream.GetStream();
            if (settings.Type == "http") {
                var authority = Authority(target, port);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n\r\n"), ct);
                var response = (await ReadHeader(stream, ct)).Split("\r\n")[0].Split(' ');
                if (response.Length < 2 || response[1] != "200") throw new IOException("上游拒绝 CONNECT。");
            } else {
                await stream.WriteAsync(new byte[] {5,1,0}, ct);
                var auth = new byte[2]; await stream.ReadExactlyAsync(auth, ct);
                if (auth[0] != 5 || auth[1] != 0) throw new IOException("上游 SOCKS5 认证不支持。");
                var request = new List<byte> {5,1,0};
                if (IPAddress.TryParse(target, out var ip)) {
                    request.Add((byte)(ip.AddressFamily == AddressFamily.InterNetwork ? 1 : 4)); request.AddRange(ip.GetAddressBytes());
                } else {
                    var domain = Encoding.ASCII.GetBytes(target);
                    if (domain.Length is 0 or > 255) throw new IOException("目标域名无效。");
                    request.Add(3); request.Add((byte)domain.Length); request.AddRange(domain);
                }
                request.Add((byte)(port >> 8)); request.Add((byte)port);
                await stream.WriteAsync(request.ToArray(), ct);
                var reply = new byte[4]; await stream.ReadExactlyAsync(reply, ct);
                if (reply[0] != 5 || reply[1] != 0 || reply[2] != 0) throw new IOException("上游 SOCKS5 连接失败。");
                int length = reply[3] switch {1 => 4, 4 => 16, 3 => 0, _ => throw new IOException("SOCKS5 地址类型无效。")};
                if (reply[3] == 3) { var size = new byte[1]; await stream.ReadExactlyAsync(size, ct); length = size[0]; }
                await stream.ReadExactlyAsync(new byte[length + 2], ct);
            }
            return upstream;
        } catch { upstream.Dispose(); throw; }
    }
    private async Task Handle(TcpClient client)
    {
        using (client) {
            var downstream = client.GetStream(); bool established = false;
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            setup.CancelAfter(TimeSpan.FromSeconds(15));
            try {
                var header = await ReadHeader(downstream, setup.Token);
                var first = header.Split("\r\n")[0].Split(' ');
                if (first.Length != 3 || !first[2].StartsWith("HTTP/1.")) throw new IOException("HTTP 代理请求无效。");
                bool connect = first[0] == "CONNECT";
                if (connect && first[1].IndexOfAny(['/','?','#','@']) >= 0) throw new IOException("CONNECT 目标无效。");
                Uri uri;
                if (!Uri.TryCreate(connect ? "http://" + first[1] : first[1], UriKind.Absolute, out uri!) ||
                    uri.Scheme != "http" || uri.UserInfo.Length != 0 || uri.Port is < 1 or > 65535)
                    throw new IOException("目标地址无效。");
                var targets = await settings.Policy.Targets(uri.IdnHost, resolver, setup.Token);
                TcpClient? selected = null;
                string chosenTarget = "";
                foreach (var target in targets) {
                    try {
                        if (!connect && settings.Type == "http") {
                            var plain = new TcpClient();
                            try { await plain.ConnectAsync(settings.Host, settings.Port, setup.Token); }
                            catch { plain.Dispose(); throw; }
                            selected = plain;
                        } else selected = await OpenTunnel(target, uri.Port, setup.Token);
                        chosenTarget = target; break;
                    }
                    catch (IOException) { }
                    catch (SocketException) { }
                }
                using var upstream = selected ?? throw new IOException("目标连接失败。");
                BridgeEventNotifier.RecordConnection(uri.IdnHost, uri.Port, settings.Type, settings.Policy.UseRemote(uri.IdnHost));
                var remote = upstream.GetStream();
                if (connect) {
                    await downstream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), setup.Token);
                } else {
                    // 使用原始 Host 保留 HTTP 虚拟主机语义；每个明文请求关闭连接，避免复用到其他目标。
                    var lines = header.Split("\r\n").Skip(1).Where(x => x.Length > 0 &&
                        !x.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) &&
                        !x.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) &&
                        !x.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
                    string requestTarget = settings.Type == "http" ? "http://" + Authority(chosenTarget, uri.Port) + uri.PathAndQuery : uri.PathAndQuery;
                    string request = $"{first[0]} {requestTarget} HTTP/1.1\r\nHost: {uri.Authority}\r\nConnection: close\r\n" +
                        string.Concat(lines.Select(line => line + "\r\n")) + "\r\n";
                    await remote.WriteAsync(Encoding.ASCII.GetBytes(request), setup.Token);
                }
                established = true;
                // 单向 EOF 不丢弃反向剩余数据；保持 TLS、WebSocket 和流式响应的原始字节。
                async Task Pump(Stream from, Stream to, TcpClient destination) {
                    try { await from.CopyToAsync(to, lifetime.Token); destination.Client.Shutdown(SocketShutdown.Send); }
                    catch (OperationCanceledException) { }
                    catch { client.Dispose(); upstream.Dispose(); }
                }
                if (connect) await Task.WhenAll(Pump(downstream, remote, upstream), Pump(remote, downstream, client));
                else {
                    // 每个明文请求单独决策，不能把后续 pipelined 请求原样送给上游绕过 DNS 名单。
                    async Task SendBody() {
                        try { await ForwardBody(header, downstream, remote, lifetime.Token); upstream.Client.Shutdown(SocketShutdown.Send); }
                        catch { client.Dispose(); upstream.Dispose(); }
                    }
                    await Task.WhenAll(SendBody(), Pump(remote, downstream, client));
                    // 丢弃客户端已排队的其他请求，避免带未读数据关闭导致 RST 丢失当前响应。
                    var discard = new byte[16384]; int drained = 0;
                    while (client.Connected && client.Available > 0 && drained < 32768) {
                        int read = await downstream.ReadAsync(discard.AsMemory(0, Math.Min(discard.Length, client.Available)), lifetime.Token);
                        if (read == 0) break; drained += read;
                    }
                }
            } catch {
                if (!established && !lifetime.IsCancellationRequested) {
                    try { using var timeout = new CancellationTokenSource(1000);
                        await downstream.WriteAsync("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), timeout.Token); }
                    catch { }
                }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); listener.Stop();
        if (acceptLoop != null) { try { await acceptLoop; } catch (SocketException) { } }
        await Task.WhenAll(clients.Values);
        lifetime.Dispose();
    }
}

internal static class BridgeEventNotifier
{
    private static readonly IPEndPoint? targetEndpoint;
    private static readonly Socket? udpSocket;
    private static int loopbackCount;
    private static volatile bool fullLogs;
    private static readonly Timer summaryTimer;

    static BridgeEventNotifier()
    {
        try {
            udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            targetEndpoint = new IPEndPoint(IPAddress.Loopback, 52252);
        } catch { }
        summaryTimer = new Timer(_ => {
            try {
                string marker = Path.Combine(AppContext.BaseDirectory, "full-logs.enabled");
                fullLogs = false;
                if (File.Exists(marker) && int.TryParse(File.ReadAllText(marker), out int pid)) {
                    using var manager = System.Diagnostics.Process.GetProcessById(pid);
                    fullLogs = !manager.HasExited;
                }
            } catch { fullLogs = false; }
            int count = Interlocked.Exchange(ref loopbackCount, 0);
            if (count > 0) Notify($"[日志汇总] 本地回环通信 {count} 次");
        }, null, 1000, 1000);
    }

    public static void Notify(string message)
    {
        if (udpSocket == null || targetEndpoint == null) return;
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            udpSocket.SendTo(bytes, targetEndpoint);
        } catch { }
    }

    public static void RecordConnection(string host, int port, string proxyType, bool isRemoteDns)
    {
        bool isLoopback = host is "127.0.0.1" or "localhost" or "::1";
        if (isLoopback && !fullLogs) {
            Interlocked.Increment(ref loopbackCount);
            return;
        }

        string dnsTag = isRemoteDns ? " [远端DNS]" : "";
        Notify($"[代理转发] 目标: {host}:{port}{dnsTag} -> 转发至 {proxyType.ToUpperInvariant()} 代理 ✓");
    }
}
